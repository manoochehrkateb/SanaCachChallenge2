using System.Globalization;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using SanaCash.GoldCredit.Domain.Credit;
using SanaCash.GoldCredit.Domain.Credit.Enums;
using SanaCash.GoldCredit.Domain.Pricing;
using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Persistence.Common;

namespace SanaCash.GoldCredit.Persistence.Credit;

public class PostgresMarginEvaluationRepository(
    NpgsqlDataSource dataSource,
    PostgresSession session) : IMarginEvaluationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task<MinuteBucket?> GetWatermarkAsync(Instrument instrument, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT last_evaluated_minute FROM platform.evaluator_state WHERE instrument = @instrument;",
            connection);
        command.Parameters.AddWithValue("instrument", instrument.Code);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        var timestamp = ReadUtcTimestamp(value);
        return timestamp is not null ? MinuteBucket.From(timestamp.Value) : null;
    }

    public async Task<MarginEvaluationBatch> LoadInputsAsync(MinuteBucket minute, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        DateTimeOffset? latestRealTickUtc;
        await using (var latest = new NpgsqlCommand(
            "SELECT max(ts) FROM pricing.price_ticks WHERE instrument = 'XAU-750';",
            connection))
        {
            var value = await latest.ExecuteScalarAsync(cancellationToken);
            latestRealTickUtc = ReadUtcTimestamp(value);
        }

        const string sql = """
            WITH minute_price AS
            (
                SELECT COALESCE(
                    (SELECT close_irr_per_gram FROM pricing.price_candles_1m
                     WHERE instrument = 'XAU-750' AND bucket = @minute),
                    (SELECT price_irr_per_gram FROM pricing.price_ticks
                     WHERE instrument = 'XAU-750' AND ts < @minute + INTERVAL '1 minute'
                     ORDER BY ts DESC LIMIT 1)) AS close_irr
            ),
            current_rows AS
            (
                SELECT f.facility_id, f.status, f.version, @minute::timestamptz AS minute,
                       mp.close_irr::bigint AS close_irr,
                       f.debt_irr, f.collateral_fine_mg,
                       floor(f.collateral_fine_mg::numeric * mp.close_irr / 750)::bigint AS collateral_value_irr
                FROM credit.facilities AS f
                CROSS JOIN minute_price AS mp
                WHERE f.status IN ('Healthy', 'MarginCall') AND mp.close_irr IS NOT NULL
            ),
            previous_rows AS
            (
                SELECT f.facility_id, f.status, f.version, e.minute,
                       e.close_irr_per_gram AS close_irr, e.debt_irr, e.collateral_fine_mg,
                       e.collateral_value_irr
                FROM credit.facilities AS f
                JOIN credit.margin_evaluations AS e ON e.facility_id = f.facility_id
                WHERE f.status IN ('Healthy', 'MarginCall')
                  AND e.minute >= @minute - INTERVAL '2 minutes'
                  AND e.minute < @minute
            )
            SELECT facility_id, status, version, minute, close_irr, debt_irr,
                   collateral_fine_mg, collateral_value_irr
            FROM current_rows
            UNION ALL
            SELECT facility_id, status, version, minute, close_irr, debt_irr,
                   collateral_fine_mg, collateral_value_irr
            FROM previous_rows
            ORDER BY facility_id, minute;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("minute", minute.StartUtc);

        var grouped = new Dictionary<Guid, (FacilityStatus Status, long Version, List<MinuteSnapshot> Snapshots)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var facilityId = reader.GetGuid(0);
            var status = Enum.Parse<FacilityStatus>(reader.GetString(1), ignoreCase: false);
            var version = reader.GetInt64(2);
            var snapshot = CreateSnapshot(
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetInt64(7));

            if (!grouped.TryGetValue(facilityId, out var value))
            {
                value = (status, version, []);
                grouped.Add(facilityId, value);
            }

            value.Snapshots.Add(snapshot);
        }

        var inputs = grouped.Select(pair => new FacilityMinuteInput(
            FacilityId.Create(pair.Key).Value,
            pair.Value.Status,
            pair.Value.Version,
            pair.Value.Snapshots)).ToArray();
        return new MarginEvaluationBatch(latestRealTickUtc, inputs);
    }

    public async Task<bool> TryApplyEvaluationAsync(
        MinuteBucket minute,
        MarginEvaluationBatch batch,
        IReadOnlyList<MarginTransition> transitions,
        CancellationToken cancellationToken = default)
    {
        var instrument = Instrument.Xau750;
        await using (var initialize = new NpgsqlCommand(
            "INSERT INTO platform.evaluator_state (instrument, last_evaluated_minute) " +
            "VALUES (@instrument, TIMESTAMPTZ '0001-01-01 00:00:00+00') " +
            "ON CONFLICT (instrument) DO NOTHING;",
            session.Connection,
            session.Transaction))
        {
            initialize.Parameters.AddWithValue("instrument", instrument.Code);
            await initialize.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var lockWatermark = new NpgsqlCommand(
            "SELECT last_evaluated_minute FROM platform.evaluator_state WHERE instrument = @instrument FOR UPDATE;",
            session.Connection,
            session.Transaction))
        {
            lockWatermark.Parameters.AddWithValue("instrument", instrument.Code);
            var last = await lockWatermark.ExecuteScalarAsync(cancellationToken);
            var lastMinute = ReadUtcTimestamp(last);
            if (lastMinute is not null && lastMinute.Value >= minute.StartUtc)
            {
                return false;
            }
        }

        await InsertEvaluationsAsync(minute, batch, cancellationToken);
        await ApplyTransitionsAsync(transitions, cancellationToken);

        await using var updateWatermark = new NpgsqlCommand(
            "UPDATE platform.evaluator_state SET last_evaluated_minute = @minute, updated_at = now() " +
            "WHERE instrument = @instrument;",
            session.Connection,
            session.Transaction);
        updateWatermark.Parameters.AddWithValue("minute", minute.StartUtc);
        updateWatermark.Parameters.AddWithValue("instrument", instrument.Code);
        await updateWatermark.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }

    private async Task InsertEvaluationsAsync(
        MinuteBucket minute,
        MarginEvaluationBatch batch,
        CancellationToken cancellationToken)
    {
        var rows = batch.Facilities
            .Where(input => input.RecentSnapshots.Count > 0)
            .Select(input =>
            {
                var current = input.RecentSnapshots.MaxBy(snapshot => snapshot.Minute.StartUtc)!;
                return new
                {
                    facilityId = input.FacilityId.Value,
                    expectedVersion = input.Version,
                    expectedStatus = input.Status.ToString(),
                    minute = minute.StartUtc,
                    closeIrrPerGram = current.Close.IrrPerGram,
                    debtIrr = current.Debt.Value,
                    collateralFineMg = current.Collateral.Value,
                    collateralValueIrr = current.Value.Irr,
                    ltvBps = current.Ltv.BasisPoints.ToString(CultureInfo.InvariantCulture),
                    isLtvInfinite = current.Ltv.IsInfinite
                };
            })
            .ToArray();
        var json = JsonSerializer.Serialize(rows, JsonOptions);
        await using var command = new NpgsqlCommand(
            "INSERT INTO credit.margin_evaluations " +
            "(facility_id, minute, close_irr_per_gram, debt_irr, collateral_fine_mg, collateral_value_irr, ltv_bps, is_ltv_infinite) " +
            "SELECT x.facility_id, x.minute, x.close_irr_per_gram, x.debt_irr, x.collateral_fine_mg, " +
            "x.collateral_value_irr, x.ltv_bps::numeric, x.is_ltv_infinite " +
            "FROM jsonb_to_recordset(@rows::jsonb) AS x(" +
            "facility_id uuid, expected_version bigint, expected_status text, minute timestamptz, " +
            "close_irr_per_gram bigint, debt_irr bigint, collateral_fine_mg bigint, collateral_value_irr bigint, " +
            "ltv_bps text, is_ltv_infinite boolean) " +
            "WHERE EXISTS (SELECT 1 FROM credit.facilities f WHERE f.facility_id = x.facility_id " +
            "AND f.version = x.expected_version AND f.status = x.expected_status) " +
            "ON CONFLICT (facility_id, minute) DO NOTHING;",
            session.Connection,
            session.Transaction);
        command.Parameters.Add("rows", NpgsqlDbType.Jsonb).Value = json;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ApplyTransitionsAsync(
        IReadOnlyList<MarginTransition> transitions,
        CancellationToken cancellationToken)
    {
        if (transitions.Count == 0)
        {
            return;
        }

        var rows = transitions.Select(transition => new
        {
            eventId = transition.EventId,
            facilityId = transition.FacilityId.Value,
            expectedVersion = transition.ExpectedVersion,
            expectedStatus = transition.ExpectedStatus.ToString(),
            newStatus = transition.NewStatus.ToString(),
            eventType = transition.EventType.ToString(),
            occurredAtUtc = transition.OccurredAtUtc,
            evidence = transition.Evidence.Snapshots.Select(snapshot => new
            {
                minuteStartUtc = snapshot.Minute.StartUtc,
                referencePriceIrrPerGram = snapshot.Close.IrrPerGram,
                debtIrr = snapshot.Debt.Value,
                collateralFineMg = snapshot.Collateral.Value,
                collateralValueIrr = snapshot.Value.Irr,
                ltvBps = snapshot.Ltv.BasisPoints.ToString(CultureInfo.InvariantCulture),
                isLtvInfinite = snapshot.Ltv.IsInfinite
            }).ToArray()
        }).ToArray();
        var json = JsonSerializer.Serialize(rows, JsonOptions);
        const string sql = """
            WITH input AS
            (
                SELECT * FROM jsonb_to_recordset(@rows::jsonb) AS x(
                    event_id uuid, facility_id uuid, expected_version bigint, expected_status text,
                    new_status text, event_type text, occurred_at_utc timestamptz, evidence jsonb)
            ),
            changed AS
            (
                UPDATE credit.facilities AS f
                SET status = i.new_status, version = f.version + 1
                FROM input AS i
                WHERE f.facility_id = i.facility_id
                  AND f.version = i.expected_version
                  AND f.status = i.expected_status
                RETURNING i.event_id, i.facility_id, i.event_type, i.occurred_at_utc, i.evidence
            ),
            episodes AS
            (
                SELECT c.*,
                    CASE c.event_type
                        WHEN 'MarginCallIssued' THEN COALESCE((SELECT max(e.episode_number) + 1 FROM credit.margin_events e
                            WHERE e.facility_id = c.facility_id AND e.event_type = 'MarginCallIssued'), 1)
                        WHEN 'MarginCallCured' THEN COALESCE((SELECT max(e.episode_number) FROM credit.margin_events e
                            WHERE e.facility_id = c.facility_id AND e.event_type = 'MarginCallIssued'), 1)
                        ELSE 1
                    END AS episode_number
                FROM changed c
            ),
            inserted_events AS
            (
                INSERT INTO credit.margin_events
                    (event_id, facility_id, event_type, episode_number, occurred_at, evidence)
                SELECT event_id, facility_id, event_type, episode_number, occurred_at_utc, evidence
                FROM episodes
                ON CONFLICT (facility_id, event_type, episode_number) DO NOTHING
                RETURNING event_id, facility_id, event_type, occurred_at, evidence
            )
            INSERT INTO platform.outbox_messages
                (event_id, event_type, aggregate_id, payload, occurred_at)
            SELECT e.event_id, e.event_type || 'V1', e.facility_id::text,
                   jsonb_build_object('eventId', e.event_id, 'facilityId', e.facility_id,
                       'occurredAtUtc', e.occurred_at, 'evidence', e.evidence), e.occurred_at
            FROM inserted_events e;
            """;
        await using var command = new NpgsqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.Add("rows", NpgsqlDbType.Jsonb).Value = json;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static MinuteSnapshot CreateSnapshot(
        DateTimeOffset minute,
        long closeIrr,
        long debtIrr,
        long collateralFineMg,
        long collateralValueIrr)
    {
        var price = ReferencePrice.Create(closeIrr).Value;
        var debt = Irr.Create(debtIrr).Value;
        var collateral = FineWeightMg.Create(collateralFineMg).Value;
        var value = CollateralValue.Restore(collateralValueIrr);
        return new MinuteSnapshot(
            MinuteBucket.From(minute), price, debt, collateral, value, Ltv.Calculate(debt, value));
    }

    private static DateTimeOffset? ReadUtcTimestamp(object? value) => value switch
    {
        DateTimeOffset timestamp => timestamp.ToUniversalTime(),
        DateTime timestamp => new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)),
        _ => null
    };
}
