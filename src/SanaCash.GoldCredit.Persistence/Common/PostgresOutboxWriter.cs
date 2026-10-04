using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using SanaCash.GoldCredit.Application.Abstractions.Data;

namespace SanaCash.GoldCredit.Persistence.Common;

public sealed class PostgresOutboxWriter(PostgresSession session) : IOutboxWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task AddAsync<TIntegrationEvent>(
        Guid eventId,
        string partitionKey,
        TIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
        where TIntegrationEvent : notnull
    {
        await using var command = new NpgsqlCommand(
            "INSERT INTO platform.outbox_messages (event_id, event_type, aggregate_id, payload, occurred_at) " +
            "VALUES (@event_id, @event_type, @aggregate_id, @payload, @occurred_at);",
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("event_type", typeof(TIntegrationEvent).Name);
        command.Parameters.AddWithValue("aggregate_id", partitionKey);
        command.Parameters.Add("payload", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(integrationEvent, JsonOptions);
        command.Parameters.AddWithValue("occurred_at", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}