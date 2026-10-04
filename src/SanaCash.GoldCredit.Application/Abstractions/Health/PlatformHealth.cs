namespace SanaCash.GoldCredit.Application.Abstractions.Health;

public sealed record PlatformHealth(bool DatabaseHealthy, bool KafkaHealthy, bool PriceFeedHealthy)
{
    public bool IsHealthy => DatabaseHealthy && KafkaHealthy && PriceFeedHealthy;
}