using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Credit.Events;

public sealed record CollateralPledged(Guid EventId, FacilityId FacilityId, FineWeightMg Amount,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;