using SanaCash.GoldCredit.Domain.Pricing.Enums;
using SanaCash.GoldCredit.Domain.Pricing.Errors;
using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Pricing;

public sealed class PriceTickValidator
{
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromSeconds(2);

    public Result<PriceTick> Validate(PriceTickPayload payload, DateTimeOffset receivedAtUtc)
    {
        if (payload.Price <= 0)
        {
            return Result<PriceTick>.Failure(PriceErrors.Rejected(TickRejectionReason.NonPositivePrice));
        }

        var instrument = Instrument.Create(payload.Instrument);
        if (instrument.IsFailure)
        {
            return Result<PriceTick>.Failure(PriceErrors.Rejected(TickRejectionReason.UnknownInstrument));
        }

        if (payload.TimestampUtc > receivedAtUtc.Add(FutureTolerance))
        {
            return Result<PriceTick>.Failure(PriceErrors.Rejected(TickRejectionReason.TimestampTooFarAhead));
        }

        var tickId = TickId.Create(instrument.Value, payload.Sequence);
        if (tickId.IsFailure)
        {
            return Result<PriceTick>.Failure(PriceErrors.Rejected(TickRejectionReason.InvalidSequence));
        }

        var price = ReferencePrice.Create(payload.Price);
        if (price.IsFailure)
        {
            return Result<PriceTick>.Failure(PriceErrors.Rejected(TickRejectionReason.NonPositivePrice));
        }

        return Result<PriceTick>.Success(PriceTick.Create(tickId.Value, price.Value, payload.TimestampUtc));
    }
}