using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Shared;

public sealed record FacilityId : ValueObject
{
    private FacilityId(Guid value) => Value = value;

    public Guid Value { get; }

    public static Result<FacilityId> Create(Guid value) => value == Guid.Empty
        ? Result<FacilityId>.Failure(new Error("InvalidFacilityId", "Facility ID cannot be empty."))
        : Result<FacilityId>.Success(new FacilityId(value));
}