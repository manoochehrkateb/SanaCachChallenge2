using SanaCash.GoldCredit.Domain.Shared;

namespace SanaCash.GoldCredit.Domain.Custody;

public interface ICustodyHoldingRepository
{
    Task<CustodyHolding?> GetForUpdateAsync(ClientId clientId, CancellationToken cancellationToken = default);
    Task UpdateAsync(CustodyHolding holding, CancellationToken cancellationToken = default);
}