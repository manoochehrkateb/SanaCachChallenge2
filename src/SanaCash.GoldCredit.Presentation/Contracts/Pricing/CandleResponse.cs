namespace SanaCash.GoldCredit.Presentation.Contracts.Pricing;

public sealed record CandleResponse(
    DateTimeOffset MinuteStartUtc,
    long? OpenIrr,
    long? HighIrr,
    long? LowIrr,
    long? CloseIrr,
    long TickCount,
    bool CarriedForward);