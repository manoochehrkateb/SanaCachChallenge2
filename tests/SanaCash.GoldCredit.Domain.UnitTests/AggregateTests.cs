using SanaCash.GoldCredit.Domain.Credit;
using SanaCash.GoldCredit.Domain.Credit.Enums;
using SanaCash.GoldCredit.Domain.Custody;
using SanaCash.GoldCredit.Domain.Pricing;
using SanaCash.GoldCredit.Domain.Shared;

namespace SanaCash.GoldCredit.Domain.UnitTests;

public class AggregateTests
{
    [Fact]
    public void Facility_drawdown_at_limit_succeeds_and_next_irr_is_rejected()
    {
        var facility = CreateFacility();
        var now = DateTimeOffset.UtcNow;
        Assert.True(facility.Pledge(FineWeightMg.Create(100_000).Value, now).IsSuccess);
        var price = ReferencePrice.Create(120_000_000).Value;

        Assert.True(facility.Drawdown(Irr.Create(9_600_000_000).Value, price, now).IsSuccess);
        var rejected = facility.Drawdown(Irr.Create(1).Value, price, now);

        Assert.True(rejected.IsFailure);
        Assert.Equal("LtvLimitExceeded", rejected.Error.Code);
        Assert.Equal(FacilityStatus.Healthy, facility.Status);
        Assert.Equal(9_600_000_000, facility.Debt.Value);
    }

    [Fact]
    public void Margin_call_blocks_drawdown_and_release_but_allows_pledge_and_repayment()
    {
        var facility = CreateFacility();
        var now = DateTimeOffset.UtcNow;
        facility.Pledge(FineWeightMg.Create(100_000).Value, now);
        facility.Drawdown(Irr.Create(1_000).Value, ReferencePrice.Create(120_000_000).Value, now);
        var evidence = MarginEvidence.Create([Snapshot()]).Value;
        Assert.True(facility.ApplyMarginDecision(FacilityStatus.MarginCall, evidence, now).IsSuccess);

        Assert.True(facility.Drawdown(Irr.Create(1).Value, ReferencePrice.Create(120_000_000).Value, now).IsFailure);
        Assert.True(facility.Release(FineWeightMg.Create(1).Value, ReferencePrice.Create(120_000_000).Value, now).IsFailure);
        Assert.True(facility.Pledge(FineWeightMg.Create(1).Value, now).IsSuccess);
        Assert.True(facility.Repay(Irr.Create(1).Value, now).IsSuccess);
    }

    [Fact]
    public void Custody_holding_never_allows_free_gold_to_become_negative()
    {
        var clientId = ClientId.Create(Guid.NewGuid()).Value;
        var holdingId = CustodyHoldingId.Create(clientId, Instrument.Xau750);
        var holding = CustodyHolding.Create(holdingId, FineWeightMg.Create(150_000).Value);

        Assert.True(holding.Debit(FineWeightMg.Create(140_000).Value).IsSuccess);
        Assert.Equal(10_000, holding.FreeFineWeight.Value);

        var result = holding.Debit(FineWeightMg.Create(10_001).Value);

        Assert.True(result.IsFailure);
        Assert.Equal("InsufficientFreeGold", result.Error.Code);
        Assert.Equal(10_000, holding.FreeFineWeight.Value);
    }

    private static CreditFacility CreateFacility() => CreditFacility.Create(
        FacilityId.Create(Guid.NewGuid()).Value,
        ClientId.Create(Guid.NewGuid()).Value);

    private static MinuteSnapshot Snapshot()
    {
        var price = ReferencePrice.Create(120_000_000).Value;
        var collateral = FineWeightMg.Create(100_000).Value;
        var value = CollateralValue.Calculate(collateral, price).Value;
        var debt = Irr.Create(1_000).Value;
        return new MinuteSnapshot(MinuteBucket.From(DateTimeOffset.UtcNow), price, debt, collateral, value,
            Ltv.Calculate(debt, value));
    }
}