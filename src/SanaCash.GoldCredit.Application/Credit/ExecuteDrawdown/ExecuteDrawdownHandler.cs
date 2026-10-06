using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SanaCash.GoldCredit.Application.Abstractions.Authentication;
using SanaCash.GoldCredit.Application.Abstractions.Common;
using SanaCash.GoldCredit.Application.Abstractions.Data;
using SanaCash.GoldCredit.Application.Abstractions.Pricing;
using SanaCash.GoldCredit.Application.IntegrationEvents;
using SanaCash.GoldCredit.Domain.Credit;
using SanaCash.GoldCredit.Domain.Credit.Events;
using SanaCash.GoldCredit.Domain.Credit.Errors;
using SanaCash.GoldCredit.Domain.Pricing;
using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Application.Credit.ExecuteDrawdown;

public class ExecuteDrawdownHandler(
    ICurrentClient currentClient,
    ICreditFacilityRepository facilities,
    IReferencePriceProvider referencePrices,
    PriceFreshnessPolicy priceFreshness,
    IClock clock,
    IUnitOfWork unitOfWork,
    IIdempotencyStore idempotencyStore,
    IOutboxWriter outbox)
{
    private const string Operation = "ExecuteDrawdown";

    public Task<Result<DrawdownReceipt>> HandleAsync(
        ExecuteDrawdownCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            return Task.FromResult(Result<DrawdownReceipt>.Failure(
                new Error("IdempotencyKeyMissing", "An Idempotency-Key is required.")));
        }

        var fingerprint = CreateFingerprint(command);
        return unitOfWork.ExecuteInTransactionAsync(
            transactionToken => idempotencyStore.ExecuteAsync(
                currentClient.ClientId,
                Operation,
                command.IdempotencyKey,
                fingerprint,
                operationToken => ExecuteCoreAsync(command, operationToken),
                transactionToken),
            cancellationToken);
    }

    private async Task<Result<DrawdownReceipt>> ExecuteCoreAsync(
        ExecuteDrawdownCommand command,
        CancellationToken cancellationToken)
    {
        var facilityIdResult = FacilityId.Create(command.FacilityId);
        if (facilityIdResult.IsFailure)
        {
            return Result<DrawdownReceipt>.Failure(FacilityErrors.NotFound);
        }

        var amountResult = Irr.Create(command.AmountIrr);
        if (amountResult.IsFailure)
        {
            return Result<DrawdownReceipt>.Failure(amountResult.Error);
        }

        var facility = await facilities.GetForUpdateAsync(facilityIdResult.Value, cancellationToken);
        if (facility is null || facility.ClientId != currentClient.ClientId)
        {
            return Result<DrawdownReceipt>.Failure(FacilityErrors.NotFound);
        }

        var quote = await referencePrices.GetLatestAsync(facility.Instrument, cancellationToken);
        var now = clock.UtcNow;
        if (quote is null || !priceFreshness.IsFresh(quote.TimestampUtc, now))
        {
            return Result<DrawdownReceipt>.Failure(
                new Error("NoFreshPrice", "No fresh reference price is available."));
        }

        var drawdownResult = facility.Drawdown(amountResult.Value, quote.Price, now);
        if (drawdownResult.IsFailure)
        {
            return Result<DrawdownReceipt>.Failure(drawdownResult.Error);
        }

        var domainEvent = facility.DomainEvents.OfType<DrawdownExecuted>().Last();
        var integrationEvent = new DrawdownExecutedV1(
            domainEvent.EventId,
            facility.Id.Value,
            domainEvent.Amount.Value,
            domainEvent.OccurredAtUtc);

        await facilities.UpdateAsync(facility, cancellationToken);
        await outbox.AddAsync(
            integrationEvent.EventId,
            facility.Id.Value.ToString("D"),
            integrationEvent,
            cancellationToken);

        return Result<DrawdownReceipt>.Success(
            new DrawdownReceipt(facility.Id.Value, facility.Debt.Value, facility.Version));
    }

    private static string CreateFingerprint(ExecuteDrawdownCommand command)
    {
        var request = string.Create(
            CultureInfo.InvariantCulture,
            $"{Operation}|{command.FacilityId:D}|{command.AmountIrr}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request)));
    }
}
