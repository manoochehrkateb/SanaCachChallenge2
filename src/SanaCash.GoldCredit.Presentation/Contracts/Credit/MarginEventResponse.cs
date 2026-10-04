using System.Text.Json;

namespace SanaCash.GoldCredit.Presentation.Contracts.Credit;

public sealed record MarginEventResponse(
    Guid EventId,
    string EventType,
    int EpisodeNumber,
    DateTimeOffset OccurredAtUtc,
    JsonElement Evidence);