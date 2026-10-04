using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Shared;

public sealed record Instrument : ValueObject
{
    public const string Xau750Code = "XAU-750";

    private Instrument(string code) => Code = code;

    public string Code { get; }
    public static Instrument Xau750 { get; } = new(Xau750Code);

    public static Result<Instrument> Create(string code) => string.Equals(code, Xau750Code, StringComparison.Ordinal)
        ? Result<Instrument>.Success(Xau750)
        : Result<Instrument>.Failure(new Error("UnknownInstrument", $"Instrument '{code}' is not supported."));
}