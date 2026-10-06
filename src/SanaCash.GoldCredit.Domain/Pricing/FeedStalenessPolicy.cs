using SanaCash.GoldCredit.Domain.Pricing.Enums;

namespace SanaCash.GoldCredit.Domain.Pricing;

public class FeedStalenessPolicy
{
    public static readonly TimeSpan MaximumRealTickAge = TimeSpan.FromMinutes(2);

    public FeedState Evaluate(DateTimeOffset? latestRealTickUtc, DateTimeOffset nowUtc) =>
        latestRealTickUtc is not null
        && nowUtc - latestRealTickUtc.Value <= MaximumRealTickAge
            ? FeedState.Live
            : FeedState.Stale;
}
