namespace SanaCash.GoldCredit.Domain.Pricing;

public interface IRejectedPriceTickRepository
{
    Task AddAsync(RejectedPriceTick tick, CancellationToken cancellationToken = default);
}