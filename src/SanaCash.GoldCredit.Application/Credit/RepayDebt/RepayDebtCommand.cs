namespace SanaCash.GoldCredit.Application.Credit.RepayDebt;

public sealed record RepayDebtCommand(Guid FacilityId, long AmountIrr, string IdempotencyKey);