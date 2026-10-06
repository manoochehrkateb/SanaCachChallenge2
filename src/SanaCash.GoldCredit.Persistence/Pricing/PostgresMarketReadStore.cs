using Microsoft.EntityFrameworkCore;
using SanaCash.GoldCredit.Application.Abstractions.Data;
using SanaCash.GoldCredit.Application.Abstractions.Common;
using SanaCash.GoldCredit.Domain.Pricing;
using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Persistence.Common;

namespace SanaCash.GoldCredit.Persistence.Pricing;

public class PostgresMarketReadStore(
    GoldCreditDbContext context,
    PostgresSession session,
    IClock clock,
    FeedStalenessPolicy stalenessPolicy) : GenericRepository<PriceCandleEntity>(context, session), IMarketReadStore
{
    public async Task<IReadOnlyList<PriceCandleReadModel>> GetCandlesAsync(
        Instrument instrument,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        var start = MinuteStart(fromUtc);
        var end = MinuteStart(toUtc);
        if (end < toUtc)
        {
            end = end.AddMinutes(1);
        }

        var candles = await Entities
            .AsNoTracking()
            .Where(item => item.Instrument == instrument.Code && item.Bucket >= start && item.Bucket < end)
            .OrderBy(item => item.Bucket)
            .ToDictionaryAsync(
                item => item.Bucket.ToUniversalTime(),
                item => new PriceCandleReadModel(
                    item.Bucket.ToUniversalTime(),
                    item.OpenIrrPerGram,
                    item.HighIrrPerGram,
                    item.LowIrrPerGram,
                    item.CloseIrrPerGram,
                    item.TickCount,
                    false),
                cancellationToken);

        var previousClose = await Entities
            .AsNoTracking()
            .Where(item => item.Instrument == instrument.Code && item.Bucket < start)
            .OrderByDescending(item => item.Bucket)
            .Select(item => (long?)item.CloseIrrPerGram)
            .FirstOrDefaultAsync(cancellationToken);

        var latestRealTickUtc = await context.Set<PriceTickEntity>()
            .AsNoTracking()
            .Where(item => item.Instrument == instrument.Code)
            .Select(item => (DateTimeOffset?)item.Timestamp)
            .MaxAsync(cancellationToken);
        var canCarryForward = stalenessPolicy.Evaluate(latestRealTickUtc, clock.UtcNow)
            == Domain.Pricing.Enums.FeedState.Live;

        var results = new List<PriceCandleReadModel>();
        for (var minute = start; minute < end; minute = minute.AddMinutes(1))
        {
            if (candles.TryGetValue(minute, out var candle))
            {
                results.Add(candle);
                previousClose = candle.CloseIrr;
                continue;
            }

            var carriedClose = canCarryForward ? previousClose : null;
            results.Add(new PriceCandleReadModel(
                minute, carriedClose, carriedClose, carriedClose, carriedClose, 0, carriedClose is not null));
        }

        return results;
    }

    private static DateTimeOffset MinuteStart(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero);
    }
}
