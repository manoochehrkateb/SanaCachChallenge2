using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Pricing;

public sealed record MinuteClose : ValueObject
{
    public MinuteClose(MinuteBucket bucket, ReferencePrice close, bool carriedForward)
    {
        Bucket = bucket;
        Close = close;
        CarriedForward = carriedForward;
    }

    public MinuteBucket Bucket { get; }
    public ReferencePrice Close { get; }
    public bool CarriedForward { get; }
}