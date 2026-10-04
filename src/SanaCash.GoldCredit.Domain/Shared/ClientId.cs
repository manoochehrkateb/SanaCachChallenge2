using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Shared;

public sealed record ClientId : ValueObject
{
    private ClientId(Guid value) => Value = value;

    public Guid Value { get; }

    public static Result<ClientId> Create(Guid value) => value == Guid.Empty
        ? Result<ClientId>.Failure(new Error("InvalidClientId", "Client ID cannot be empty."))
        : Result<ClientId>.Success(new ClientId(value));
}