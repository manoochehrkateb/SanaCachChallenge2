namespace SanaCash.GoldCredit.Application.Abstractions.Common;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}