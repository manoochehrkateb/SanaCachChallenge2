using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Shared;

public sealed record ReferencePrice : ValueObject
{
    private ReferencePrice(long irrPerGram) => IrrPerGram = irrPerGram;

    public long IrrPerGram { get; }

    public static Result<ReferencePrice> Create(long irrPerGram) => irrPerGram <= 0
        ? Result<ReferencePrice>.Failure(new Error("InvalidReferencePrice", "Reference price must be positive."))
        : Result<ReferencePrice>.Success(new ReferencePrice(irrPerGram));
}