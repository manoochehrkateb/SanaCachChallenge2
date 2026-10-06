using SanaCash.GoldCredit.Application.Abstractions.Data;
using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Application.Pricing.GetCandles;

public class GetCandlesHandler(IMarketReadStore market)
{
    public Task<Result<IReadOnlyList<PriceCandleReadModel>>> HandleAsync(
        string instrumentCode,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        var instrument = Instrument.Create(instrumentCode);
        if (instrument.IsFailure)
        {
            return Task.FromResult(Result<IReadOnlyList<PriceCandleReadModel>>.Failure(instrument.Error));
        }

        if (fromUtc >= toUtc)
        {
            return Task.FromResult(Result<IReadOnlyList<PriceCandleReadModel>>.Failure(
                new Error("InvalidCandleRange", "The candle range start must be before its end.")));
        }

        return GetAsync(instrument.Value, fromUtc.ToUniversalTime(), toUtc.ToUniversalTime(), cancellationToken);
    }

    private async Task<Result<IReadOnlyList<PriceCandleReadModel>>> GetAsync(
        Instrument instrument,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken) =>
        Result<IReadOnlyList<PriceCandleReadModel>>.Success(
            await market.GetCandlesAsync(instrument, fromUtc, toUtc, cancellationToken));
}
