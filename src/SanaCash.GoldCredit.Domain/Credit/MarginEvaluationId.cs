using SanaCash.GoldCredit.Domain.Pricing;
using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Credit;

public sealed record MarginEvaluationId : ValueObject
{
    public MarginEvaluationId(FacilityId facilityId, MinuteBucket minute)
    {
        FacilityId = facilityId;
        Minute = minute;
    }

    public FacilityId FacilityId { get; }
    public MinuteBucket Minute { get; }
}