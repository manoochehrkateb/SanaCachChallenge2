namespace SanaCash.GoldCredit.Application.Abstractions.Data;

public sealed record FacilityReadModel(
    Guid FacilityId,
    Guid ClientId,
    long CollateralFineMg,
    long DebtIrr,
    string Status,
    long Version);

public sealed record MarginEventReadModel(
    Guid EventId,
    string EventType,
    int EpisodeNumber,
    DateTimeOffset OccurredAtUtc,
    string EvidenceJson);

public sealed record PriceCandleReadModel(
    DateTimeOffset MinuteStartUtc,
    long? OpenIrr,
    long? HighIrr,
    long? LowIrr,
    long? CloseIrr,
    long TickCount,
    bool CarriedForward);