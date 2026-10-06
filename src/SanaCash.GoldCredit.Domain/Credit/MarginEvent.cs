using SanaCash.GoldCredit.Domain.Credit.Enums;
using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Credit;

public class MarginEvent : Entity<Guid>
{
    public MarginEvent(Guid eventId, FacilityId facilityId, MarginEventType type,
        int episodeNumber, MarginEvidence evidence, DateTimeOffset occurredAtUtc) : base(eventId)
    {
        FacilityId = facilityId;
        Type = type;
        EpisodeNumber = episodeNumber;
        Evidence = evidence;
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
    }

    public FacilityId FacilityId { get; }
    public MarginEventType Type { get; }
    public int EpisodeNumber { get; }
    public MarginEvidence Evidence { get; }
    public DateTimeOffset OccurredAtUtc { get; }
}
