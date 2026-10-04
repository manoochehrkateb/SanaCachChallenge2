namespace SanaCash.GoldCredit.Application.Credit.ExecuteDrawdown;

public sealed record ExecuteDrawdownCommand(Guid FacilityId, long AmountIrr, string IdempotencyKey);