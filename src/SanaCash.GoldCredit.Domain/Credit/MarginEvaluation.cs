using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Credit;

public class MarginEvaluation : Entity<MarginEvaluationId>
{
    public MarginEvaluation(MarginEvaluationId id, MinuteSnapshot snapshot) : base(id) => Snapshot = snapshot;

    public MinuteSnapshot Snapshot { get; }
}
