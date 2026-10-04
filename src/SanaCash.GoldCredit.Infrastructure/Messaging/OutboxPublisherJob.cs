using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SanaCash.GoldCredit.Infrastructure.Messaging;

public sealed class OutboxPublisherJob(
    OutboxPublisher publisher,
    ILogger<OutboxPublisherJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var published = await publisher.PublishBatchAsync(stoppingToken);
                if (published == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox publisher cycle failed.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }
}