namespace SanaCash.GoldCredit.Application.IntegrationEvents;

public sealed record MarginSnapshotV1(
    DateTimeOffset MinuteStartUtc,
    long ReferencePriceIrrPerGram,
    long DebtIrr,
    long CollateralFineMg,
    long CollateralValueIrr,
    long LtvBps,
    bool IsLtvInfinite);