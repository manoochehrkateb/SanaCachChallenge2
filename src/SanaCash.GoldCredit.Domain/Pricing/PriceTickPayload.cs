namespace SanaCash.GoldCredit.Domain.Pricing;

public sealed record PriceTickPayload(
    string Instrument,
    long Sequence,
    long Price,
    DateTimeOffset TimestampUtc);