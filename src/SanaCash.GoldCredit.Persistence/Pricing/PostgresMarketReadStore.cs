using Microsoft.EntityFrameworkCore;
using SanaCash.GoldCredit.Application.Abstractions.Data;
using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Persistence.Common;

namespace SanaCash.GoldCredit.Persistence.Pricing;

public class PostgresMarketReadStore(
    GoldCreditDbContext context,
    PostgresSession session) : GenericRepository<PriceCandleEntity>(context, session), IMarketReadStore
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

        var results = new List<PriceCandleReadModel>();
        for (var minute = start; minute < end; minute = minute.AddMinutes(1))
        {
            if (candles.TryGetValue(minute, out var candle))
            {
                results.Add(candle);
                previousClose = candle.CloseIrr;
                continue;
            }

            results.Add(new PriceCandleReadModel(
                minute, previousClose, previousClose, previousClose, previousClose, 0, previousClose is not null));
        }

        return results;
    }

    private static DateTimeOffset MinuteStart(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero);
    }
}
