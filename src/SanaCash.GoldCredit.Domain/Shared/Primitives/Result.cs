namespace SanaCash.GoldCredit.Domain.Shared.Primitives;

public readonly struct Result
{
    private Result(bool isSuccess, Error error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);
}

public readonly struct Result<T>
{
    private readonly T? _value;

    private Result(T value)
    {
        _value = value;
        Error = Error.None;
        IsSuccess = true;
    }

    private Result(Error error)
    {
        _value = default;
        Error = error;
        IsSuccess = false;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("A failed result has no value.");

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(Error error) => new(error);
}