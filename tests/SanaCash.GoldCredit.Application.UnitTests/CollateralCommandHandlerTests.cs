using SanaCash.GoldCredit.Application.Abstractions.Authentication;
using SanaCash.GoldCredit.Application.Abstractions.Common;
using SanaCash.GoldCredit.Application.Abstractions.Data;
using SanaCash.GoldCredit.Application.Abstractions.Pricing;
using SanaCash.GoldCredit.Application.Credit.PledgeCollateral;
using SanaCash.GoldCredit.Application.Credit.ReleaseCollateral;
using SanaCash.GoldCredit.Application.Credit.RepayDebt;
using SanaCash.GoldCredit.Domain.Credit;
using SanaCash.GoldCredit.Domain.Credit.Enums;
using SanaCash.GoldCredit.Domain.Custody;
using SanaCash.GoldCredit.Domain.Pricing;
using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Application.UnitTests;

public class CollateralCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Pledge_moves_gold_and_locks_facility_before_holding()
    {
        var clientId = ClientId.Create(Guid.NewGuid()).Value;
        var facility = NewFacility(clientId);
        var holding = NewHolding(clientId, 150_000);
        var locks = new List<string>();
        var facilityRepository = new FakeFacilityRepository(facility, locks);
        var holdingRepository = new FakeHoldingRepository(holding, locks);
        var handler = new PledgeCollateralHandler(
            new FakeCurrentClient(clientId), facilityRepository, holdingRepository,
            new CollateralTransferService(), new FakeClock(), new InlineUnitOfWork(), new FakeIdempotencyStore());

        var result = await handler.HandleAsync(new PledgeCollateralCommand(facility.Id.Value, 20_000, "pledge-1"));

        Assert.True(result.IsSuccess);
        Assert.Equal(20_000, facility.Collateral.Value);
        Assert.Equal(130_000, holding.FreeFineWeight.Value);
        Assert.Equal(["facility-lock", "holding-lock"], locks);
    }

    [Fact]
    public async Task Release_returns_gold_to_custody_after_fresh_price_and_ltv_check()
    {
        var clientId = ClientId.Create(Guid.NewGuid()).Value;
        var facility = NewFacility(clientId);
        Assert.True(facility.Pledge(FineWeightMg.Create(100_000).Value, Now).IsSuccess);
        facility.PullDomainEvents();
        var holding = NewHolding(clientId, 0);
        var handler = new ReleaseCollateralHandler(
            new FakeCurrentClient(clientId), new FakeFacilityRepository(facility, []),
            new FakeHoldingRepository(holding, []), new FakeReferencePriceProvider(Now),
            new PriceFreshnessPolicy(), new FakeClock(), new InlineUnitOfWork(), new FakeIdempotencyStore());

        var result = await handler.HandleAsync(new ReleaseCollateralCommand(facility.Id.Value, 20_000, "release-1"));

        Assert.True(result.IsSuccess);
        Assert.Equal(80_000, facility.Collateral.Value);
        Assert.Equal(20_000, holding.FreeFineWeight.Value);
    }

    [Fact]
    public async Task Repayment_reduces_debt_even_when_facility_is_in_margin_call()
    {
        var clientId = ClientId.Create(Guid.NewGuid()).Value;
        var facility = NewFacility(clientId);
        Assert.True(facility.Pledge(FineWeightMg.Create(100_000).Value, Now).IsSuccess);
        Assert.True(facility.Drawdown(Irr.Create(1_000_000).Value,
            ReferencePrice.Create(120_000_000).Value, Now).IsSuccess);
        facility.PullDomainEvents();
        var evidence = MarginEvidence.Create([CreateSnapshot()]).Value;
        Assert.True(facility.ApplyMarginDecision(FacilityStatus.MarginCall, evidence, Now).IsSuccess);
        facility.PullDomainEvents();
        var handler = new RepayDebtHandler(
            new FakeCurrentClient(clientId), new FakeFacilityRepository(facility, []),
            new FakeClock(), new InlineUnitOfWork(), new FakeIdempotencyStore());

        var result = await handler.HandleAsync(new RepayDebtCommand(facility.Id.Value, 500_000, "repay-1"));

        Assert.True(result.IsSuccess);
        Assert.Equal(500_000, facility.Debt.Value);
        Assert.Equal(FacilityStatus.MarginCall, facility.Status);
    }

    private static CreditFacility NewFacility(ClientId clientId) =>
        CreditFacility.Create(FacilityId.Create(Guid.NewGuid()).Value, clientId);

    private static CustodyHolding NewHolding(ClientId clientId, long amount) =>
        CustodyHolding.Create(
            CustodyHoldingId.Create(clientId, Instrument.Xau750),
            FineWeightMg.Create(amount).Value);

    private static MinuteSnapshot CreateSnapshot()
    {
        var price = ReferencePrice.Create(120_000_000).Value;
        var collateral = FineWeightMg.Create(100_000).Value;
        var value = CollateralValue.Calculate(collateral, price).Value;
        var debt = Irr.Create(1_000_000).Value;
        return new MinuteSnapshot(MinuteBucket.From(Now), price, debt, collateral, value, Ltv.Calculate(debt, value));
    }

private class FakeCurrentClient(ClientId clientId) : ICurrentClient
    {
        public ClientId ClientId { get; } = clientId;
        public bool IsRiskOfficer => false;
    }

private class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

private class FakeFacilityRepository(CreditFacility facility, List<string> locks) : ICreditFacilityRepository
    {
        public Task<CreditFacility?> GetForUpdateAsync(FacilityId facilityId, CancellationToken cancellationToken = default)
        {
            locks.Add("facility-lock");
            return Task.FromResult<CreditFacility?>(facility.Id == facilityId ? facility : null);
        }

        public Task UpdateAsync(CreditFacility updatedFacility, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

private class FakeHoldingRepository(CustodyHolding holding, List<string> locks) : ICustodyHoldingRepository
    {
        public Task<CustodyHolding?> GetForUpdateAsync(ClientId clientId, CancellationToken cancellationToken = default)
        {
            locks.Add("holding-lock");
            return Task.FromResult<CustodyHolding?>(holding.Id.ClientId == clientId ? holding : null);
        }

        public Task UpdateAsync(CustodyHolding updatedHolding, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

private class FakeReferencePriceProvider(DateTimeOffset timestamp) : IReferencePriceProvider
    {
        public Task<ReferencePriceQuote?> GetLatestAsync(Instrument instrument, CancellationToken cancellationToken = default) =>
            Task.FromResult<ReferencePriceQuote?>(new(ReferencePrice.Create(120_000_000).Value, timestamp));
    }

private class InlineUnitOfWork : IUnitOfWork
    {
        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default) => operation(cancellationToken);
    }

private class FakeIdempotencyStore : IIdempotencyStore
    {
        public Task<Result<TResponse>> ExecuteAsync<TResponse>(
            ClientId clientId,
            string operation,
            string key,
            string requestFingerprint,
            Func<CancellationToken, Task<Result<TResponse>>> execute,
            CancellationToken cancellationToken = default) => execute(cancellationToken);
    }
}
