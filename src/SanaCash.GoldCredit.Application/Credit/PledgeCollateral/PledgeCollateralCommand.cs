namespace SanaCash.GoldCredit.Application.Credit.PledgeCollateral;

public sealed record PledgeCollateralCommand(Guid FacilityId, long FineWeightMg, string IdempotencyKey);