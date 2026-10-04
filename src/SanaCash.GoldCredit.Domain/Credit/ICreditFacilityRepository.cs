using SanaCash.GoldCredit.Domain.Shared;

namespace SanaCash.GoldCredit.Domain.Credit;

public interface ICreditFacilityRepository
{
    Task<CreditFacility?> GetForUpdateAsync(FacilityId facilityId, CancellationToken cancellationToken = default);
    Task UpdateAsync(CreditFacility facility, CancellationToken cancellationToken = default);
}