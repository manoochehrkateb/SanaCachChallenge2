namespace SanaCash.GoldCredit.Domain.Pricing;

public class PriceFreshnessPolicy
{
    public static readonly TimeSpan MaximumAge = TimeSpan.FromSeconds(30);

    public bool IsFresh(DateTimeOffset tickTimestampUtc, DateTimeOffset nowUtc) =>
        tickTimestampUtc <= nowUtc && nowUtc - tickTimestampUtc <= MaximumAge;
}
