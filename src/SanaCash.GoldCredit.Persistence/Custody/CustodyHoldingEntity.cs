namespace SanaCash.GoldCredit.Persistence.Custody;

public class CustodyHoldingEntity
{
    public Guid ClientId { get; set; }
    public string Instrument { get; set; } = string.Empty;
    public long FreeFineMg { get; set; }
    public long Version { get; set; }
}
