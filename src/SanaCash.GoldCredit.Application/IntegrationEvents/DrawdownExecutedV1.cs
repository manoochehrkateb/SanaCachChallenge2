using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Application.IntegrationEvents;

public sealed record DrawdownExecutedV1(
    Guid EventId,
    Guid FacilityId,
    long AmountIrr,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;
