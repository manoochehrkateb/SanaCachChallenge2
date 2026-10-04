using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Custody.Errors;

public static class CustodyErrors
{
    public static Error InsufficientFreeGold { get; } = new("InsufficientFreeGold", "The client does not have enough free gold.");
    public static Error HoldingNotFound { get; } = new("HoldingNotFound", "Custody holding was not found.");
}