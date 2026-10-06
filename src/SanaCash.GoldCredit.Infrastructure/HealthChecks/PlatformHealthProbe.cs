using Confluent.Kafka;
using Microsoft.Extensions.Options;
using Npgsql;
using SanaCash.GoldCredit.Application.Abstractions.Common;
using SanaCash.GoldCredit.Application.Abstractions.Health;
using SanaCash.GoldCredit.Application.Abstractions.Pricing;
using SanaCash.GoldCredit.Domain.Pricing;
using SanaCash.GoldCredit.Domain.Pricing.Enums;
using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Infrastructure.Messaging;

namespace SanaCash.GoldCredit.Infrastructure.HealthChecks;

public class PlatformHealthProbe(
    NpgsqlDataSource dataSource,
    IOptions<KafkaOptions> kafkaOptions,
    IReferencePriceProvider referencePrices,
    IClock clock,
    FeedStalenessPolicy stalenessPolicy) : IPlatformHealthProbe
{
    public async Task<PlatformHealth> CheckAsync(CancellationToken cancellationToken = default)
    {
        var databaseHealthy = await CheckDatabaseAsync(cancellationToken);
        var kafkaHealthy = CheckKafka();
        var priceFeedHealthy = await CheckPriceFeedAsync(cancellationToken);
        return new PlatformHealth(databaseHealthy, kafkaHealthy, priceFeedHealthy);
    }

    private async Task<bool> CheckDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1;", connection);
            return (int)(await command.ExecuteScalarAsync(cancellationToken))! == 1;
        }
        catch
        {
            return false;
        }
    }

    private bool CheckKafka()
    {
        try
        {
            using var admin = new AdminClientBuilder(new AdminClientConfig
            {
                BootstrapServers = kafkaOptions.Value.BootstrapServers
            }).Build();
            admin.GetMetadata(TimeSpan.FromSeconds(2));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> CheckPriceFeedAsync(CancellationToken cancellationToken)
    {
        try
        {
            var quote = await referencePrices.GetLatestAsync(Instrument.Xau750, cancellationToken);
            return quote is not null
                && stalenessPolicy.Evaluate(quote.TimestampUtc, clock.UtcNow) == FeedState.Live;
        }
        catch
        {
            return false;
        }
    }
}
