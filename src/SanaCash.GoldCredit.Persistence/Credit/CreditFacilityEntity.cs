namespace SanaCash.GoldCredit.Persistence.Credit;

public class CreditFacilityEntity
{
    public Guid FacilityId { get; set; }
    public Guid ClientId { get; set; }
    public string Instrument { get; set; } = string.Empty;
    public long CollateralFineMg { get; set; }
    public long DebtIrr { get; set; }
    public string Status { get; set; } = string.Empty;
    public long Version { get; set; }
}
