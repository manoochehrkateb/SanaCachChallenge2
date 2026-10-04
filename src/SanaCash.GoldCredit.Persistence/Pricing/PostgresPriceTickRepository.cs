using System.Security.Cryptography;
using System.Text;
using Npgsql;
using SanaCash.GoldCredit.Domain.Pricing;
using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Persistence.Common;

namespace SanaCash.GoldCredit.Persistence.Pricing;

public sealed class PostgresPriceTickRepository(PostgresSession session, NpgsqlDataSource dataSource) : IPriceTickRepository
{
    public async Task<bool> TryAddAsync(PriceTick tick, CancellationToken cancellationToken = default)
    {
        var instrument = tick.Id.Instrument.Code;
        var payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{instrument}\n{tick.Id.Sequence}\n{tick.Price.IrrPerGram}\n{tick.TimestampUtc:O}"));

        await using var keyCommand = new NpgsqlCommand(
            "INSERT INTO pricing.price_tick_keys (instrument, sequence, payload_hash) " +
            "VALUES (@instrument, @sequence, @payload_hash) " +
            "ON CONFLICT (instrument, sequence) DO NOTHING;",
            session.Connection,
            session.Transaction);
        keyCommand.Parameters.AddWithValue("instrument", instrument);
        keyCommand.Parameters.AddWithValue("sequence", tick.Id.Sequence);
        keyCommand.Parameters.AddWithValue("payload_hash", payloadHash);

        if (await keyCommand.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            await using var existing = new NpgsqlCommand(
                "SELECT payload_hash FROM pricing.price_tick_keys WHERE instrument = @instrument AND sequence = @sequence;",
                session.Connection,
                session.Transaction);
            existing.Parameters.AddWithValue("instrument", instrument);
            existing.Parameters.AddWithValue("sequence", tick.Id.Sequence);
            var existingHash = (byte[]?)await existing.ExecuteScalarAsync(cancellationToken);
            if (existingHash is not null && !CryptographicOperations.FixedTimeEquals(existingHash, payloadHash))
            {
                throw new InvalidOperationException("A price tick identity was reused with a different payload.");
            }

            return false;
        }

        await using var tickCommand = new NpgsqlCommand(
            "INSERT INTO pricing.price_ticks (ts, instrument, sequence, price_irr_per_gram) " +
            "VALUES (@ts, @instrument, @sequence, @price);",
            session.Connection,
            session.Transaction);
        tickCommand.Parameters.AddWithValue("ts", tick.TimestampUtc);
        tickCommand.Parameters.AddWithValue("instrument", instrument);
        tickCommand.Parameters.AddWithValue("sequence", tick.Id.Sequence);
        tickCommand.Parameters.AddWithValue("price", tick.Price.IrrPerGram);
        await tickCommand.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }

    public async Task<PriceTick?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT ts, instrument, sequence, price_irr_per_gram FROM pricing.price_ticks " +
            "ORDER BY ts DESC LIMIT 1;",
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var instrument = Instrument.Create(reader.GetString(1));
        if (instrument.IsFailure)
        {
            throw new InvalidOperationException("Persisted price tick contains an unknown instrument.");
        }

        var tickId = TickId.Create(instrument.Value, reader.GetInt64(2));
        var price = ReferencePrice.Create(reader.GetInt64(3));
        if (tickId.IsFailure || price.IsFailure)
        {
            throw new InvalidOperationException("Persisted price tick contains invalid domain values.");
        }

        return PriceTick.Create(tickId.Value, price.Value, reader.GetFieldValue<DateTimeOffset>(0));
    }
}