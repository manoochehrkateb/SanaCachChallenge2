namespace SanaCash.GoldCredit.Infrastructure.Messaging;

public sealed class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string ReferencePricesTopic { get; set; } = "market.reference-prices.v1";
    public string FacilityEventsTopic { get; set; } = "credit.facility-events.v1";
    public string ConsumerGroupId { get; set; } = "sanacash-goldcredit-prices";
}