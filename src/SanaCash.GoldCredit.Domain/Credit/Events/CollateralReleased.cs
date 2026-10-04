using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Credit.Events;

public sealed record CollateralReleased(Guid EventId, FacilityId FacilityId, FineWeightMg Amount,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;