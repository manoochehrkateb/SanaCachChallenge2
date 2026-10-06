using SanaCash.GoldCredit.Domain.Pricing.Enums;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Pricing;

public class RejectedPriceTick : Entity<Guid>
{
    private RejectedPriceTick(Guid id, string instrument, long sequence, long price, DateTimeOffset timestamp,
        TickRejectionReason reason, string? rawPayload = null) : base(id)
    {
        Instrument = instrument;
        Sequence = sequence;
        Price = price;
        Timestamp = timestamp;
        Reason = reason;
        RawPayload = rawPayload;
    }

    public string Instrument { get; }
    public long Sequence { get; }
    public long Price { get; }
    public DateTimeOffset Timestamp { get; }
    public TickRejectionReason Reason { get; }
    public string? RawPayload { get; }

    public static RejectedPriceTick Record(string instrument, long sequence, long price,
        DateTimeOffset timestamp, TickRejectionReason reason) =>
        new(Guid.NewGuid(), instrument, sequence, price, timestamp, reason);

    public static RejectedPriceTick RecordMalformed(string rawPayload, DateTimeOffset receivedAtUtc) =>
        new(Guid.NewGuid(), "(malformed)", -1, 0, receivedAtUtc.ToUniversalTime(),
            TickRejectionReason.InvalidPayload, rawPayload);
}
