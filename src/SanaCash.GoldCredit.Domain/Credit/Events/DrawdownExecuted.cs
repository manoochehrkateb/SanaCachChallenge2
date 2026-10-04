using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Credit.Events;

public sealed record DrawdownExecuted(Guid EventId, FacilityId FacilityId, Irr Amount,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;