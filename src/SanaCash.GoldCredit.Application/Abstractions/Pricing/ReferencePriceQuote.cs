using SanaCash.GoldCredit.Domain.Shared;

namespace SanaCash.GoldCredit.Application.Abstractions.Pricing;

public sealed record ReferencePriceQuote(ReferencePrice Price, DateTimeOffset TimestampUtc);