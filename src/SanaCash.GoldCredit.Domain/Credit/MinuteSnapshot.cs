using SanaCash.GoldCredit.Domain.Pricing;
using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Credit;

public sealed record MinuteSnapshot : ValueObject
{
    public MinuteSnapshot(MinuteBucket minute, ReferencePrice close, Irr debt,
        FineWeightMg collateral, CollateralValue value, Ltv ltv)
    {
        Minute = minute;
        Close = close;
        Debt = debt;
        Collateral = collateral;
        Value = value;
        Ltv = ltv;
    }

    public MinuteBucket Minute { get; }
    public ReferencePrice Close { get; }
    public Irr Debt { get; }
    public FineWeightMg Collateral { get; }
    public CollateralValue Value { get; }
    public Ltv Ltv { get; }
}