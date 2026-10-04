namespace SanaCash.GoldCredit.Domain.Shared.Primitives;

public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAtUtc { get; }
}