namespace SanaCash.GoldCredit.Persistence.Pricing;

public class PriceTickEntity
{
    public DateTimeOffset Timestamp { get; set; }
    public string Instrument { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public long PriceIrrPerGram { get; set; }
}
