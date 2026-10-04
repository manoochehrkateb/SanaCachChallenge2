namespace SanaCash.GoldCredit.Presentation.Contracts.Credit;

public sealed record FacilityResponse(
    Guid FacilityId,
    long CollateralFineMg,
    long DebtIrr,
    string Status,
    long? LtvBps,
    bool IsLtvInfinite,
    DateTimeOffset? PriceTimestampUtc);