using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Credit;

public sealed record CollateralValue : ValueObject
{
    private CollateralValue(long irr) => Irr = irr;

    public long Irr { get; }

    public static CollateralValue Restore(long irr) => irr < 0
        ? throw new ArgumentOutOfRangeException(nameof(irr))
        : new CollateralValue(irr);

    public static Result<CollateralValue> Calculate(FineWeightMg fineWeight, ReferencePrice referencePrice)
    {
        var value = (Int128)fineWeight.Value * referencePrice.IrrPerGram / 750;
        return value > long.MaxValue
            ? Result<CollateralValue>.Failure(new Error("CollateralValueOverflow", "Collateral value exceeds the supported IRR range."))
            : Result<CollateralValue>.Success(new CollateralValue((long)value));
    }
}