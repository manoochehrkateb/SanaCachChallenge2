namespace SanaCash.GoldCredit.Domain.Credit;

public static class MarginThresholds
{
    public const int MaximumClientLtvBps = 6_000;
    public const int MarginCallLtvBps = 7_000;
    public const int CureLtvBps = 6_500;
    public const int LiquidationLtvBps = 8_000;
    public const int ConsecutiveBreachMinutes = 3;
}