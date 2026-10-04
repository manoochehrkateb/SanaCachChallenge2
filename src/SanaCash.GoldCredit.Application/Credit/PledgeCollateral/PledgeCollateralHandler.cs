using SanaCash.GoldCredit.Application.Abstractions.Authentication;
using SanaCash.GoldCredit.Application.Abstractions.Common;
using SanaCash.GoldCredit.Application.Abstractions.Data;
using SanaCash.GoldCredit.Application.Behaviors.Idempotency;
using SanaCash.GoldCredit.Domain.Credit;
using SanaCash.GoldCredit.Domain.Custody;
using SanaCash.GoldCredit.Domain.Credit.Errors;
using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Application.Credit.PledgeCollateral;

public sealed class PledgeCollateralHandler(
    ICurrentClient currentClient,
    ICreditFacilityRepository facilities,
    ICustodyHoldingRepository holdings,
    CollateralTransferService transferService,
    IClock clock,
    IUnitOfWork unitOfWork,
    IIdempotencyStore idempotencyStore)
{
    private const string Operation = "PledgeCollateral";

    public Task<Result<long>> HandleAsync(PledgeCollateralCommand command, CancellationToken cancellationToken = default)
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

    private async Task<Result<long>> ExecuteCoreAsync(PledgeCollateralCommand command, CancellationToken cancellationToken)
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

        var validation = transferService.ValidatePledge(holding, facility, amount.Value);
        if (validation.IsFailure)
        {
            return Result<long>.Failure(validation.Error);
        }

        var pledgeResult = facility.Pledge(amount.Value, clock.UtcNow);
        if (pledgeResult.IsFailure)
        {
            return Result<long>.Failure(pledgeResult.Error);
        }

        var debitResult = holding.Debit(amount.Value);
        if (debitResult.IsFailure)
        {
            return Result<long>.Failure(debitResult.Error);
        }

        await facilities.UpdateAsync(facility, cancellationToken);
        await holdings.UpdateAsync(holding, cancellationToken);
        return Result<long>.Success(facility.Version);
    }
}