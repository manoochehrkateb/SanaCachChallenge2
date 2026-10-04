namespace SanaCash.GoldCredit.Domain.Credit;

public sealed record MarginEvaluationBatch(
    DateTimeOffset? LatestRealTickUtc,
    IReadOnlyList<FacilityMinuteInput> Facilities);