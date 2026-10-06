using Microsoft.EntityFrameworkCore;
using SanaCash.GoldCredit.Application.Abstractions.Data;
using SanaCash.GoldCredit.Persistence.Common;

namespace SanaCash.GoldCredit.Persistence.Credit;

public class PostgresFacilityReadStore(
    GoldCreditDbContext context,
    PostgresSession session,
    IGenericRepository<MarginEventEntity> marginEvents) : GenericRepository<CreditFacilityEntity>(context, session), IFacilityReadStore
{
    public async Task<FacilityReadModel?> GetFacilityAsync(
        Guid facilityId,
        CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.FacilityId == facilityId, cancellationToken);

        return entity is null
            ? null
            : new FacilityReadModel(
                entity.FacilityId,
                entity.ClientId,
                entity.CollateralFineMg,
                entity.DebtIrr,
                entity.Status,
                entity.Version);
    }

    public async Task<IReadOnlyList<MarginEventReadModel>> GetMarginEventsAsync(
        Guid facilityId,
        CancellationToken cancellationToken = default)
    {
        return await marginEvents.Query()
            .AsNoTracking()
            .Where(item => item.FacilityId == facilityId)
            .OrderBy(item => item.OccurredAt)
            .ThenBy(item => item.EventId)
            .Select(item => new MarginEventReadModel(
                item.EventId,
                item.EventType,
                item.EpisodeNumber,
                item.OccurredAt,
                item.Evidence))
            .ToListAsync(cancellationToken);
    }
}
