using Npgsql;
using SanaCash.GoldCredit.Application.Abstractions.Data;
using SanaCash.GoldCredit.Domain.Shared;

namespace SanaCash.GoldCredit.Persistence.Pricing;

public sealed class PostgresMarketReadStore(NpgsqlDataSource dataSource) : IMarketReadStore
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

        var candles = await LoadCandlesAsync(instrument, start, end, cancellationToken);
        var previousClose = await LoadPreviousCloseAsync(instrument, start, cancellationToken);
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

    private async Task<Dictionary<DateTimeOffset, PriceCandleReadModel>> LoadCandlesAsync(
        Instrument instrument,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT bucket, open_irr_per_gram, high_irr_per_gram, low_irr_per_gram, " +
            "close_irr_per_gram, tick_count FROM pricing.price_candles_1m " +
            "WHERE instrument = @instrument AND bucket >= @from AND bucket < @to ORDER BY bucket;",
            connection);
        command.Parameters.AddWithValue("instrument", instrument.Code);
        command.Parameters.AddWithValue("from", fromUtc);
        command.Parameters.AddWithValue("to", toUtc);

        var results = new Dictionary<DateTimeOffset, PriceCandleReadModel>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var minute = reader.GetFieldValue<DateTimeOffset>(0).ToUniversalTime();
            var open = reader.GetInt64(1);
            var high = reader.GetInt64(2);
            var low = reader.GetInt64(3);
            var close = reader.GetInt64(4);
            results.Add(minute, new PriceCandleReadModel(minute, open, high, low, close, reader.GetInt64(5), false));
        }

        return results;
    }

    private async Task<long?> LoadPreviousCloseAsync(
        Instrument instrument,
        DateTimeOffset beforeUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT close_irr_per_gram FROM pricing.price_candles_1m " +
            "WHERE instrument = @instrument AND bucket < @before ORDER BY bucket DESC LIMIT 1;",
            connection);
        command.Parameters.AddWithValue("instrument", instrument.Code);
        command.Parameters.AddWithValue("before", beforeUtc);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is long close ? close : null;
    }

    private static DateTimeOffset MinuteStart(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero);
    }
}