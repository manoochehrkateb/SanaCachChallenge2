using SanaCash.GoldCredit.Domain.Shared;

namespace SanaCash.GoldCredit.Application.Abstractions.Data;

public interface IMarketReadStore
{
    Task<IReadOnlyList<PriceCandleReadModel>> GetCandlesAsync(
        Instrument instrument,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);
}