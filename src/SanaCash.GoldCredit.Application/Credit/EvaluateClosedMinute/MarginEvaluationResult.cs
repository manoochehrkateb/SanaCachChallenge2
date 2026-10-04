namespace SanaCash.GoldCredit.Application.Credit.EvaluateClosedMinute;

public sealed record MarginEvaluationResult(
    bool Applied,
    bool PausedForStaleFeed,
    bool SkippedGap,
    int FacilitiesEvaluated,
    int TransitionsProposed);