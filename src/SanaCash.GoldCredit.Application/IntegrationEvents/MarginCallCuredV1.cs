namespace SanaCash.GoldCredit.Application.IntegrationEvents;

public sealed record MarginCallCuredV1(
    Guid EventId,
    Guid FacilityId,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyList<MarginSnapshotV1> Evidence);