using System.Data;
using Npgsql;
using SanaCash.GoldCredit.Domain.Credit;
using SanaCash.GoldCredit.Domain.Credit.Enums;
using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Persistence.Common;

namespace SanaCash.GoldCredit.Persistence.Credit;

public class PostgresCreditFacilityRepository(PostgresSession session) : ICreditFacilityRepository
{
    public async Task<CreditFacility?> GetForUpdateAsync(FacilityId facilityId, CancellationToken cancellationToken = default)
    {
        await using var command = new NpgsqlCommand(
            "SELECT facility_id, client_id, collateral_fine_mg, debt_irr, status, version " +
            "FROM credit.facilities WHERE facility_id = @facility_id FOR UPDATE;",
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("facility_id", facilityId.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return CreditFacility.Restore(
            FacilityId.Create(reader.GetGuid(0)).Value,
            ClientId.Create(reader.GetGuid(1)).Value,
            FineWeightMg.Create(reader.GetInt64(2)).Value,
            Irr.Create(reader.GetInt64(3)).Value,
            Enum.Parse<FacilityStatus>(reader.GetString(4), ignoreCase: false),
            reader.GetInt64(5));
    }

    public async Task UpdateAsync(CreditFacility facility, CancellationToken cancellationToken = default)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE credit.facilities SET collateral_fine_mg = @collateral, debt_irr = @debt, " +
            "status = @status, version = @version WHERE facility_id = @facility_id AND version = @expected_version;",
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("collateral", facility.Collateral.Value);
        command.Parameters.AddWithValue("debt", facility.Debt.Value);
        command.Parameters.AddWithValue("status", facility.Status.ToString());
        command.Parameters.AddWithValue("version", facility.Version);
        command.Parameters.AddWithValue("expected_version", checked(facility.Version - 1));
        command.Parameters.AddWithValue("facility_id", facility.Id.Value);

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new DBConcurrencyException("Credit facility changed after it was loaded for update.");
        }
    }
}
