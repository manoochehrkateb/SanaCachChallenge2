using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace SanaCash.GoldCredit.Infrastructure.Messaging;

public class OutboxPublisher(
    NpgsqlDataSource dataSource,
    IProducer<string, string> producer,
    IOptions<KafkaOptions> options,
    ILogger<OutboxPublisher> logger)
{
    public async Task<int> PublishBatchAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var messages = new List<PendingMessage>();
        await using (var select = new NpgsqlCommand(
            "SELECT current.event_id, current.event_type, current.aggregate_id, current.payload::text, current.occurred_at " +
            "FROM platform.outbox_messages AS current " +
            "WHERE current.published_at IS NULL " +
            "AND NOT EXISTS (SELECT 1 FROM platform.outbox_messages AS earlier " +
            "WHERE earlier.aggregate_id = current.aggregate_id AND earlier.published_at IS NULL " +
            "AND (earlier.occurred_at, earlier.event_id) < (current.occurred_at, current.event_id)) " +
            "ORDER BY current.occurred_at, current.event_id " +
            "FOR UPDATE OF current SKIP LOCKED LIMIT 100;",
            connection,
            transaction))
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                messages.Add(new PendingMessage(
                    reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetFieldValue<DateTimeOffset>(4)));
            }
        }

        var published = 0;
        foreach (var message in messages)
        {
            try
            {
                var kafkaMessage = new Message<string, string>
                {
                    Key = message.AggregateId,
                    Value = message.Payload,
                    Headers = new Headers
                    {
                        { "event-id", Encoding.UTF8.GetBytes(message.EventId.ToString("D")) },
                        { "event-type", Encoding.UTF8.GetBytes(message.EventType) }
                    }
                };
                await producer.ProduceAsync(options.Value.FacilityEventsTopic, kafkaMessage, cancellationToken);
                await using var mark = new NpgsqlCommand(
                    "UPDATE platform.outbox_messages SET published_at = now(), attempts = attempts + 1, last_error = NULL " +
                    "WHERE event_id = @event_id;",
                    connection,
                    transaction);
                mark.Parameters.AddWithValue("event_id", message.EventId);
                await mark.ExecuteNonQueryAsync(cancellationToken);
                published++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Outbox publish failed for {EventId} ({EventType})", message.EventId, message.EventType);
                await using var failed = new NpgsqlCommand(
                    "UPDATE platform.outbox_messages SET attempts = attempts + 1, last_error = @error " +
                    "WHERE event_id = @event_id;",
                    connection,
                    transaction);
                failed.Parameters.AddWithValue("error", exception.Message);
                failed.Parameters.AddWithValue("event_id", message.EventId);
                await failed.ExecuteNonQueryAsync(cancellationToken);
                break;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return published;
    }

    private sealed record PendingMessage(
        Guid EventId,
        string EventType,
        string AggregateId,
        string Payload,
        DateTimeOffset OccurredAtUtc);
}
