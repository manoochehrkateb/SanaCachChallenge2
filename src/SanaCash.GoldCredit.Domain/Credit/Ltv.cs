using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Credit;

public sealed record Ltv : ValueObject
{
    private Ltv(Int128 basisPoints, bool isInfinite)
    {
        BasisPoints = basisPoints;
        IsInfinite = isInfinite;
    }

    public Int128 BasisPoints { get; }
    public bool IsInfinite { get; }
    public static Ltv Zero { get; } = new(0, false);
    public static Ltv Infinite { get; } = new(0, true);

    public static Ltv Calculate(Irr debt, CollateralValue collateralValue)
    {
        if (debt.Value == 0)
        {
            return Zero;
        }

        if (collateralValue.Irr == 0)
        {
            return Infinite;
        }

        var numerator = (Int128)debt.Value * 10_000;
        var denominator = collateralValue.Irr;
        var roundedUp = (numerator + denominator - 1) / denominator;
        return new Ltv(roundedUp, false);
    }

    public bool Exceeds(int basisPoints) => IsInfinite || BasisPoints > basisPoints;
}