public class MarginEventEntity
{
    public Guid EventId { get; set; }
    public Guid FacilityId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int EpisodeNumber { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Evidence { get; set; } = string.Empty;
}
