using Npgsql;
using SanaCash.GoldCredit.Application.Abstractions.Common;
using SanaCash.GoldCredit.Application.Abstractions.Data;
using SanaCash.GoldCredit.Application.Credit.EvaluateClosedMinute;
using SanaCash.GoldCredit.Domain.Credit;
using SanaCash.GoldCredit.Domain.Credit.Enums;
using SanaCash.GoldCredit.Domain.Pricing;
using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Persistence.Common;
using SanaCash.GoldCredit.Persistence.Credit;
using SanaCash.GoldCredit.Persistence.Migrations;

namespace SanaCash.GoldCredit.IntegrationTests;

public class MarginEvaluatorIntegrationTests
{
    [PostgresIntegrationFact]
    public async Task Three_breach_minutes_issue_one_call_and_competing_evaluators_issue_one_liquidation()
    {
        await using var dataSource = NpgsqlDataSource.Create(
            Environment.GetEnvironmentVariable("SANACASH_TEST_CONNECTION")!);
        await new MigrationRunner(dataSource).ApplyAsync();

        var instrument = Instrument.Xau750;
        var firstMinute = MinuteBucket.From(DateTimeOffset.UtcNow.AddMinutes(-8));
        var facilityId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var sequence = Random.Shared.NextInt64(1, long.MaxValue - 10);
        var originalWatermark = await new PostgresMarginEvaluationRepository(dataSource, new PostgresSession())
            .GetWatermarkAsync(instrument);

        await SetWatermarkAsync(dataSource, firstMinute.StartUtc.AddMinutes(-1));
        await InsertFacilityAsync(dataSource, facilityId, clientId);

        try
        {
            for (var index = 0; index < 3; index++)
            {
                var minute = firstMinute.StartUtc.AddMinutes(index);
                await InsertTickAsync(dataSource, sequence + index, minute.AddSeconds(30), 102_000_000);
                var loaded = await new PostgresMarginEvaluationRepository(dataSource, new PostgresSession())
                    .LoadInputsAsync(MinuteBucket.From(minute));
                var loadedFacility = Assert.Single(loaded.Facilities, input => input.FacilityId.Value == facilityId);
                Assert.Equal(FacilityStatus.Healthy, loadedFacility.Status);
                Assert.Equal(0, loadedFacility.Version);
                Assert.Equal(7_059, (int)loadedFacility.RecentSnapshots[^1].Ltv.BasisPoints);
                Assert.Equal(MinuteBucket.From(minute), loadedFacility.RecentSnapshots[^1].Minute);
                if (index == 2)
                {
                    Assert.Equal(FacilityStatus.MarginCall,
                        new MarginPolicy().Evaluate(loadedFacility.Status, loadedFacility.RecentSnapshots));
                }
                var result = await CreateHandler(dataSource, minute.AddMinutes(1).AddSeconds(5))
                    .EvaluateLatestAsync();

                Assert.True(result.Applied,
                    $"Minute {minute:O} was not applied; stale={result.PausedForStaleFeed}, gap={result.SkippedGap}, evaluated={result.FacilitiesEvaluated}.");
                Assert.Equal(index + 1, await GetEvaluationCountAsync(dataSource, facilityId));
                if (index < 2)
                {
                    Assert.Equal(0, result.TransitionsProposed);
                }
                else
                {
                    Assert.Equal(1, result.TransitionsProposed);
                }
            }

            await using (var state = await dataSource.OpenConnectionAsync())
            await using (var command = new NpgsqlCommand(
                "SELECT status FROM credit.facilities WHERE facility_id = @facility_id;",
                state))
            {
                command.Parameters.AddWithValue("facility_id", facilityId);
                Assert.Equal("MarginCall", await command.ExecuteScalarAsync());
            }

            var liquidationMinute = firstMinute.StartUtc.AddMinutes(3);
            await InsertTickAsync(dataSource, sequence + 3, liquidationMinute.AddSeconds(30), 89_000_000);
            var evaluationTime = liquidationMinute.AddMinutes(1).AddSeconds(5);
            var competing = await Task.WhenAll(
                Task.Run(() => CreateHandler(dataSource, evaluationTime).EvaluateLatestAsync()),
                Task.Run(() => CreateHandler(dataSource, evaluationTime).EvaluateLatestAsync()));

            Assert.Equal(1, competing.Count(result => result.Applied));

            await using var verify = await dataSource.OpenConnectionAsync();
            await using (var status = new NpgsqlCommand(
                "SELECT status FROM credit.facilities WHERE facility_id = @facility_id;",
                verify))
            {
                status.Parameters.AddWithValue("facility_id", facilityId);
                Assert.Equal("LiquidationRequired", await status.ExecuteScalarAsync());
            }

            await using (var events = new NpgsqlCommand(
                "SELECT event_type, count(*) FROM credit.margin_events " +
                "WHERE facility_id = @facility_id GROUP BY event_type;",
                verify))
            {
                events.Parameters.AddWithValue("facility_id", facilityId);
                await using var reader = await events.ExecuteReaderAsync();
                var eventCounts = new Dictionary<string, long>();
                while (await reader.ReadAsync())
                {
                    eventCounts.Add(reader.GetString(0), reader.GetInt64(1));
                }

                Assert.Equal(1, eventCounts["MarginCallIssued"]);
                Assert.Equal(1, eventCounts["LiquidationRequired"]);
            }

            await using (var evidence = new NpgsqlCommand(
                "SELECT jsonb_array_length(evidence) FROM credit.margin_events " +
                "WHERE facility_id = @facility_id AND event_type = 'MarginCallIssued';",
                verify))
            {
                evidence.Parameters.AddWithValue("facility_id", facilityId);
                Assert.Equal(3, (int)(await evidence.ExecuteScalarAsync())!);
            }

            await using (var outbox = new NpgsqlCommand(
                "SELECT count(*) FROM platform.outbox_messages WHERE aggregate_id = @facility_id;",
                verify))
            {
                outbox.Parameters.AddWithValue("facility_id", facilityId.ToString("D"));
                Assert.Equal(2L, (long)(await outbox.ExecuteScalarAsync())!);
            }
        }
        finally
        {
            await CleanupAsync(dataSource, facilityId, sequence);
            if (originalWatermark is null)
            {
                await using var connection = await dataSource.OpenConnectionAsync();
                await using var command = new NpgsqlCommand(
                    "DELETE FROM platform.evaluator_state WHERE instrument = 'XAU-750';", connection);
                await command.ExecuteNonQueryAsync();
            }
            else
            {
                await SetWatermarkAsync(dataSource, originalWatermark.StartUtc);
            }
        }
    }

    private static EvaluateClosedMinuteHandler CreateHandler(NpgsqlDataSource dataSource, DateTimeOffset now)
    {
        var session = new PostgresSession();
        return new EvaluateClosedMinuteHandler(
            new PostgresMarginEvaluationRepository(dataSource, session),
            new MarginPolicy(),
            new FeedStalenessPolicy(),
            new FixedClock(now),
            new PostgresUnitOfWork(dataSource, session));
    }

    private static async Task InsertFacilityAsync(NpgsqlDataSource dataSource, Guid facilityId, Guid clientId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO credit.facilities " +
            "(facility_id, client_id, instrument, collateral_fine_mg, debt_irr, status, version) " +
            "VALUES (@facility_id, @client_id, 'XAU-750', 100000, 9600000000, 'Healthy', 0);",
            connection);
        command.Parameters.AddWithValue("facility_id", facilityId);
        command.Parameters.AddWithValue("client_id", clientId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertTickAsync(
        NpgsqlDataSource dataSource,
        long sequence,
        DateTimeOffset timestamp,
        long price)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var key = new NpgsqlCommand(
            "INSERT INTO pricing.price_tick_keys (instrument, sequence, payload_hash) " +
            "VALUES ('XAU-750', @sequence, @hash);",
            connection,
            transaction))
        {
            key.Parameters.AddWithValue("sequence", sequence);
            key.Parameters.AddWithValue("hash", BitConverter.GetBytes(sequence));
            await key.ExecuteNonQueryAsync();
        }

        await using (var tick = new NpgsqlCommand(
            "INSERT INTO pricing.price_ticks (ts, instrument, sequence, price_irr_per_gram) " +
            "VALUES (@timestamp, 'XAU-750', @sequence, @price);",
            connection,
            transaction))
        {
            tick.Parameters.AddWithValue("timestamp", timestamp);
            tick.Parameters.AddWithValue("sequence", sequence);
            tick.Parameters.AddWithValue("price", price);
            await tick.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private static async Task SetWatermarkAsync(NpgsqlDataSource dataSource, DateTimeOffset minute)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO platform.evaluator_state (instrument, last_evaluated_minute) " +
            "VALUES ('XAU-750', @minute) ON CONFLICT (instrument) DO UPDATE " +
            "SET last_evaluated_minute = EXCLUDED.last_evaluated_minute, updated_at = now();",
            connection);
        command.Parameters.AddWithValue("minute", minute);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CleanupAsync(NpgsqlDataSource dataSource, Guid facilityId, long sequence)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM platform.outbox_messages WHERE aggregate_id = @facility_text; " +
            "DELETE FROM credit.margin_events WHERE facility_id = @facility_id; " +
            "DELETE FROM credit.margin_evaluations WHERE facility_id = @facility_id; " +
            "DELETE FROM credit.facilities WHERE facility_id = @facility_id; " +
            "DELETE FROM pricing.price_ticks WHERE instrument = 'XAU-750' AND sequence BETWEEN @sequence AND @last_sequence; " +
            "DELETE FROM pricing.price_tick_keys WHERE instrument = 'XAU-750' AND sequence BETWEEN @sequence AND @last_sequence;",
            connection);
        command.Parameters.AddWithValue("facility_text", facilityId.ToString("D"));
        command.Parameters.AddWithValue("facility_id", facilityId);
        command.Parameters.AddWithValue("sequence", sequence);
        command.Parameters.AddWithValue("last_sequence", sequence + 3);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> GetEvaluationCountAsync(NpgsqlDataSource dataSource, Guid facilityId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM credit.margin_evaluations WHERE facility_id = @facility_id;",
            connection);
        command.Parameters.AddWithValue("facility_id", facilityId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

private class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
