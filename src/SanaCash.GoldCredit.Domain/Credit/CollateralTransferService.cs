using SanaCash.GoldCredit.Domain.Custody;
using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Credit;

public sealed class CollateralTransferService
{
    public Result ValidatePledge(CustodyHolding holding, CreditFacility facility, FineWeightMg amount)
    {
        if (holding.Id.ClientId != facility.ClientId || holding.Id.Instrument != facility.Instrument)
        {
            return Result.Failure(new Error("CollateralOwnerMismatch", "Custody holding and facility must belong to the same client and instrument."));
        }

        if (amount.Value <= 0 || amount.Value > holding.FreeFineWeight.Value)
        {
            return Result.Failure(new Error("InsufficientFreeGold", "Custody holding does not contain enough free gold."));
        }

        if (facility.Status == Enums.FacilityStatus.LiquidationRequired)
        {
            return Result.Failure(new Error("FacilityLiquidationRequired", "Collateral cannot be pledged after liquidation is required."));
        }

        return Result.Success();
    }
}