using SanaCash.GoldCredit.Domain.Pricing.Enums;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Pricing.Errors;

public static class PriceErrors
{
    public static Error NoFreshPrice { get; } = new("NoFreshPrice", "No reference price is fresh enough for this action.");
    public static Error FeedStale { get; } = new("FeedStale", "Price feed is stale; margin evaluation is paused.");

    public static Error Rejected(TickRejectionReason reason) => new(
        $"PriceTick{reason}",
        $"Price tick rejected: {reason}.");
}