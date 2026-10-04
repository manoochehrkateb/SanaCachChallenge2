using SanaCash.GoldCredit.Domain.Credit;
using SanaCash.GoldCredit.Domain.Shared;

namespace SanaCash.GoldCredit.Domain.UnitTests;

public class ValuationTests
{
    private static FineWeightMg Weight(long milligrams) => FineWeightMg.Create(milligrams).Value;
    private static ReferencePrice Price(long irrPerGram) => ReferencePrice.Create(irrPerGram).Value;
    private static Irr Debt(long irr) => Irr.Create(irr).Value;

    [Fact]
    public void Worked_example_calculates_collateral_value_and_client_ltv_boundary()
    {
        var value = CollateralValue.Calculate(Weight(100_000), Price(120_000_000)).Value;

        Assert.Equal(16_000_000_000, value.Irr);
        Assert.Equal((Int128)6_000, Ltv.Calculate(Debt(9_600_000_000), value).BasisPoints);
        Assert.Equal((Int128)6_001, Ltv.Calculate(Debt(9_600_000_001), value).BasisPoints);
    }

    [Theory]
    [InlineData(102_000_000, 13_600_000_000, 7_059)]
    [InlineData(104_000_000, 13_866_666_666, 6_924)]
    [InlineData(111_000_000, 14_800_000_000, 6_487)]
    [InlineData(90_000_000, 12_000_000_000, 8_000)]
    [InlineData(89_000_000, 11_866_666_666, 8_090)]
    public void Worked_price_examples_use_conservative_integer_rounding(long price, long expectedValue, int expectedLtv)
    {
        var value = CollateralValue.Calculate(Weight(100_000), Price(price)).Value;
        var ltv = Ltv.Calculate(Debt(9_600_000_000), value);

        Assert.Equal(expectedValue, value.Irr);
        Assert.Equal((Int128)expectedLtv, ltv.BasisPoints);
    }

    [Fact]
    public void Positive_debt_with_zero_value_has_infinite_ltv_but_zero_debt_is_zero()
    {
        var zeroValue = CollateralValue.Calculate(Weight(0), Price(120_000_000)).Value;

        Assert.True(Ltv.Calculate(Debt(1), zeroValue).IsInfinite);
        Assert.Equal(Ltv.Zero, Ltv.Calculate(Debt(0), zeroValue));
    }

    [Fact]
    public void Invalid_financial_values_cannot_be_created()
    {
        Assert.True(FineWeightMg.Create(-1).IsFailure);
        Assert.True(ReferencePrice.Create(0).IsFailure);
        Assert.True(Irr.Create(-1).IsFailure);
        Assert.True(FineWeightMg.Create(1.5m).IsFailure);
    }

    [Fact]
    public void Fine_weight_subtraction_cannot_make_a_negative_value()
    {
        var result = Weight(100).Subtract(Weight(101));

        Assert.True(result.IsFailure);
        Assert.Equal("InsufficientFineWeight", result.Error.Code);
    }
}