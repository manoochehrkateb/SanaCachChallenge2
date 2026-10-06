using SanaCash.GoldCredit.Application.Abstractions.Authentication;
using SanaCash.GoldCredit.Application.Abstractions.Data;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Application.Credit.GetMarginEvents;

public class GetMarginEventsHandler(ICurrentClient currentClient, IFacilityReadStore facilities)
{
    public async Task<Result<IReadOnlyList<MarginEventReadModel>>> HandleAsync(
        Guid facilityId,
        CancellationToken cancellationToken = default)
    {
        var facility = await facilities.GetFacilityAsync(facilityId, cancellationToken);
        if (facility is null || (!currentClient.IsRiskOfficer && facility.ClientId != currentClient.ClientId.Value))
        {
            return Result<IReadOnlyList<MarginEventReadModel>>.Failure(
                new Error("FacilityNotFound", "Credit facility was not found."));
        }

        return Result<IReadOnlyList<MarginEventReadModel>>.Success(
            await facilities.GetMarginEventsAsync(facilityId, cancellationToken));
    }
}
