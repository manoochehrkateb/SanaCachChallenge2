using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Application.IntegrationEvents;

public sealed record MarginCallCuredV1(
    Guid EventId,
    Guid FacilityId,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyList<MarginSnapshotV1> Evidence) : IDomainEvent;
