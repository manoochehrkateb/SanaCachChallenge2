using Npgsql;
using Microsoft.EntityFrameworkCore;
using SanaCash.GoldCredit.Application.Abstractions.Authentication;
using SanaCash.GoldCredit.Application.Abstractions.Common;
using SanaCash.GoldCredit.Application.Abstractions.Pricing;
using SanaCash.GoldCredit.Application.Credit.ExecuteDrawdown;
using SanaCash.GoldCredit.Application.Credit.PledgeCollateral;
using SanaCash.GoldCredit.Application.Credit.ReleaseCollateral;
using SanaCash.GoldCredit.Application.Credit.RepayDebt;
using SanaCash.GoldCredit.Application.Pricing.IngestPriceTick;
using SanaCash.GoldCredit.Domain.Credit;
using SanaCash.GoldCredit.Domain.Custody;
using SanaCash.GoldCredit.Domain.Pricing;
using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;
using SanaCash.GoldCredit.Persistence.Common;
using SanaCash.GoldCredit.Persistence.Credit;
using SanaCash.GoldCredit.Persistence.Custody;
using SanaCash.GoldCredit.Persistence.Migrations;
using SanaCash.GoldCredit.Persistence.Pricing;

namespace SanaCash.GoldCredit.IntegrationTests;

public class PostgresCoreFlowTests
{
    [PostgresIntegrationFact]
    public async Task Tick_identity_is_deduplicated_and_latest_is_selected_by_timestamp()
    {
        await using var dataSource = CreateDataSource();
        await new MigrationRunner(dataSource).ApplyAsync();

        var sequence = Random.Shared.NextInt64(1, long.MaxValue - 10);
        var newestAt = new DateTimeOffset(2030, 1, 1, 0, 0, 10, TimeSpan.Zero);
        var duplicate = CreateTick(sequence, newestAt);
        var olderArrival = CreateTick(sequence + 1, newestAt.AddSeconds(-5));
        var newerArrival = CreateTick(sequence + 2, newestAt.AddSeconds(5));

        Assert.True(await InsertTickAsync(dataSource, duplicate));
        Assert.False(await InsertTickAsync(dataSource, duplicate));
        Assert.True(await InsertTickAsync(dataSource, newerArrival));
        Assert.True(await InsertTickAsync(dataSource, olderArrival));

        var latest = await new PostgresPriceTickRepository(new PostgresSession(), dataSource)
            .GetLatestAsync();

        Assert.NotNull(latest);
        Assert.Equal(newerArrival.Id.Sequence, latest.Id.Sequence);
        Assert.Equal(newestAt.AddSeconds(5), latest.TimestampUtc);

        await using var cleanup = await dataSource.OpenConnectionAsync();
        await using var deleteTicks = new NpgsqlCommand(
            "DELETE FROM pricing.price_ticks WHERE instrument = 'XAU-750' AND sequence = ANY(@sequences); " +
            "DELETE FROM pricing.price_tick_keys WHERE instrument = 'XAU-750' AND sequence = ANY(@sequences);",
            cleanup);
        deleteTicks.Parameters.AddWithValue("sequences", new[] { sequence, sequence + 1, sequence + 2 });
        await deleteTicks.ExecuteNonQueryAsync();
    }

    [PostgresIntegrationFact]
    public async Task Candle_gap_is_carried_only_while_the_real_feed_is_live()
    {
        await using var dataSource = CreateDataSource();
        await new MigrationRunner(dataSource).ApplyAsync();

        var now = DateTimeOffset.UtcNow;
        var sequence = Random.Shared.NextInt64(1, long.MaxValue - 10);
        var tick = CreateTick(sequence, now.AddSeconds(-30));
        await InsertTickAsync(dataSource, tick);

        try
        {
            var context = new GoldCreditDbContext(
                new DbContextOptionsBuilder<GoldCreditDbContext>()
                    .UseNpgsql(dataSource)
                    .Options);
            var store = new PostgresMarketReadStore(
                context,
                new PostgresSession(),
                new FixedClock(now),
                new FeedStalenessPolicy());

            var start = MinuteBucket.From(now.AddMinutes(-1)).StartUtc;
            var candles = await store.GetCandlesAsync(
                Instrument.Xau750, start, start.AddMinutes(3));

            Assert.Equal(3, candles.Count);
            Assert.Contains(candles, candle => candle.CarriedForward);
            Assert.Contains(candles, candle => candle.CloseIrr == 120_000_000);
        }
        finally
        {
            await using var cleanup = await dataSource.OpenConnectionAsync();
            await using var delete = new NpgsqlCommand(
                "DELETE FROM pricing.price_ticks WHERE instrument = 'XAU-750' AND sequence = @sequence; " +
                "DELETE FROM pricing.price_tick_keys WHERE instrument = 'XAU-750' AND sequence = @sequence;",
                cleanup);
            delete.Parameters.AddWithValue("sequence", sequence);
            await delete.ExecuteNonQueryAsync();
        }
    }

    [PostgresIntegrationFact]
    public async Task Concurrent_same_key_pledges_debit_custody_once()
    {
        await using var dataSource = CreateDataSource();
        await new MigrationRunner(dataSource).ApplyAsync();

        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        await using (var connection = await dataSource.OpenConnectionAsync())
        await using (var seed = new NpgsqlCommand(
            "INSERT INTO custody.holdings (client_id, instrument, free_fine_mg) " +
            "VALUES (@client_id, 'XAU-750', 20000); " +
            "INSERT INTO credit.facilities " +
            "(facility_id, client_id, instrument, status) " +
            "VALUES (@facility_id, @client_id, 'XAU-750', 'Healthy');",
            connection))
        {
            seed.Parameters.AddWithValue("client_id", clientId);
            seed.Parameters.AddWithValue("facility_id", facilityId);
            await seed.ExecuteNonQueryAsync();
        }

        try
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
            {
                var session = new PostgresSession();
                var handler = new PledgeCollateralHandler(
                    new TestCurrentClient(ClientId.Create(clientId).Value),
                    new PostgresCreditFacilityRepository(session),
                    new PostgresCustodyHoldingRepository(session),
                    new CollateralTransferService(),
                    new FixedClock(DateTimeOffset.UtcNow),
                    new PostgresUnitOfWork(dataSource, session),
                    new PostgresIdempotencyStore(session));
                return await handler.HandleAsync(
                    new PledgeCollateralCommand(facilityId, 20_000, "pledge-once"));
            })));

            Assert.All(results, result => Assert.True(result.IsSuccess));

            await using var verify = await dataSource.OpenConnectionAsync();
            await using (var facility = new NpgsqlCommand(
                "SELECT collateral_fine_mg, version FROM credit.facilities WHERE facility_id = @facility_id;",
                verify))
            {
                facility.Parameters.AddWithValue("facility_id", facilityId);
                await using var reader = await facility.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(20_000, reader.GetInt64(0));
                Assert.Equal(1, reader.GetInt64(1));
            }

            await using (var holding = new NpgsqlCommand(
                "SELECT free_fine_mg FROM custody.holdings WHERE client_id = @client_id AND instrument = 'XAU-750';",
                verify))
            {
                holding.Parameters.AddWithValue("client_id", clientId);
                Assert.Equal(0L, (long)(await holding.ExecuteScalarAsync())!);
            }
        }

        finally
        {
            await using var cleanup = await dataSource.OpenConnectionAsync();
            await using var delete = new NpgsqlCommand(
                "DELETE FROM platform.idempotency_records WHERE client_id = @client_id; " +
                "DELETE FROM credit.facilities WHERE facility_id = @facility_id; " +
                "DELETE FROM custody.holdings WHERE client_id = @client_id;",
                cleanup);
            delete.Parameters.AddWithValue("client_id", clientId);
            delete.Parameters.AddWithValue("facility_id", facilityId);
            await delete.ExecuteNonQueryAsync();
        }
    }

    [PostgresIntegrationFact]
    public async Task One_minute_candle_uses_timestamp_order_for_ohlc_and_count()
    {
        await using var dataSource = CreateDataSource();
        await new MigrationRunner(dataSource).ApplyAsync();

        var minute = MinuteBucket.From(DateTimeOffset.UtcNow.AddMinutes(-5)).StartUtc;
        var sequences = new[]
        {
            Random.Shared.NextInt64(1, long.MaxValue - 10),
            Random.Shared.NextInt64(1, long.MaxValue - 10),
            Random.Shared.NextInt64(1, long.MaxValue - 10)
        };
        var ticks = new[]
        {
            (sequences[0], minute.AddSeconds(55), 103_000_000L),
            (sequences[1], minute.AddSeconds(5), 102_000_000L),
            (sequences[2], minute.AddSeconds(30), 101_000_000L)
        };

        try
        {
            await using var connection = await dataSource.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            foreach (var tick in ticks)
            {
                await using (var key = new NpgsqlCommand(
                    "INSERT INTO pricing.price_tick_keys (instrument, sequence, payload_hash) " +
                    "VALUES ('XAU-750', @sequence, @hash);", connection, transaction))
                {
                    key.Parameters.AddWithValue("sequence", tick.Item1);
                    key.Parameters.AddWithValue("hash", BitConverter.GetBytes(tick.Item1));
                    await key.ExecuteNonQueryAsync();
                }

                await using var insert = new NpgsqlCommand(
                    "INSERT INTO pricing.price_ticks (ts, instrument, sequence, price_irr_per_gram) " +
                    "VALUES (@timestamp, 'XAU-750', @sequence, @price);", connection, transaction);
                insert.Parameters.AddWithValue("timestamp", tick.Item2);
                insert.Parameters.AddWithValue("sequence", tick.Item1);
                insert.Parameters.AddWithValue("price", tick.Item3);
                await insert.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();

            await using var query = new NpgsqlCommand(
                "SELECT open_irr_per_gram, high_irr_per_gram, low_irr_per_gram, " +
                "close_irr_per_gram, tick_count FROM pricing.price_candles_1m " +
                "WHERE instrument = 'XAU-750' AND bucket = time_bucket('1 minute', @minute);",
                connection);
            query.Parameters.AddWithValue("minute", minute);
            await using var reader = await query.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(102_000_000, reader.GetInt64(0));
            Assert.Equal(103_000_000, reader.GetInt64(1));
            Assert.Equal(101_000_000, reader.GetInt64(2));
            Assert.Equal(103_000_000, reader.GetInt64(3));
            Assert.Equal(3, reader.GetInt64(4));
        }
        finally
        {
            await using var cleanup = await dataSource.OpenConnectionAsync();
            await using var delete = new NpgsqlCommand(
                "DELETE FROM pricing.price_ticks WHERE instrument = 'XAU-750' AND sequence = ANY(@sequences); " +
                "DELETE FROM pricing.price_tick_keys WHERE instrument = 'XAU-750' AND sequence = ANY(@sequences);",
                cleanup);
            delete.Parameters.AddWithValue("sequences", sequences);
            await delete.ExecuteNonQueryAsync();
        }
    }

    [PostgresIntegrationFact]
    public async Task Concurrent_same_key_releases_move_collateral_once()
    {
        await using var dataSource = CreateDataSource();
        await new MigrationRunner(dataSource).ApplyAsync();

        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        await using (var connection = await dataSource.OpenConnectionAsync())
        await using (var seed = new NpgsqlCommand(
            "INSERT INTO custody.holdings (client_id, instrument, free_fine_mg) " +
            "VALUES (@client_id, 'XAU-750', 0); " +
            "INSERT INTO credit.facilities " +
            "(facility_id, client_id, instrument, collateral_fine_mg, debt_irr, status, version) " +
            "VALUES (@facility_id, @client_id, 'XAU-750', 100000, 0, 'Healthy', 0);",
            connection))
        {
            seed.Parameters.AddWithValue("client_id", clientId);
            seed.Parameters.AddWithValue("facility_id", facilityId);
            await seed.ExecuteNonQueryAsync();
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
            {
                var session = new PostgresSession();
                var handler = new ReleaseCollateralHandler(
                    new TestCurrentClient(ClientId.Create(clientId).Value),
                    new PostgresCreditFacilityRepository(session),
                    new PostgresCustodyHoldingRepository(session),
                    new FixedReferencePriceProvider(new ReferencePriceQuote(
                        ReferencePrice.Create(120_000_000).Value, now)),
                    new PriceFreshnessPolicy(),
                    new FixedClock(now),
                    new PostgresUnitOfWork(dataSource, session),
                    new PostgresIdempotencyStore(session));
                return await handler.HandleAsync(
                    new ReleaseCollateralCommand(facilityId, 10_000, "release-once"));
            })));

            Assert.All(results, result => Assert.True(result.IsSuccess));

            await using var verify = await dataSource.OpenConnectionAsync();
            await using (var facility = new NpgsqlCommand(
                "SELECT collateral_fine_mg, version FROM credit.facilities WHERE facility_id = @facility_id;",
                verify))
            {
                facility.Parameters.AddWithValue("facility_id", facilityId);
                await using var reader = await facility.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(90_000, reader.GetInt64(0));
                Assert.Equal(1, reader.GetInt64(1));
            }

            await using (var holding = new NpgsqlCommand(
                "SELECT free_fine_mg FROM custody.holdings WHERE client_id = @client_id AND instrument = 'XAU-750';",
                verify))
            {
                holding.Parameters.AddWithValue("client_id", clientId);
                Assert.Equal(10_000L, (long)(await holding.ExecuteScalarAsync())!);
            }
        }
        finally
        {
            await using var cleanup = await dataSource.OpenConnectionAsync();
            await using var delete = new NpgsqlCommand(
                "DELETE FROM platform.idempotency_records WHERE client_id = @client_id; " +
                "DELETE FROM credit.facilities WHERE facility_id = @facility_id; " +
                "DELETE FROM custody.holdings WHERE client_id = @client_id;",
                cleanup);
            delete.Parameters.AddWithValue("client_id", clientId);
            delete.Parameters.AddWithValue("facility_id", facilityId);
            await delete.ExecuteNonQueryAsync();
        }
    }

    [PostgresIntegrationFact]
    public async Task Concurrent_same_key_repayments_apply_debt_change_once()
    {
        await using var dataSource = CreateDataSource();
        await new MigrationRunner(dataSource).ApplyAsync();

        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        await using (var connection = await dataSource.OpenConnectionAsync())
        await using (var seed = new NpgsqlCommand(
            "INSERT INTO credit.facilities " +
            "(facility_id, client_id, instrument, collateral_fine_mg, debt_irr, status, version) " +
            "VALUES (@facility_id, @client_id, 'XAU-750', 100000, 1000000000, 'Healthy', 0);",
            connection))
        {
            seed.Parameters.AddWithValue("facility_id", facilityId);
            seed.Parameters.AddWithValue("client_id", clientId);
            await seed.ExecuteNonQueryAsync();
        }

        try
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
            {
                var session = new PostgresSession();
                var handler = new RepayDebtHandler(
                    new TestCurrentClient(ClientId.Create(clientId).Value),
                    new PostgresCreditFacilityRepository(session),
                    new FixedClock(DateTimeOffset.UtcNow),
                    new PostgresUnitOfWork(dataSource, session),
                    new PostgresIdempotencyStore(session));
                return await handler.HandleAsync(
                    new RepayDebtCommand(facilityId, 100_000_000, "repay-once"));
            })));

            Assert.All(results, result => Assert.True(result.IsSuccess));

            await using var verify = await dataSource.OpenConnectionAsync();
            await using var query = new NpgsqlCommand(
                "SELECT debt_irr, version FROM credit.facilities WHERE facility_id = @facility_id;",
                verify);
            query.Parameters.AddWithValue("facility_id", facilityId);
            await using var reader = await query.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(900_000_000, reader.GetInt64(0));
            Assert.Equal(1, reader.GetInt64(1));
        }
        finally
        {
            await using var cleanup = await dataSource.OpenConnectionAsync();
            await using var delete = new NpgsqlCommand(
                "DELETE FROM platform.idempotency_records WHERE client_id = @client_id; " +
                "DELETE FROM credit.facilities WHERE facility_id = @facility_id;",
                cleanup);
            delete.Parameters.AddWithValue("client_id", clientId);
            delete.Parameters.AddWithValue("facility_id", facilityId);
            await delete.ExecuteNonQueryAsync();
        }
    }

    [PostgresIntegrationFact]
    public async Task Malformed_tick_payload_is_durably_recorded_with_raw_body()
    {
        await using var dataSource = CreateDataSource();
        await new MigrationRunner(dataSource).ApplyAsync();

        var rawPayload = "{not-json";
        var session = new PostgresSession();
        var handler = new IngestPriceTickHandler(
            new PriceTickValidator(),
            new PostgresPriceTickRepository(session, dataSource),
            new PostgresRejectedPriceTickRepository(session),
            new FixedClock(DateTimeOffset.UtcNow),
            new PostgresUnitOfWork(dataSource, session));

        Assert.True(await handler.RecordMalformedAsync(rawPayload));

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var query = new NpgsqlCommand(
            "SELECT rejection_reason, payload->>'RawPayload' FROM pricing.rejected_price_ticks " +
            "WHERE rejection_reason = 'InvalidPayload' AND payload->>'RawPayload' = @raw_payload " +
            "ORDER BY received_at DESC LIMIT 1;",
            connection);
        query.Parameters.AddWithValue("raw_payload", rawPayload);
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("InvalidPayload", reader.GetString(0));
        Assert.Equal(rawPayload, reader.GetString(1));

        await reader.CloseAsync();
        await using var cleanup = new NpgsqlCommand(
            "DELETE FROM pricing.rejected_price_ticks WHERE rejection_reason = 'InvalidPayload' AND payload->>'RawPayload' = @raw_payload;",
            connection);
        cleanup.Parameters.AddWithValue("raw_payload", rawPayload);
        await cleanup.ExecuteNonQueryAsync();
    }

    [PostgresIntegrationFact]
    public async Task Twenty_concurrent_drawdowns_preserve_maximum_ltv_and_write_one_outbox_per_success()
    {
        await using var dataSource = CreateDataSource();
        await new MigrationRunner(dataSource).ApplyAsync();

        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        await using (var connection = await dataSource.OpenConnectionAsync())
        await using (var seed = new NpgsqlCommand(
            "INSERT INTO credit.facilities " +
            "(facility_id, client_id, instrument, collateral_fine_mg, debt_irr, status, version) " +
            "VALUES (@facility_id, @client_id, 'XAU-750', 100000, 0, 'Healthy', 0);",
            connection))
        {
            seed.Parameters.AddWithValue("facility_id", facilityId);
            seed.Parameters.AddWithValue("client_id", clientId);
            await seed.ExecuteNonQueryAsync();
        }

        try
        {
            var fixedNow = DateTimeOffset.UtcNow;
            var tasks = Enumerable.Range(0, 20).Select(index => Task.Run(async () =>
            {
                var session = new PostgresSession();
                var handler = new ExecuteDrawdownHandler(
                    new TestCurrentClient(ClientId.Create(clientId).Value),
                    new PostgresCreditFacilityRepository(session),
                    new FixedReferencePriceProvider(new ReferencePriceQuote(
                        ReferencePrice.Create(120_000_000).Value, fixedNow)),
                    new PriceFreshnessPolicy(),
                    new FixedClock(fixedNow),
                    new PostgresUnitOfWork(dataSource, session),
                    new PostgresIdempotencyStore(session),
                    new PostgresOutboxWriter(session));

                return await handler.HandleAsync(new ExecuteDrawdownCommand(
                    facilityId, 600_000_000, $"parallel-{index}"));
            })).ToArray();

            var results = await Task.WhenAll(tasks);
            Assert.Equal(16, results.Count(result => result.IsSuccess));
            Assert.Equal(4, results.Count(result => result.IsFailure && result.Error.Code == "LtvLimitExceeded"));
            Assert.DoesNotContain(results, result => result.IsFailure && result.Error.Code != "LtvLimitExceeded");

            await using var verify = await dataSource.OpenConnectionAsync();
            await using (var query = new NpgsqlCommand(
                "SELECT debt_irr, version FROM credit.facilities WHERE facility_id = @facility_id;",
                verify))
            {
                query.Parameters.AddWithValue("facility_id", facilityId);
                await using var reader = await query.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(9_600_000_000, reader.GetInt64(0));
                Assert.Equal(16, reader.GetInt64(1));
            }

            await using (var count = new NpgsqlCommand(
                "SELECT count(*) FROM platform.outbox_messages WHERE aggregate_id = @facility_id;",
                verify))
            {
                count.Parameters.AddWithValue("facility_id", facilityId.ToString("D"));
                Assert.Equal(16L, (long)(await count.ExecuteScalarAsync())!);
            }
        }
        finally
        {
            await using var cleanup = await dataSource.OpenConnectionAsync();
            await using var delete = new NpgsqlCommand(
                "DELETE FROM platform.outbox_messages WHERE aggregate_id = @aggregate_id; " +
                "DELETE FROM platform.idempotency_records WHERE client_id = @client_id; " +
                "DELETE FROM credit.margin_events WHERE facility_id = @facility_id; " +
                "DELETE FROM credit.margin_evaluations WHERE facility_id = @facility_id; " +
                "DELETE FROM credit.facilities WHERE facility_id = @facility_id;",
                cleanup);
            delete.Parameters.AddWithValue("aggregate_id", facilityId.ToString("D"));
            delete.Parameters.AddWithValue("client_id", clientId);
            delete.Parameters.AddWithValue("facility_id", facilityId);
            await delete.ExecuteNonQueryAsync();
        }
    }

    [PostgresIntegrationFact]
    public async Task Concurrent_same_key_requests_replay_one_result_and_reject_payload_reuse()
    {
        await using var dataSource = CreateDataSource();
        await new MigrationRunner(dataSource).ApplyAsync();

        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        await using (var connection = await dataSource.OpenConnectionAsync())
        await using (var seed = new NpgsqlCommand(
            "INSERT INTO credit.facilities " +
            "(facility_id, client_id, instrument, collateral_fine_mg, debt_irr, status, version) " +
            "VALUES (@facility_id, @client_id, 'XAU-750', 100000, 0, 'Healthy', 0);",
            connection))
        {
            seed.Parameters.AddWithValue("facility_id", facilityId);
            seed.Parameters.AddWithValue("client_id", clientId);
            await seed.ExecuteNonQueryAsync();
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var requests = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
            {
                var session = new PostgresSession();
                var handler = new ExecuteDrawdownHandler(
                    new TestCurrentClient(ClientId.Create(clientId).Value),
                    new PostgresCreditFacilityRepository(session),
                    new FixedReferencePriceProvider(new ReferencePriceQuote(
                        ReferencePrice.Create(120_000_000).Value, now)),
                    new PriceFreshnessPolicy(),
                    new FixedClock(now),
                    new PostgresUnitOfWork(dataSource, session),
                    new PostgresIdempotencyStore(session),
                    new PostgresOutboxWriter(session));
                return await handler.HandleAsync(new ExecuteDrawdownCommand(facilityId, 600_000_000, "same-key"));
            })).ToArray();

            var results = await Task.WhenAll(requests);
            Assert.All(results, result => Assert.True(result.IsSuccess));
            Assert.All(results, result => Assert.Equal(results[0].Value, result.Value));

            var mismatchSession = new PostgresSession();
            var mismatchHandler = new ExecuteDrawdownHandler(
                new TestCurrentClient(ClientId.Create(clientId).Value),
                new PostgresCreditFacilityRepository(mismatchSession),
                new FixedReferencePriceProvider(new ReferencePriceQuote(
                    ReferencePrice.Create(120_000_000).Value, now)),
                new PriceFreshnessPolicy(),
                new FixedClock(now),
                new PostgresUnitOfWork(dataSource, mismatchSession),
                new PostgresIdempotencyStore(mismatchSession),
                new PostgresOutboxWriter(mismatchSession));
            var mismatch = await mismatchHandler.HandleAsync(
                new ExecuteDrawdownCommand(facilityId, 700_000_000, "same-key"));
            Assert.True(mismatch.IsFailure);
            Assert.Equal("IdempotencyKeyReused", mismatch.Error.Code);

            await using var verify = await dataSource.OpenConnectionAsync();
            await using (var facility = new NpgsqlCommand(
                "SELECT debt_irr FROM credit.facilities WHERE facility_id = @facility_id;", verify))
            {
                facility.Parameters.AddWithValue("facility_id", facilityId);
                Assert.Equal(600_000_000L, (long)(await facility.ExecuteScalarAsync())!);
            }

            await using (var outbox = new NpgsqlCommand(
                "SELECT count(*) FROM platform.outbox_messages WHERE aggregate_id = @aggregate_id;", verify))
            {
                outbox.Parameters.AddWithValue("aggregate_id", facilityId.ToString("D"));
                Assert.Equal(1L, (long)(await outbox.ExecuteScalarAsync())!);
            }
        }
        finally
        {
            await using var cleanup = await dataSource.OpenConnectionAsync();
            await using var delete = new NpgsqlCommand(
                "DELETE FROM platform.outbox_messages WHERE aggregate_id = @aggregate_id; " +
                "DELETE FROM platform.idempotency_records WHERE client_id = @client_id; " +
                "DELETE FROM credit.facilities WHERE facility_id = @facility_id;",
                cleanup);
            delete.Parameters.AddWithValue("aggregate_id", facilityId.ToString("D"));
            delete.Parameters.AddWithValue("client_id", clientId);
            delete.Parameters.AddWithValue("facility_id", facilityId);
            await delete.ExecuteNonQueryAsync();
        }
    }

    [PostgresIntegrationFact]
    public async Task Concurrent_pledges_across_facilities_never_overdraw_shared_custody()
    {
        await using var dataSource = CreateDataSource();
        await new MigrationRunner(dataSource).ApplyAsync();

        var clientId = Guid.NewGuid();
        var facilityIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await using (var connection = await dataSource.OpenConnectionAsync())
        await using (var seed = new NpgsqlCommand(
            "INSERT INTO custody.holdings (client_id, instrument, free_fine_mg) VALUES (@client_id, 'XAU-750', 150000); " +
            "INSERT INTO credit.facilities (facility_id, client_id, instrument, status) " +
            "VALUES (@facility_a, @client_id, 'XAU-750', 'Healthy'), (@facility_b, @client_id, 'XAU-750', 'Healthy');",
            connection))
        {
            seed.Parameters.AddWithValue("client_id", clientId);
            seed.Parameters.AddWithValue("facility_a", facilityIds[0]);
            seed.Parameters.AddWithValue("facility_b", facilityIds[1]);
            await seed.ExecuteNonQueryAsync();
        }

        try
        {
            var tasks = Enumerable.Range(0, 10).Select(index => Task.Run(() =>
            {
                var facilityId = facilityIds[index % facilityIds.Length];
                var session = new PostgresSession();
                var handler = new PledgeCollateralHandler(
                    new TestCurrentClient(ClientId.Create(clientId).Value),
                    new PostgresCreditFacilityRepository(session),
                    new PostgresCustodyHoldingRepository(session),
                    new CollateralTransferService(),
                    new FixedClock(DateTimeOffset.UtcNow),
                    new PostgresUnitOfWork(dataSource, session),
                    new PostgresIdempotencyStore(session));
                return handler.HandleAsync(new PledgeCollateralCommand(
                    facilityId, 20_000, $"pledge-contention-{index}"));
            })).ToArray();

            var results = await Task.WhenAll(tasks);
            Assert.Equal(7, results.Count(result => result.IsSuccess));
            Assert.Equal(3, results.Count(result => result.IsFailure && result.Error.Code == "InsufficientFreeGold"));
            Assert.DoesNotContain(results, result => result.IsFailure && result.Error.Code != "InsufficientFreeGold");

            await using var verify = await dataSource.OpenConnectionAsync();
            await using var balance = new NpgsqlCommand(
                "SELECT free_fine_mg FROM custody.holdings WHERE client_id = @client_id AND instrument = 'XAU-750';",
                verify);
            balance.Parameters.AddWithValue("client_id", clientId);
            Assert.Equal(10_000L, (long)(await balance.ExecuteScalarAsync())!);
        }
        finally
        {
            await using var cleanup = await dataSource.OpenConnectionAsync();
            await using var delete = new NpgsqlCommand(
                "DELETE FROM platform.idempotency_records WHERE client_id = @client_id; " +
                "DELETE FROM credit.facilities WHERE facility_id = ANY(@facility_ids); " +
                "DELETE FROM custody.holdings WHERE client_id = @client_id;",
                cleanup);
            delete.Parameters.AddWithValue("client_id", clientId);
            delete.Parameters.AddWithValue("facility_ids", facilityIds);
            await delete.ExecuteNonQueryAsync();
        }
    }

    [PostgresIntegrationFact]
    public async Task Release_drawdown_write_skew_race_preserves_ltv_for_fifty_iterations()
    {
        await using var dataSource = CreateDataSource();
        await new MigrationRunner(dataSource).ApplyAsync();

        var clientIds = new Guid[50];
        var facilityIds = new Guid[50];
        var fixedNow = DateTimeOffset.UtcNow;
        var quote = new ReferencePriceQuote(ReferencePrice.Create(120_000_000).Value, fixedNow);

        for (var index = 0; index < 50; index++)
        {
            clientIds[index] = Guid.NewGuid();
            facilityIds[index] = Guid.NewGuid();
            await SeedWriteSkewCaseAsync(dataSource, clientIds[index], facilityIds[index]);

            var releaseSession = new PostgresSession();
            var releaseHandler = new ReleaseCollateralHandler(
                new TestCurrentClient(ClientId.Create(clientIds[index]).Value),
                new PostgresCreditFacilityRepository(releaseSession),
                new PostgresCustodyHoldingRepository(releaseSession),
                new FixedReferencePriceProvider(quote),
                new PriceFreshnessPolicy(),
                new FixedClock(fixedNow),
                new PostgresUnitOfWork(dataSource, releaseSession),
                new PostgresIdempotencyStore(releaseSession));

            var drawdownSession = new PostgresSession();
            var drawdownHandler = new ExecuteDrawdownHandler(
                new TestCurrentClient(ClientId.Create(clientIds[index]).Value),
                new PostgresCreditFacilityRepository(drawdownSession),
                new FixedReferencePriceProvider(quote),
                new PriceFreshnessPolicy(),
                new FixedClock(fixedNow),
                new PostgresUnitOfWork(dataSource, drawdownSession),
                new PostgresIdempotencyStore(drawdownSession),
                new PostgresOutboxWriter(drawdownSession));

            var operations = await Task.WhenAll(
                Task.Run(async () => (await releaseHandler.HandleAsync(
                    new ReleaseCollateralCommand(facilityIds[index], 30_000, $"release-{index}"))).IsSuccess),
                Task.Run(async () => (await drawdownHandler.HandleAsync(
                    new ExecuteDrawdownCommand(facilityIds[index], 3_000_000_000, $"write-skew-{index}"))).IsSuccess));
            Assert.Single(operations, succeeded => succeeded);

            await using var verify = await dataSource.OpenConnectionAsync();
            await using var query = new NpgsqlCommand(
                "SELECT collateral_fine_mg, debt_irr FROM credit.facilities WHERE facility_id = @facility_id;",
                verify);
            query.Parameters.AddWithValue("facility_id", facilityIds[index]);
            await using var reader = await query.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            var collateral = FineWeightMg.Create(reader.GetInt64(0)).Value;
            var debt = Irr.Create(reader.GetInt64(1)).Value;
            var value = CollateralValue.Calculate(collateral, quote.Price).Value;
            Assert.False(Ltv.Calculate(debt, value).Exceeds(MarginThresholds.MaximumClientLtvBps),
                $"Write skew left iteration {index} above the client LTV cap.");
        }

        await using var cleanup = await dataSource.OpenConnectionAsync();
        await using var deleteAll = new NpgsqlCommand(
            "DELETE FROM platform.outbox_messages WHERE aggregate_id = ANY(@aggregate_ids); " +
            "DELETE FROM platform.idempotency_records WHERE client_id = ANY(@client_ids); " +
            "DELETE FROM credit.facilities WHERE facility_id = ANY(@facility_ids); " +
            "DELETE FROM custody.holdings WHERE client_id = ANY(@client_ids);",
            cleanup);
        deleteAll.Parameters.AddWithValue("aggregate_ids", facilityIds.Select(id => id.ToString("D")).ToArray());
        deleteAll.Parameters.AddWithValue("client_ids", clientIds);
        deleteAll.Parameters.AddWithValue("facility_ids", facilityIds);
        await deleteAll.ExecuteNonQueryAsync();
    }

    private static async Task SeedWriteSkewCaseAsync(NpgsqlDataSource dataSource, Guid clientId, Guid facilityId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var seed = new NpgsqlCommand(
            "INSERT INTO custody.holdings (client_id, instrument, free_fine_mg) VALUES (@client_id, 'XAU-750', 0); " +
            "INSERT INTO credit.facilities " +
            "(facility_id, client_id, instrument, collateral_fine_mg, debt_irr, status, version) " +
            "VALUES (@facility_id, @client_id, 'XAU-750', 100000, 6000000000, 'Healthy', 0);",
            connection);
        seed.Parameters.AddWithValue("client_id", clientId);
        seed.Parameters.AddWithValue("facility_id", facilityId);
        await seed.ExecuteNonQueryAsync();
    }

    private static NpgsqlDataSource CreateDataSource() =>
        NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("SANACASH_TEST_CONNECTION")!);

    private static PriceTick CreateTick(long sequence, DateTimeOffset timestamp)
    {
        var id = TickId.Create(Instrument.Xau750, sequence).Value;
        return PriceTick.Create(id, ReferencePrice.Create(120_000_000).Value, timestamp);
    }

    private static async Task<bool> InsertTickAsync(NpgsqlDataSource dataSource, PriceTick tick)
    {
        var session = new PostgresSession();
        var repository = new PostgresPriceTickRepository(session, dataSource);
        return await new PostgresUnitOfWork(dataSource, session).ExecuteInTransactionAsync(
            cancellationToken => repository.TryAddAsync(tick, cancellationToken));
    }

private class TestCurrentClient(ClientId clientId) : ICurrentClient
    {
        public ClientId ClientId { get; } = clientId;
        public bool IsRiskOfficer => false;
    }

private class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

private class FixedReferencePriceProvider(ReferencePriceQuote quote) : IReferencePriceProvider
    {
        public Task<ReferencePriceQuote?> GetLatestAsync(
            Instrument instrument,
            CancellationToken cancellationToken = default) => Task.FromResult<ReferencePriceQuote?>(quote);
    }
}
