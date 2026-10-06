using SanaCash.GoldCredit.Application.Abstractions.Authentication;
using SanaCash.GoldCredit.Application.Abstractions.Common;
using SanaCash.GoldCredit.Application.Abstractions.Data;
using SanaCash.GoldCredit.Application.Abstractions.Pricing;
using SanaCash.GoldCredit.Application.Credit.ExecuteDrawdown;
using SanaCash.GoldCredit.Application.IntegrationEvents;
using SanaCash.GoldCredit.Domain.Credit;
using SanaCash.GoldCredit.Domain.Pricing;
using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Application.UnitTests;

public class ExecuteDrawdownHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Successful_drawdown_updates_facility_and_writes_outbox_event()
    {
        var clientId = ClientId.Create(Guid.NewGuid()).Value;
        var facility = CreateFacility(clientId);
        var repository = new FakeFacilityRepository(facility);
        var outbox = new FakeOutboxWriter();
        var idempotency = new FakeIdempotencyStore();
        var handler = CreateHandler(clientId, repository, outbox, idempotency,
            new ReferencePriceQuote(ReferencePrice.Create(120_000_000).Value, Now));

        var result = await handler.HandleAsync(new ExecuteDrawdownCommand(
            facility.Id.Value, 9_600_000_000, "request-1"));

        Assert.True(result.IsSuccess);
        Assert.Equal(9_600_000_000, facility.Debt.Value);
        Assert.IsType<DrawdownExecutedV1>(outbox.LastEvent);
        Assert.Equal("ExecuteDrawdown", idempotency.Operation);
        Assert.Equal("request-1", idempotency.Key);
        Assert.Equal(1, repository.UpdateCount);
    }

    [Fact]
    public async Task Stale_reference_price_rejects_drawdown_without_mutation_or_outbox()
    {
        var clientId = ClientId.Create(Guid.NewGuid()).Value;
        var facility = CreateFacility(clientId);
        var repository = new FakeFacilityRepository(facility);
        var outbox = new FakeOutboxWriter();
        var handler = CreateHandler(clientId, repository, outbox, new FakeIdempotencyStore(),
            new ReferencePriceQuote(ReferencePrice.Create(120_000_000).Value, Now.AddSeconds(-31)));

        var result = await handler.HandleAsync(new ExecuteDrawdownCommand(
            facility.Id.Value, 1_000, "request-2"));

        Assert.True(result.IsFailure);
        Assert.Equal("NoFreshPrice", result.Error.Code);
        Assert.Equal(0, facility.Debt.Value);
        Assert.Null(outbox.LastEvent);
        Assert.Equal(0, repository.UpdateCount);
    }

    [Fact]
    public async Task Another_clients_facility_is_reported_as_not_found()
    {
        var owner = ClientId.Create(Guid.NewGuid()).Value;
        var caller = ClientId.Create(Guid.NewGuid()).Value;
        var facility = CreateFacility(owner);
        var repository = new FakeFacilityRepository(facility);
        var outbox = new FakeOutboxWriter();
        var handler = CreateHandler(caller, repository, outbox, new FakeIdempotencyStore(),
            new ReferencePriceQuote(ReferencePrice.Create(120_000_000).Value, Now));

        var result = await handler.HandleAsync(new ExecuteDrawdownCommand(
            facility.Id.Value, 1_000, "request-3"));

        Assert.True(result.IsFailure);
        Assert.Equal("FacilityNotFound", result.Error.Code);
        Assert.Equal(0, facility.Debt.Value);
        Assert.Null(outbox.LastEvent);
    }

    private static CreditFacility CreateFacility(ClientId clientId)
    {
        var facilityId = FacilityId.Create(Guid.NewGuid()).Value;
        var facility = CreditFacility.Create(facilityId, clientId);
        var collateral = FineWeightMg.Create(100_000).Value;
        Assert.True(facility.Pledge(collateral, Now).IsSuccess);
        facility.PullDomainEvents();
        return facility;
    }

    private static ExecuteDrawdownHandler CreateHandler(
        ClientId clientId,
        FakeFacilityRepository repository,
        FakeOutboxWriter outbox,
        FakeIdempotencyStore idempotency,
        ReferencePriceQuote? quote) => new(
            new FakeCurrentClient(clientId),
            repository,
            new FakeReferencePriceProvider(quote),
            new PriceFreshnessPolicy(),
            new FakeClock(Now),
            new InlineUnitOfWork(),
            idempotency,
            outbox);

private class FakeCurrentClient(ClientId clientId) : ICurrentClient
    {
        public ClientId ClientId { get; } = clientId;
        public bool IsRiskOfficer => false;
    }

private class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

private class FakeFacilityRepository(CreditFacility facility) : ICreditFacilityRepository
    {
        public int UpdateCount { get; private set; }

        public Task<CreditFacility?> GetForUpdateAsync(FacilityId facilityId, CancellationToken cancellationToken = default) =>
            Task.FromResult<CreditFacility?>(facility.Id == facilityId ? facility : null);

        public Task UpdateAsync(CreditFacility updatedFacility, CancellationToken cancellationToken = default)
        {
            UpdateCount++;
            return Task.CompletedTask;
        }
    }

private class FakeReferencePriceProvider(ReferencePriceQuote? quote) : IReferencePriceProvider
    {
        public Task<ReferencePriceQuote?> GetLatestAsync(Instrument instrument, CancellationToken cancellationToken = default) =>
            Task.FromResult(quote);
    }

private class InlineUnitOfWork : IUnitOfWork
    {
        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default) => operation(cancellationToken);
    }

private class FakeIdempotencyStore : IIdempotencyStore
    {
        public string? Operation { get; private set; }
        public string? Key { get; private set; }

        public Task<Result<TResponse>> ExecuteAsync<TResponse>(
            ClientId clientId,
            string operation,
            string key,
            string requestFingerprint,
            Func<CancellationToken, Task<Result<TResponse>>> execute,
            CancellationToken cancellationToken = default)
        {
            Operation = operation;
            Key = key;
            return execute(cancellationToken);
        }
    }

private class FakeOutboxWriter : IOutboxWriter
    {
        public object? LastEvent { get; private set; }

        public Task AddAsync<TIntegrationEvent>(
            Guid eventId,
            string partitionKey,
            TIntegrationEvent integrationEvent,
            CancellationToken cancellationToken = default)
            where TIntegrationEvent : notnull
        {
            LastEvent = integrationEvent;
            return Task.CompletedTask;
        }
    }
}
