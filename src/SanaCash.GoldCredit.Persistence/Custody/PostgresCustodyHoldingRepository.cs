using System.Data;
using Microsoft.EntityFrameworkCore;
using SanaCash.GoldCredit.Domain.Custody;
using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Persistence.Common;

namespace SanaCash.GoldCredit.Persistence.Custody;

public class PostgresCustodyHoldingRepository(
    GoldCreditDbContext context,
    PostgresSession session) : GenericRepository<CustodyHoldingEntity>(context, session), ICustodyHoldingRepository
{
    public PostgresCustodyHoldingRepository(PostgresSession session)
        : this(CreateContext(session), session)
    {
    }

    public async Task<CustodyHolding?> GetForUpdateAsync(
        ClientId clientId,
        CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .FromSqlInterpolated($"SELECT client_id, instrument, free_fine_mg, version FROM custody.holdings WHERE client_id = {clientId.Value} AND instrument = {Instrument.Xau750.Code} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

        if (entity is null)
        {
            return null;
        }

        var instrument = Instrument.Create(entity.Instrument);
        if (instrument.IsFailure)
        {
            throw new InvalidOperationException("Persisted custody holding has an unknown instrument.");
        }

        var id = CustodyHoldingId.Create(clientId, instrument.Value);
        return CustodyHolding.Restore(
            id,
            FineWeightMg.Create(entity.FreeFineMg).Value,
            entity.Version);
    }

    public async Task UpdateAsync(
        CustodyHolding holding,
        CancellationToken cancellationToken = default)
    {
        var entity = await Entities.FindAsync(
            [holding.Id.ClientId.Value, holding.Id.Instrument.Code],
            cancellationToken);

        if (entity is null)
        {
            throw new DBConcurrencyException("Custody holding changed after it was loaded for update.");
        }

        entity.FreeFineMg = holding.FreeFineWeight.Value;
        entity.Version = holding.Version;
        Context.Entry(entity).Property(item => item.Version).OriginalValue = checked(holding.Version - 1);

        try
        {
            await SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new DBConcurrencyException(
                "Custody holding changed after it was loaded for update.",
                exception);
        }
    }

    private static GoldCreditDbContext CreateContext(PostgresSession session)
    {
        var context = new GoldCreditDbContext(
            new DbContextOptionsBuilder<GoldCreditDbContext>()
                .UseNpgsql(session.Connection)
                .Options);
        context.Database.UseTransaction(session.Transaction);
        return context;
    }
}
