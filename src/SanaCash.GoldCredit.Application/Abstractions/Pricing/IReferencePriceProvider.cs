using SanaCash.GoldCredit.Domain.Shared;

namespace SanaCash.GoldCredit.Application.Abstractions.Pricing;

public interface IReferencePriceProvider
{
    Task<ReferencePriceQuote?> GetLatestAsync(
        Instrument instrument,
        CancellationToken cancellationToken = default);
}