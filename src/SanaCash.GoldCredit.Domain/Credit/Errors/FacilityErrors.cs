using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Credit.Errors;

public static class FacilityErrors
{
    public static Error NotFound { get; } = new("FacilityNotFound", "Credit facility was not found.");
    public static Error LtvLimitExceeded { get; } = new("LtvLimitExceeded", "Operation would exceed the maximum client LTV.");
    public static Error FacilityInMarginCall { get; } = new("FacilityInMarginCall", "Operation is not allowed while the facility is in margin call.");
    public static Error FacilityLiquidationRequired { get; } = new("FacilityLiquidationRequired", "Operation is not allowed after liquidation is required.");
    public static Error RepaymentExceedsDebt { get; } = new("RepaymentExceedsDebt", "Repayment exceeds outstanding debt.");
    public static Error ReleaseExceedsPledged { get; } = new("ReleaseExceedsPledged", "Release exceeds pledged collateral.");
}