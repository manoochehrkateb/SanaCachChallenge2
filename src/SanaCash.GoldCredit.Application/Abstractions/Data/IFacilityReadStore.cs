namespace SanaCash.GoldCredit.Application.Abstractions.Data;

public interface IFacilityReadStore
{
    Task<FacilityReadModel?> GetFacilityAsync(Guid facilityId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MarginEventReadModel>> GetMarginEventsAsync(Guid facilityId, CancellationToken cancellationToken = default);
}