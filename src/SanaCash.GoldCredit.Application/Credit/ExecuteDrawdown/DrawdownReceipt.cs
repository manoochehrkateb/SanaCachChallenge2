namespace SanaCash.GoldCredit.Application.Credit.ExecuteDrawdown;

public sealed record DrawdownReceipt(Guid FacilityId, long DebtIrr, long Version);