namespace SanaCash.GoldCredit.Application.Credit.ReleaseCollateral;

public sealed record ReleaseCollateralCommand(Guid FacilityId, long FineWeightMg, string IdempotencyKey);