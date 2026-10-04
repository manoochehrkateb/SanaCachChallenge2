using SanaCash.GoldCredit.Application.Abstractions.Authentication;
using SanaCash.GoldCredit.Application.Abstractions.Common;
using SanaCash.GoldCredit.Application.Abstractions.Data;
using SanaCash.GoldCredit.Application.Abstractions.Pricing;
using SanaCash.GoldCredit.Application.Behaviors.Idempotency;
using SanaCash.GoldCredit.Domain.Credit;
using SanaCash.GoldCredit.Domain.Credit.Errors;
using SanaCash.GoldCredit.Domain.Custody;
using SanaCash.GoldCredit.Domain.Pricing;
using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Application.Credit.ReleaseCollateral;

public sealed class ReleaseCollateralHandler(
    ICurrentClient currentClient,
    ICreditFacilityRepository facilities,
    ICustodyHoldingRepository holdings,
    IReferencePriceProvider referencePrices,
    PriceFreshnessPolicy priceFreshness,
    IClock clock,
    IUnitOfWork unitOfWork,
    IIdempotencyStore idempotencyStore)
{
    private const string Operation = "ReleaseCollateral";

    public Task<Result<long>> HandleAsync(ReleaseCollateralCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            return Task.FromResult(Result<long>.Failure(new Error("IdempotencyKeyMissing", "An Idempotency-Key is required.")));
        }

        var fingerprint = RequestFingerprint.Create(Operation, new { command.FacilityId, command.FineWeightMg });
        return unitOfWork.ExecuteInTransactionAsync(
            transactionToken => idempotencyStore.ExecuteAsync(
                currentClient.ClientId, Operation, command.IdempotencyKey, fingerprint,
                operationToken => ExecuteCoreAsync(command, operationToken), transactionToken),
            cancellationToken);
    }

    private async Task<Result<long>> ExecuteCoreAsync(ReleaseCollateralCommand command, CancellationToken cancellationToken)
    {
        var facilityId = FacilityId.Create(command.FacilityId);
        var amount = FineWeightMg.Create(command.FineWeightMg);
        if (facilityId.IsFailure)
        {
            return Result<long>.Failure(FacilityErrors.NotFound);
        }

        if (amount.IsFailure)
        {
            return Result<long>.Failure(amount.Error);
        }

        var facility = await facilities.GetForUpdateAsync(facilityId.Value, cancellationToken);
        if (facility is null || facility.ClientId != currentClient.ClientId)
        {
            return Result<long>.Failure(FacilityErrors.NotFound);
        }

        var holding = await holdings.GetForUpdateAsync(currentClient.ClientId, cancellationToken);
        if (holding is null)
        {
            return Result<long>.Failure(new Error("HoldingNotFound", "Custody holding was not found."));
        }

        var quote = await referencePrices.GetLatestAsync(facility.Instrument, cancellationToken);
        var now = clock.UtcNow;
        if (quote is null || !priceFreshness.IsFresh(quote.TimestampUtc, now))
        {
            return Result<long>.Failure(new Error("NoFreshPrice", "No fresh reference price is available."));
        }

        var releaseResult = facility.Release(amount.Value, quote.Price, now);
        if (releaseResult.IsFailure)
        {
            return Result<long>.Failure(releaseResult.Error);
        }

        var creditResult = holding.Credit(amount.Value);
        if (creditResult.IsFailure)
        {
            return Result<long>.Failure(creditResult.Error);
        }

        await facilities.UpdateAsync(facility, cancellationToken);
        await holdings.UpdateAsync(holding, cancellationToken);
        return Result<long>.Success(facility.Version);
    }
}