namespace SanaCash.GoldCredit.Persistence.Pricing;

public class PriceCandleEntity
{
    public DateTimeOffset Bucket { get; set; }
    public string Instrument { get; set; } = string.Empty;
    public long OpenIrrPerGram { get; set; }
    public long HighIrrPerGram { get; set; }
    public long LowIrrPerGram { get; set; }
    public long CloseIrrPerGram { get; set; }
    public long TickCount { get; set; }
}
