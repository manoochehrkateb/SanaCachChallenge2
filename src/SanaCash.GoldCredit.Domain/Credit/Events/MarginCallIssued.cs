using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Credit.Events;

public sealed record MarginCallIssued(Guid EventId, FacilityId FacilityId, MarginEvidence Evidence,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;