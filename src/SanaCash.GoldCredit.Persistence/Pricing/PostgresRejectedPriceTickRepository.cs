using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using SanaCash.GoldCredit.Domain.Pricing;
using SanaCash.GoldCredit.Persistence.Common;

namespace SanaCash.GoldCredit.Persistence.Pricing;

public sealed class PostgresRejectedPriceTickRepository(PostgresSession session) : IRejectedPriceTickRepository
{
    public async Task AddAsync(RejectedPriceTick tick, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            tick.Instrument,
            tick.Sequence,
            tick.Price,
            TimestampUtc = tick.Timestamp,
            tick.RawPayload
        });

        await using var command = new NpgsqlCommand(
            "INSERT INTO pricing.rejected_price_ticks (received_at, instrument, sequence, payload, rejection_reason) " +
            "VALUES (now(), @instrument, @sequence, @payload, @reason);",
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("instrument", (object?)tick.Instrument ?? DBNull.Value);
        command.Parameters.AddWithValue("sequence", tick.Sequence);
        command.Parameters.Add("payload", NpgsqlDbType.Jsonb).Value = payload;
        command.Parameters.AddWithValue("reason", tick.Reason.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}