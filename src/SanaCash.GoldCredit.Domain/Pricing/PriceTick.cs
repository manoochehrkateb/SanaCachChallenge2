using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Pricing;

public sealed class PriceTick : Entity<TickId>
{
    private PriceTick(TickId id, ReferencePrice price, DateTimeOffset timestampUtc) : base(id)
    {
        Price = price;
        TimestampUtc = timestampUtc.ToUniversalTime();
    }

    public ReferencePrice Price { get; }
    public DateTimeOffset TimestampUtc { get; }

    public static PriceTick Create(TickId id, ReferencePrice price, DateTimeOffset timestamp) =>
        new(id, price, timestamp);
}