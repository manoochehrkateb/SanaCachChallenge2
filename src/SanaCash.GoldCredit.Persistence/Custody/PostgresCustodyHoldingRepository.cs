using System.Data;
using Npgsql;
using SanaCash.GoldCredit.Domain.Custody;
using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Persistence.Common;

namespace SanaCash.GoldCredit.Persistence.Custody;

public sealed class PostgresCustodyHoldingRepository(PostgresSession session) : ICustodyHoldingRepository
{
    public async Task<CustodyHolding?> GetForUpdateAsync(ClientId clientId, CancellationToken cancellationToken = default)
    {
        await using var command = new NpgsqlCommand(
            "SELECT instrument, free_fine_mg, version FROM custody.holdings " +
            "WHERE client_id = @client_id AND instrument = 'XAU-750' FOR UPDATE;",
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("client_id", clientId.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var instrument = Instrument.Create(reader.GetString(0));
        if (instrument.IsFailure)
        {
            throw new InvalidOperationException("Persisted custody holding has an unknown instrument.");
        }

        var id = CustodyHoldingId.Create(clientId, instrument.Value);
        return CustodyHolding.Restore(id, FineWeightMg.Create(reader.GetInt64(1)).Value, reader.GetInt64(2));
    }

    public async Task UpdateAsync(CustodyHolding holding, CancellationToken cancellationToken = default)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE custody.holdings SET free_fine_mg = @free_fine_mg, version = @version " +
            "WHERE client_id = @client_id AND instrument = @instrument AND version = @expected_version;",
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("free_fine_mg", holding.FreeFineWeight.Value);
        command.Parameters.AddWithValue("version", holding.Version);
        command.Parameters.AddWithValue("client_id", holding.Id.ClientId.Value);
        command.Parameters.AddWithValue("instrument", holding.Id.Instrument.Code);
        command.Parameters.AddWithValue("expected_version", checked(holding.Version - 1));

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new DBConcurrencyException("Custody holding changed after it was loaded for update.");
        }
    }
}