using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Pricing;

public sealed record MinuteBucket : ValueObject
{
    private MinuteBucket(DateTimeOffset startUtc) => StartUtc = startUtc;

    public DateTimeOffset StartUtc { get; }
    public DateTimeOffset ClosesAtUtc => StartUtc.AddMinutes(1);
    public DateTimeOffset EvaluableAtUtc => ClosesAtUtc.AddSeconds(5);

    public static MinuteBucket From(DateTimeOffset timestamp)
    {
        var utc = timestamp.ToUniversalTime();
        var start = new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero);
        return new MinuteBucket(start);
    }
}