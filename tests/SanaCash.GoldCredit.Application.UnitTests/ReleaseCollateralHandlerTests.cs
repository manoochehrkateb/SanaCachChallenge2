using SanaCash.GoldCredit.Application.Abstractions.Authentication;
using SanaCash.GoldCredit.Application.Abstractions.Common;
using SanaCash.GoldCredit.Application.Abstractions.Data;
using SanaCash.GoldCredit.Application.Abstractions.Pricing;
using SanaCash.GoldCredit.Application.Credit.ReleaseCollateral;
using SanaCash.GoldCredit.Domain.Credit;
using SanaCash.GoldCredit.Domain.Custody;
using SanaCash.GoldCredit.Domain.Pricing;
using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Application.UnitTests;

public class ReleaseCollateralHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Stale_reference_price_rejects_release_without_mutating_facility_or_holding()
    {
        var clientId = ClientId.Create(Guid.NewGuid()).Value;
        var facility = CreditFacility.Create(FacilityId.Create(Guid.NewGuid()).Value, clientId);
        Assert.True(facility.Pledge(FineWeightMg.Create(100_000).Value, Now).IsSuccess);
        facility.PullDomainEvents();
        var holding = CustodyHolding.Create(
            CustodyHoldingId.Create(clientId, Instrument.Xau750), FineWeightMg.Create(0).Value);
        var facilities = new FacilityRepository(facility);
        var holdings = new HoldingRepository(holding);
        var handler = new ReleaseCollateralHandler(
            new CurrentClient(clientId),
            facilities,
            holdings,
            new PriceProvider(new ReferencePriceQuote(
                ReferencePrice.Create(120_000_000).Value, Now.AddSeconds(-31))),
            new PriceFreshnessPolicy(),
            new Clock(Now),
            new InlineUnitOfWork(),
            new IdempotencyStore());

        var result = await handler.HandleAsync(
            new ReleaseCollateralCommand(facility.Id.Value, 10_000, "release-stale"));

        Assert.True(result.IsFailure);
        Assert.Equal("NoFreshPrice", result.Error.Code);
        Assert.Equal(100_000, facility.Collateral.Value);
        Assert.Equal(0, holding.FreeFineWeight.Value);
        Assert.Equal(0, facilities.UpdateCount);
        Assert.Equal(0, holdings.UpdateCount);
    }

    private sealed class CurrentClient(ClientId clientId) : ICurrentClient
    {
        public ClientId ClientId { get; } = clientId;
        public bool IsRiskOfficer => false;
    }

    private sealed class Clock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class PriceProvider(ReferencePriceQuote quote) : IReferencePriceProvider
    {
        public Task<ReferencePriceQuote?> GetLatestAsync(Instrument instrument, CancellationToken cancellationToken = default) =>
            Task.FromResult<ReferencePriceQuote?>(quote);
    }

    private sealed class FacilityRepository(CreditFacility facility) : ICreditFacilityRepository
    {
        public int UpdateCount { get; private set; }
        public Task<CreditFacility?> GetForUpdateAsync(FacilityId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<CreditFacility?>(facility.Id == id ? facility : null);
        public Task UpdateAsync(CreditFacility facility, CancellationToken cancellationToken = default)
        {
            UpdateCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class HoldingRepository(CustodyHolding holding) : ICustodyHoldingRepository
    {
        public int UpdateCount { get; private set; }
        public Task<CustodyHolding?> GetForUpdateAsync(ClientId clientId, CancellationToken cancellationToken = default) =>
            Task.FromResult<CustodyHolding?>(holding);
        public Task UpdateAsync(CustodyHolding holding, CancellationToken cancellationToken = default)
        {
            UpdateCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class InlineUnitOfWork : IUnitOfWork
    {
        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default) => operation(cancellationToken);
    }

    private sealed class IdempotencyStore : IIdempotencyStore
    {
        public Task<Result<TResponse>> ExecuteAsync<TResponse>(ClientId clientId, string operation, string key,
            string requestFingerprint, Func<CancellationToken, Task<Result<TResponse>>> execute,
            CancellationToken cancellationToken = default) => execute(cancellationToken);
    }
}
