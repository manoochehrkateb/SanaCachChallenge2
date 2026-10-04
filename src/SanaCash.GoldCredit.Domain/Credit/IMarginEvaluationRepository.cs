using SanaCash.GoldCredit.Domain.Pricing;
using SanaCash.GoldCredit.Domain.Shared;

namespace SanaCash.GoldCredit.Domain.Credit;

public interface IMarginEvaluationRepository
{
    Task<MinuteBucket?> GetWatermarkAsync(Instrument instrument, CancellationToken cancellationToken = default);
    Task<MarginEvaluationBatch> LoadInputsAsync(MinuteBucket minute, CancellationToken cancellationToken = default);
    Task<bool> TryApplyEvaluationAsync(
        MinuteBucket minute,
        MarginEvaluationBatch batch,
        IReadOnlyList<MarginTransition> transitions,
        CancellationToken cancellationToken = default);
}