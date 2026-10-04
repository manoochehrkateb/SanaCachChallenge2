namespace SanaCash.GoldCredit.Application.Pricing.IngestPriceTick;

public sealed record PriceTickIngestionResult(bool Accepted, bool Duplicate, string? RejectionReason);