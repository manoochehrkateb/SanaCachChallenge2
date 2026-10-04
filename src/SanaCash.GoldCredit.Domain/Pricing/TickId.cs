using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Pricing;

public sealed record TickId : ValueObject
{
    private TickId(Instrument instrument, long sequence)
    {
        Instrument = instrument;
        Sequence = sequence;
    }

    public Instrument Instrument { get; }
    public long Sequence { get; }

    public static Result<TickId> Create(Instrument instrument, long sequence) => sequence < 0
        ? Result<TickId>.Failure(new Error("InvalidTickSequence", "Tick sequence cannot be negative."))
        : Result<TickId>.Success(new TickId(instrument, sequence));
}