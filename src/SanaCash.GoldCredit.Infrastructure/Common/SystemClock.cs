using SanaCash.GoldCredit.Application.Abstractions.Common;

namespace SanaCash.GoldCredit.Infrastructure.Common;

public class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
