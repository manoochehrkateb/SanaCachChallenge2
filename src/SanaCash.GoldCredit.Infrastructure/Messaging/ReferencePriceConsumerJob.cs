using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SanaCash.GoldCredit.Application.Pricing.IngestPriceTick;
using SanaCash.GoldCredit.Domain.Pricing;

namespace SanaCash.GoldCredit.Infrastructure.Messaging;

public class ReferencePriceConsumerJob(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    ILogger<ReferencePriceConsumerJob> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Run(
        () => ConsumeAsync(stoppingToken), stoppingToken);

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            GroupId = options.Value.ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(options.Value.ReferencePricesTopic);
        while (!cancellationToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? record = null;
            try
            {
                record = consumer.Consume(cancellationToken);
                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<IngestPriceTickHandler>();
                PriceTickPayload? payload;
                try
                {
                    payload = JsonSerializer.Deserialize<PriceTickPayload>(record.Message.Value);
                }
                catch (JsonException)
                {
                    await handler.RecordMalformedAsync(record.Message.Value ?? string.Empty, cancellationToken);
                    consumer.Commit(record);
                    continue;
                }

                if (payload is null)
                {
                    await handler.RecordMalformedAsync(record.Message.Value ?? string.Empty, cancellationToken);
                    consumer.Commit(record);
                    continue;
                }

                await handler.HandleAsync(payload, cancellationToken);
                consumer.Commit(record);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Price tick ingestion failed; its Kafka offset will not be committed.");
                if (record is not null)
                {
                    consumer.Seek(record.TopicPartitionOffset);
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }
}
