using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SanaCash.GoldCredit.Application.Credit.EvaluateClosedMinute;

namespace SanaCash.GoldCredit.Infrastructure.BackgroundJobs;

public class MarginEvaluatorJob(
    IServiceScopeFactory scopeFactory,
    ILogger<MarginEvaluatorJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<EvaluateClosedMinuteHandler>();
                var result = await handler.EvaluateLatestAsync(stoppingToken);
                if (result.Applied || result.PausedForStaleFeed || result.SkippedGap)
                {
                    logger.LogInformation(
                        "Margin evaluator applied={Applied}, stale={Stale}, gapSkipped={GapSkipped}, facilities={Facilities}, transitions={Transitions}",
                        result.Applied, result.PausedForStaleFeed, result.SkippedGap,
                        result.FacilitiesEvaluated, result.TransitionsProposed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Margin evaluator cycle failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}
