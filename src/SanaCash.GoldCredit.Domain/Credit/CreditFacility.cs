using SanaCash.GoldCredit.Domain.Credit.Enums;
using SanaCash.GoldCredit.Domain.Credit.Events;
using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Credit;

public class CreditFacility : AggregateRoot<FacilityId>
{
    private CreditFacility(FacilityId id, ClientId clientId) : base(id)
    {
        ClientId = clientId;
        Instrument = Instrument.Xau750;
        Collateral = FineWeightMg.Zero;
        Debt = Irr.Zero;
        Status = FacilityStatus.Healthy;
    }

    public ClientId ClientId { get; }
    public Instrument Instrument { get; }
    public FineWeightMg Collateral { get; private set; }
    public Irr Debt { get; private set; }
    public FacilityStatus Status { get; private set; }

    public static CreditFacility Create(FacilityId id, ClientId clientId) => new(id, clientId);

    public static CreditFacility Restore(
        FacilityId id,
        ClientId clientId,
        FineWeightMg collateral,
        Irr debt,
        FacilityStatus status,
        long version)
    {
        var facility = new CreditFacility(id, clientId)
        {
            Collateral = collateral,
            Debt = debt,
            Status = status
        };
        facility.RestoreVersion(version);
        return facility;
    }

    public Result Pledge(FineWeightMg amount, DateTimeOffset occurredAtUtc)
    {
        if (Status == FacilityStatus.LiquidationRequired)
        {
            return Result.Failure(new Error("FacilityLiquidationRequired", "Collateral cannot be pledged after liquidation is required."));
        }

        if (amount.Value <= 0)
        {
            return Result.Failure(new Error("InvalidPledgeAmount", "Pledge amount must be positive."));
        }

        var updated = Collateral.Add(amount);
        if (updated.IsFailure)
        {
            return Result.Failure(updated.Error);
        }

        Collateral = updated.Value;
        MarkChanged();
        Raise(new CollateralPledged(Guid.NewGuid(), Id, amount, occurredAtUtc.ToUniversalTime()));
        return Result.Success();
    }

    public Result Release(FineWeightMg amount, ReferencePrice price, DateTimeOffset occurredAtUtc)
    {
        if (Status != FacilityStatus.Healthy)
        {
            return Result.Failure(new Error("FacilityNotHealthy", "Collateral release requires a healthy facility."));
        }

        if (amount.Value <= 0)
        {
            return Result.Failure(new Error("InvalidReleaseAmount", "Release amount must be positive."));
        }

        var remaining = Collateral.Subtract(amount);
        if (remaining.IsFailure)
        {
            return Result.Failure(new Error("ReleaseExceedsPledged", "Release amount exceeds pledged collateral."));
        }

        var collateralValue = CollateralValue.Calculate(remaining.Value, price);
        if (collateralValue.IsFailure)
        {
            return Result.Failure(collateralValue.Error);
        }

        if (Ltv.Calculate(Debt, collateralValue.Value).Exceeds(MarginThresholds.MaximumClientLtvBps))
        {
            return Result.Failure(new Error("LtvLimitExceeded", "Release would exceed the maximum client LTV."));
        }

        Collateral = remaining.Value;
        MarkChanged();
        Raise(new CollateralReleased(Guid.NewGuid(), Id, amount, occurredAtUtc.ToUniversalTime()));
        return Result.Success();
    }

    public Result Drawdown(Irr amount, ReferencePrice price, DateTimeOffset occurredAtUtc)
    {
        if (Status != FacilityStatus.Healthy)
        {
            return Result.Failure(new Error("FacilityNotHealthy", "Drawdown requires a healthy facility."));
        }

        if (amount.Value <= 0)
        {
            return Result.Failure(new Error("InvalidDrawdownAmount", "Drawdown amount must be positive."));
        }

        var updatedDebt = Debt.Add(amount);
        if (updatedDebt.IsFailure)
        {
            return Result.Failure(updatedDebt.Error);
        }

        var collateralValue = CollateralValue.Calculate(Collateral, price);
        if (collateralValue.IsFailure)
        {
            return Result.Failure(collateralValue.Error);
        }

        if (Ltv.Calculate(updatedDebt.Value, collateralValue.Value).Exceeds(MarginThresholds.MaximumClientLtvBps))
        {
            return Result.Failure(new Error("LtvLimitExceeded", "Drawdown would exceed the maximum client LTV."));
        }

        Debt = updatedDebt.Value;
        MarkChanged();
        Raise(new DrawdownExecuted(Guid.NewGuid(), Id, amount, occurredAtUtc.ToUniversalTime()));
        return Result.Success();
    }

    public Result Repay(Irr amount, DateTimeOffset occurredAtUtc)
    {
        if (amount.Value <= 0)
        {
            return Result.Failure(new Error("InvalidRepaymentAmount", "Repayment amount must be positive."));
        }

        var updatedDebt = Debt.Subtract(amount);
        if (updatedDebt.IsFailure)
        {
            return Result.Failure(new Error("RepaymentExceedsDebt", "Repayment exceeds outstanding debt."));
        }

        Debt = updatedDebt.Value;
        MarkChanged();
        Raise(new RepaymentReceived(Guid.NewGuid(), Id, amount, occurredAtUtc.ToUniversalTime()));
        return Result.Success();
    }

    public Result ApplyMarginDecision(FacilityStatus status, MarginEvidence evidence, DateTimeOffset occurredAtUtc)
    {
        if (Status == FacilityStatus.LiquidationRequired && status != FacilityStatus.LiquidationRequired)
        {
            return Result.Failure(new Error("InvalidFacilityTransition", "LiquidationRequired is terminal."));
        }

        if (Status == status)
        {
            return Result.Success();
        }

        IDomainEvent domainEvent = (Status, status) switch
        {
            (FacilityStatus.Healthy, FacilityStatus.MarginCall) =>
                new MarginCallIssued(Guid.NewGuid(), Id, evidence, occurredAtUtc.ToUniversalTime()),
            (FacilityStatus.MarginCall, FacilityStatus.Healthy) =>
                new MarginCallCured(Guid.NewGuid(), Id, evidence, occurredAtUtc.ToUniversalTime()),
            (FacilityStatus.Healthy or FacilityStatus.MarginCall, FacilityStatus.LiquidationRequired) =>
                new LiquidationRequired(Guid.NewGuid(), Id, evidence, occurredAtUtc.ToUniversalTime()),
            _ => null!
        };

        if (domainEvent is null)
        {
            return Result.Failure(new Error("InvalidFacilityTransition", "Requested facility status transition is not allowed."));
        }

        Status = status;
        MarkChanged();
        Raise(domainEvent);
        return Result.Success();
    }
}
