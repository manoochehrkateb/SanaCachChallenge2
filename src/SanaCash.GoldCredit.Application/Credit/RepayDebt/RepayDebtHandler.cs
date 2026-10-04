using SanaCash.GoldCredit.Application.Abstractions.Authentication;
using SanaCash.GoldCredit.Application.Abstractions.Common;
using SanaCash.GoldCredit.Application.Abstractions.Data;
using SanaCash.GoldCredit.Application.Behaviors.Idempotency;
using SanaCash.GoldCredit.Domain.Credit;
using SanaCash.GoldCredit.Domain.Credit.Errors;
using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Application.Credit.RepayDebt;

public sealed class RepayDebtHandler(
    ICurrentClient currentClient,
    ICreditFacilityRepository facilities,
    IClock clock,
    IUnitOfWork unitOfWork,
    IIdempotencyStore idempotencyStore)
{
    private const string Operation = "RepayDebt";

    public Task<Result<long>> HandleAsync(RepayDebtCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            return Task.FromResult(Result<long>.Failure(new Error("IdempotencyKeyMissing", "An Idempotency-Key is required.")));
        }

        var fingerprint = RequestFingerprint.Create(Operation, new { command.FacilityId, command.AmountIrr });
        return unitOfWork.ExecuteInTransactionAsync(
            transactionToken => idempotencyStore.ExecuteAsync(
                currentClient.ClientId, Operation, command.IdempotencyKey, fingerprint,
                operationToken => ExecuteCoreAsync(command, operationToken), transactionToken),
            cancellationToken);
    }

    private async Task<Result<long>> ExecuteCoreAsync(RepayDebtCommand command, CancellationToken cancellationToken)
    {
        var facilityId = FacilityId.Create(command.FacilityId);
        var amount = Irr.Create(command.AmountIrr);
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

        var repaymentResult = facility.Repay(amount.Value, clock.UtcNow);
        if (repaymentResult.IsFailure)
        {
            return Result<long>.Failure(repaymentResult.Error);
        }

        await facilities.UpdateAsync(facility, cancellationToken);
        return Result<long>.Success(facility.Version);
    }
}