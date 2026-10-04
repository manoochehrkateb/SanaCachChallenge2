using SanaCash.GoldCredit.Domain.Credit;
using SanaCash.GoldCredit.Domain.Credit.Enums;
using SanaCash.GoldCredit.Domain.Pricing;
using SanaCash.GoldCredit.Domain.Pricing.Enums;
using SanaCash.GoldCredit.Domain.Shared;

namespace SanaCash.GoldCredit.Domain.UnitTests;

public class PolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Margin_call_requires_three_consecutive_minutes_over_threshold()
    {
        var policy = new MarginPolicy();

        Assert.Equal(FacilityStatus.Healthy, policy.Evaluate(FacilityStatus.Healthy, [Snapshot(7_059, Now)]));
        Assert.Equal(FacilityStatus.Healthy, policy.Evaluate(FacilityStatus.Healthy,
            [Snapshot(7_059, Now), Snapshot(7_059, Now.AddMinutes(1))]));
        Assert.Equal(FacilityStatus.MarginCall, policy.Evaluate(FacilityStatus.Healthy,
            [Snapshot(7_059, Now), Snapshot(7_059, Now.AddMinutes(1)), Snapshot(7_059, Now.AddMinutes(2))]));
    }

    [Fact]
    public void Margin_call_does_not_treat_nonconsecutive_minutes_as_a_breach_streak()
    {
        var policy = new MarginPolicy();
        var snapshots = new[]
        {
            Snapshot(7_059, Now),
            Snapshot(7_059, Now.AddMinutes(1)),
            Snapshot(7_059, Now.AddMinutes(3))
        };

        Assert.Equal(FacilityStatus.Healthy, policy.Evaluate(FacilityStatus.Healthy, snapshots));
    }

    [Fact]
    public void Margin_policy_orders_snapshots_by_minute_before_evaluating_breach_streak()
    {
        var snapshots = new[]
        {
            Snapshot(7_059, Now.AddMinutes(2)),
            Snapshot(7_059, Now),
            Snapshot(7_059, Now.AddMinutes(1))
        };

        Assert.Equal(FacilityStatus.MarginCall,
            new MarginPolicy().Evaluate(FacilityStatus.Healthy, snapshots));
    }

    [Fact]
    public void Margin_policy_uses_latest_minute_for_liquidation_decision()
    {
        var snapshots = new[]
        {
            Snapshot(8_090, Now.AddMinutes(2)),
            Snapshot(7_059, Now),
            Snapshot(7_059, Now.AddMinutes(1))
        };

        Assert.Equal(FacilityStatus.LiquidationRequired,
            new MarginPolicy().Evaluate(FacilityStatus.Healthy, snapshots));
    }

    [Fact]
    public void Margin_call_is_held_between_cure_and_call_thresholds_and_cures_at_or_below_cure()
    {
        var policy = new MarginPolicy();

        Assert.Equal(FacilityStatus.MarginCall, policy.Evaluate(FacilityStatus.MarginCall, [Snapshot(6_924)]));
        Assert.Equal(FacilityStatus.Healthy, policy.Evaluate(FacilityStatus.MarginCall, [Snapshot(6_487)]));
        Assert.Equal(FacilityStatus.Healthy, policy.Evaluate(FacilityStatus.MarginCall, [Snapshot(6_500)]));
    }

    [Fact]
    public void Liquidation_is_strictly_greater_than_eight_thousand_basis_points_and_terminal()
    {
        var policy = new MarginPolicy();

        Assert.Equal(FacilityStatus.Healthy, policy.Evaluate(FacilityStatus.Healthy, [Snapshot(8_000)]));
        Assert.Equal(FacilityStatus.LiquidationRequired, policy.Evaluate(FacilityStatus.Healthy, [Snapshot(8_090)]));
        Assert.Equal(FacilityStatus.LiquidationRequired,
            policy.Evaluate(FacilityStatus.LiquidationRequired, [Snapshot(1_000)]));
    }

    [Fact]
    public void Price_freshness_is_inclusive_at_thirty_seconds_and_rejects_future_ticks()
    {
        var policy = new PriceFreshnessPolicy();

        Assert.True(policy.IsFresh(Now.AddSeconds(-30), Now));
        Assert.False(policy.IsFresh(Now.AddSeconds(-31), Now));
        Assert.False(policy.IsFresh(Now.AddSeconds(1), Now));
    }

    [Fact]
    public void Feed_becomes_stale_only_after_two_minutes_without_a_real_tick()
    {
        var policy = new FeedStalenessPolicy();

        Assert.Equal(FeedState.Live, policy.Evaluate(Now.AddMinutes(-2), Now));
        Assert.Equal(FeedState.Stale, policy.Evaluate(Now.AddMinutes(-2).AddTicks(-1), Now));
        Assert.Equal(FeedState.Stale, policy.Evaluate(null, Now));
    }

    [Theory]
    [InlineData("XAU-750", 100, 0, null)]
    [InlineData("XAG-750", 100, 0, TickRejectionReason.UnknownInstrument)]
    [InlineData("XAU-750", 0, 0, TickRejectionReason.NonPositivePrice)]
    public void Tick_validator_accepts_supported_prices_and_returns_rejection_reason(
        string instrument, long price, int futureSeconds, TickRejectionReason? expectedReason)
    {
        var validator = new PriceTickValidator();
        var payload = new PriceTickPayload(instrument, 1, price, Now.AddSeconds(futureSeconds));

        var result = validator.Validate(payload, Now);

        Assert.Equal(expectedReason is null, result.IsSuccess);
        if (expectedReason is not null)
        {
            Assert.Equal($"PriceTick{expectedReason}", result.Error.Code);
        }
    }

    [Fact]
    public void Tick_validator_rejects_timestamps_more_than_two_seconds_ahead()
    {
        var result = new PriceTickValidator().Validate(
            new PriceTickPayload("XAU-750", 1, 100, Now.AddSeconds(3)), Now);

        Assert.True(result.IsFailure);
        Assert.Equal($"PriceTick{TickRejectionReason.TimestampTooFarAhead}", result.Error.Code);
    }

    [Fact]
    public void Tick_validator_rejects_negative_sequence_with_a_persistable_reason()
    {
        var result = new PriceTickValidator().Validate(
            new PriceTickPayload("XAU-750", -1, 100, Now), Now);

        Assert.True(result.IsFailure);
        Assert.Equal($"PriceTick{TickRejectionReason.InvalidSequence}", result.Error.Code);
    }

    private static MinuteSnapshot Snapshot(int ltvBps, DateTimeOffset? timestamp = null)
    {
        var minute = MinuteBucket.From(timestamp ?? Now);
        var close = ReferencePrice.Create(120_000_000).Value;
        var collateral = FineWeightMg.Create(100_000).Value;
        var value = CollateralValue.Calculate(collateral, close).Value;
        var debt = Irr.Create((long)((Int128)ltvBps * value.Irr / 10_000)).Value;
        var ltv = Ltv.Calculate(debt, value);
        return new MinuteSnapshot(minute, close, debt, collateral, value, ltv);
    }
}