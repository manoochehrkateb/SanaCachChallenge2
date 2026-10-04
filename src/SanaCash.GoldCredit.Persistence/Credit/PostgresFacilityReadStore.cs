using Npgsql;
using SanaCash.GoldCredit.Application.Abstractions.Data;

namespace SanaCash.GoldCredit.Persistence.Credit;

public sealed class PostgresFacilityReadStore(NpgsqlDataSource dataSource) : IFacilityReadStore
{
    public async Task<FacilityReadModel?> GetFacilityAsync(Guid facilityId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT facility_id, client_id, collateral_fine_mg, debt_irr, status, version " +
            "FROM credit.facilities WHERE facility_id = @facility_id;",
            connection);
        command.Parameters.AddWithValue("facility_id", facilityId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new FacilityReadModel(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetInt64(2), reader.GetInt64(3),
                reader.GetString(4), reader.GetInt64(5))
            : null;
    }

    public async Task<IReadOnlyList<MarginEventReadModel>> GetMarginEventsAsync(
        Guid facilityId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT event_id, event_type, episode_number, occurred_at, evidence::text " +
            "FROM credit.margin_events WHERE facility_id = @facility_id ORDER BY occurred_at, event_id;",
            connection);
        command.Parameters.AddWithValue("facility_id", facilityId);

        var results = new List<MarginEventReadModel>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new MarginEventReadModel(
                reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2),
                reader.GetFieldValue<DateTimeOffset>(3), reader.GetString(4)));
        }

        return results;
    }
}