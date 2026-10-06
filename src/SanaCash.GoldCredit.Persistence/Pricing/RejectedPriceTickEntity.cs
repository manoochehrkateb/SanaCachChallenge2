namespace SanaCash.GoldCredit.Persistence.Pricing;

public class RejectedPriceTickEntity
{
    public Guid Id { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public string? Instrument { get; set; }
    public long? Sequence { get; set; }
    public string Payload { get; set; } = string.Empty;
    public string RejectionReason { get; set; } = string.Empty;
}
