using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Custody;

public sealed class CustodyHolding : AggregateRoot<CustodyHoldingId>
{
    private CustodyHolding(CustodyHoldingId id, FineWeightMg freeFineWeight) : base(id) => FreeFineWeight = freeFineWeight;

    public FineWeightMg FreeFineWeight { get; private set; }

    public static CustodyHolding Create(CustodyHoldingId id, FineWeightMg freeFineWeight) =>
        new(id, freeFineWeight);

    public static CustodyHolding Restore(CustodyHoldingId id, FineWeightMg freeFineWeight, long version)
    {
        var holding = new CustodyHolding(id, freeFineWeight);
        holding.RestoreVersion(version);
        return holding;
    }

    public Result Debit(FineWeightMg amount)
    {
        if (amount.Value <= 0)
        {
            return Result.Failure(new Error("InvalidPledgeAmount", "Pledge amount must be positive."));
        }

        var remaining = FreeFineWeight.Subtract(amount);
        if (remaining.IsFailure)
        {
            return Result.Failure(new Error("InsufficientFreeGold", "Custody holding does not contain enough free gold."));
        }

        FreeFineWeight = remaining.Value;
        MarkChanged();
        return Result.Success();
    }

    public Result Credit(FineWeightMg amount)
    {
        if (amount.Value <= 0)
        {
            return Result.Failure(new Error("InvalidReleaseAmount", "Release amount must be positive."));
        }

        var updated = FreeFineWeight.Add(amount);
        if (updated.IsFailure)
        {
            return Result.Failure(updated.Error);
        }

        FreeFineWeight = updated.Value;
        MarkChanged();
        return Result.Success();
    }
}