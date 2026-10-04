namespace SanaCash.GoldCredit.Application.Abstractions.Data;

public interface IOutboxWriter
{
    Task AddAsync<TIntegrationEvent>(
        Guid eventId,
        string partitionKey,
        TIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
        where TIntegrationEvent : notnull;
}