using SanaCash.GoldCredit.Domain.Credit.Enums;
using SanaCash.GoldCredit.Domain.Shared;

namespace SanaCash.GoldCredit.Domain.Credit;

public sealed record MarginTransition(
    Guid EventId,
    FacilityId FacilityId,
    long ExpectedVersion,
    FacilityStatus ExpectedStatus,
    FacilityStatus NewStatus,
    MarginEventType EventType,
    MarginEvidence Evidence,
    DateTimeOffset OccurredAtUtc);