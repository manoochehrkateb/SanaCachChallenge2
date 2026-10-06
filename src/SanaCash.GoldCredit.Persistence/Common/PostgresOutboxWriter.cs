using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SanaCash.GoldCredit.Application.Abstractions.Data;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Persistence.Common;

public class PostgresOutboxWriter(
    GoldCreditDbContext context,
    PostgresSession session) : GenericRepository<OutboxMessage>(context, session), IOutboxWriter
{
    public PostgresOutboxWriter(PostgresSession session)
        : this(CreateContext(session), session)
    {
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task AddAsync<TIntegrationEvent>(
        Guid eventId,
        string partitionKey,
        TIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
        where TIntegrationEvent : notnull
    {
        var entity = new OutboxMessage
        {
            EventId = eventId,
            EventType = typeof(TIntegrationEvent).Name,
            AggregateId = partitionKey,
            Payload = JsonSerializer.Serialize(integrationEvent, JsonOptions),
            OccurredAt = integrationEvent is IDomainEvent domainEvent
                ? domainEvent.OccurredAtUtc.ToUniversalTime()
                : DateTimeOffset.UtcNow,
            Attempts = 0
        };

        await AddAsync(entity, cancellationToken);
        await SaveChangesAsync(cancellationToken);
    }

    private static GoldCreditDbContext CreateContext(PostgresSession session)
    {
        var context = new GoldCreditDbContext(
            new DbContextOptionsBuilder<GoldCreditDbContext>()
                .UseNpgsql(session.Connection)
                .Options);
        context.Database.UseTransaction(session.Transaction);
        return context;
    }
}
