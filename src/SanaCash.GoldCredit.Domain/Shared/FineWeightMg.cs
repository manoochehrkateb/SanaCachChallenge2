using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Shared;

public sealed record FineWeightMg : ValueObject
{
    private FineWeightMg(long value) => Value = value;

    public long Value { get; }
    public static FineWeightMg Zero { get; } = new(0);

    public static Result<FineWeightMg> Create(long value) => value < 0
        ? Result<FineWeightMg>.Failure(new Error("InvalidFineWeight", "Fine weight cannot be negative."))
        : Result<FineWeightMg>.Success(new FineWeightMg(value));

    public static Result<FineWeightMg> Create(decimal value)
    {
        if (value < 0 || value > long.MaxValue || decimal.Truncate(value) != value)
        {
            return Result<FineWeightMg>.Failure(new Error("InvalidFineWeight", "Fine weight must be a non-negative whole number of milligrams."));
        }

        return Result<FineWeightMg>.Success(new FineWeightMg((long)value));
    }

    public Result<FineWeightMg> Add(FineWeightMg other)
    {
        try
        {
            return Result<FineWeightMg>.Success(new FineWeightMg(checked(Value + other.Value)));
        }
        catch (OverflowException)
        {
            return Result<FineWeightMg>.Failure(new Error("FineWeightOverflow", "Fine weight exceeds the supported range."));
        }
    }

    public Result<FineWeightMg> Subtract(FineWeightMg other) => other.Value > Value
        ? Result<FineWeightMg>.Failure(new Error("InsufficientFineWeight", "Fine weight cannot become negative."))
        : Result<FineWeightMg>.Success(new FineWeightMg(Value - other.Value));
}