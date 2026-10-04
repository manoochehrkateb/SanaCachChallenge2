using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Credit;

public sealed record Irr : ValueObject
{
    private Irr(long value) => Value = value;

    public long Value { get; }
    public static Irr Zero { get; } = new(0);

    public static Result<Irr> Create(long value) => value < 0
        ? Result<Irr>.Failure(new Error("InvalidIrr", "IRR amount cannot be negative."))
        : Result<Irr>.Success(new Irr(value));

    public Result<Irr> Add(Irr other)
    {
        try
        {
            return Result<Irr>.Success(new Irr(checked(Value + other.Value)));
        }
        catch (OverflowException)
        {
            return Result<Irr>.Failure(new Error("IrrOverflow", "IRR amount exceeds the supported range."));
        }
    }

    public Result<Irr> Subtract(Irr other) => other.Value > Value
        ? Result<Irr>.Failure(new Error("InsufficientDebt", "IRR amount cannot become negative."))
        : Result<Irr>.Success(new Irr(Value - other.Value));
}