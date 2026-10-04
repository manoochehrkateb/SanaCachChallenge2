namespace SanaCash.GoldCredit.Domain.Pricing;

public interface IPriceTickRepository
{
    Task<bool> TryAddAsync(PriceTick tick, CancellationToken cancellationToken = default);
    Task<PriceTick?> GetLatestAsync(CancellationToken cancellationToken = default);
}