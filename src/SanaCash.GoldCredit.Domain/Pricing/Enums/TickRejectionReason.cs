namespace SanaCash.GoldCredit.Domain.Pricing.Enums;

public enum TickRejectionReason
{
    NonPositivePrice,
    UnknownInstrument,
    TimestampTooFarAhead,
    InvalidSequence,
    InvalidPayload
}