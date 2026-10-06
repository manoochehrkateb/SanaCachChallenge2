using SanaCash.GoldCredit.Application.Abstractions.Authentication;
using SanaCash.GoldCredit.Application.Abstractions.Data;
using SanaCash.GoldCredit.Application.Abstractions.Pricing;
using SanaCash.GoldCredit.Domain.Credit;
using SanaCash.GoldCredit.Domain.Shared;

namespace SanaCash.GoldCredit.Application.Credit.GetFacility;

public sealed record FacilityDetails(
    Guid FacilityId,
    long CollateralFineMg,
    long DebtIrr,
    string Status,
    long? LtvBps,
    bool IsLtvInfinite,
    DateTimeOffset? PriceTimestampUtc);

public class GetFacilityHandler(
    ICurrentClient currentClient,
    IFacilityReadStore facilities,
    IReferencePriceProvider referencePrices)
{
    public async Task<FacilityDetails?> HandleAsync(Guid facilityId, CancellationToken cancellationToken = default)
    {
        var facility = await facilities.GetFacilityAsync(facilityId, cancellationToken);
        if (facility is null || (!currentClient.IsRiskOfficer && facility.ClientId != currentClient.ClientId.Value))
        {
            return null;
        }

        var quote = await referencePrices.GetLatestAsync(Instrument.Xau750, cancellationToken);
        if (quote is null)
        {
            return new FacilityDetails(facility.FacilityId, facility.CollateralFineMg, facility.DebtIrr,
                facility.Status, null, false, null);
        }

        var debt = Irr.Create(facility.DebtIrr).Value;
        var collateral = FineWeightMg.Create(facility.CollateralFineMg).Value;
        var value = CollateralValue.Calculate(collateral, quote.Price).Value;
        var ltv = Ltv.Calculate(debt, value);
        return new FacilityDetails(facility.FacilityId, facility.CollateralFineMg, facility.DebtIrr,
            facility.Status, ltv.IsInfinite ? null : checked((long)ltv.BasisPoints), ltv.IsInfinite,
            quote.TimestampUtc);
    }
}
