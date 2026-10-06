using SanaCash.GoldCredit.Application.Abstractions.Common;
using SanaCash.GoldCredit.Application.Abstractions.Data;
using SanaCash.GoldCredit.Domain.Pricing;
using SanaCash.GoldCredit.Domain.Pricing.Enums;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Application.Pricing.IngestPriceTick;

public class IngestPriceTickHandler(
    PriceTickValidator validator,
    IPriceTickRepository ticks,
    IRejectedPriceTickRepository rejectedTicks,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public Task<PriceTickIngestionResult> HandleAsync(
        PriceTickPayload payload,
        CancellationToken cancellationToken = default) =>
        unitOfWork.ExecuteInTransactionAsync(
            transactionToken => IngestAsync(payload, transactionToken),
            cancellationToken);

    public Task<bool> RecordMalformedAsync(string rawPayload, CancellationToken cancellationToken = default) =>
        unitOfWork.ExecuteInTransactionAsync(async transactionToken =>
        {
            await rejectedTicks.AddAsync(
                RejectedPriceTick.RecordMalformed(rawPayload, clock.UtcNow),
                transactionToken);
            return true;
        }, cancellationToken);

    private async Task<PriceTickIngestionResult> IngestAsync(
        PriceTickPayload payload,
        CancellationToken cancellationToken)
    {
        var receivedAt = clock.UtcNow;
        var validation = validator.Validate(payload, receivedAt);
        if (validation.IsFailure)
        {
            var reasonText = validation.Error.Code.StartsWith("PriceTick", StringComparison.Ordinal)
                ? validation.Error.Code["PriceTick".Length..]
                : nameof(TickRejectionReason.InvalidSequence);
            var reason = Enum.TryParse<TickRejectionReason>(reasonText, ignoreCase: false, out var parsedReason)
                ? parsedReason
                : TickRejectionReason.InvalidSequence;
            await rejectedTicks.AddAsync(
                RejectedPriceTick.Record(payload.Instrument, payload.Sequence, payload.Price, payload.TimestampUtc, reason),
                cancellationToken);
            return new PriceTickIngestionResult(false, false, reason.ToString());
        }

        var inserted = await ticks.TryAddAsync(validation.Value, cancellationToken);
        return new PriceTickIngestionResult(true, !inserted, null);
    }
}
