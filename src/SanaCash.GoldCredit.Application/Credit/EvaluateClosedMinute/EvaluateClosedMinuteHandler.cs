using SanaCash.GoldCredit.Application.Abstractions.Common;
using SanaCash.GoldCredit.Application.Abstractions.Common;
using SanaCash.GoldCredit.Application.Abstractions.Data;
using SanaCash.GoldCredit.Domain.Credit;
using SanaCash.GoldCredit.Domain.Credit.Enums;
using SanaCash.GoldCredit.Domain.Pricing;
using SanaCash.GoldCredit.Domain.Pricing.Enums;
using SanaCash.GoldCredit.Domain.Shared;

namespace SanaCash.GoldCredit.Application.Credit.EvaluateClosedMinute;

public class EvaluateClosedMinuteHandler(
    IMarginEvaluationRepository evaluations,
    MarginPolicy marginPolicy,
    FeedStalenessPolicy stalenessPolicy,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<MarginEvaluationResult> EvaluateLatestAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var latestMinute = MinuteBucket.From(now.AddSeconds(-65));
        var watermark = await evaluations.GetWatermarkAsync(Instrument.Xau750, cancellationToken);
        if (watermark is not null && watermark.StartUtc >= latestMinute.StartUtc)
        {
            return new MarginEvaluationResult(false, false, false, 0, 0);
        }

        var skippedGap = watermark is not null
            && latestMinute.StartUtc - watermark.StartUtc > TimeSpan.FromMinutes(1);
        var facilitiesEvaluated = 0;
        var transitionsProposed = 0;
        var applied = false;

        if (watermark is not null)
        {
            for (var minute = watermark.StartUtc.AddMinutes(1);
                 minute < latestMinute.StartUtc;
                 minute = minute.AddMinutes(1))
            {
                var gapResult = await EvaluateMinuteAsync(
                    MinuteBucket.From(minute), allowTransitions: false, cancellationToken);
                if (gapResult.PausedForStaleFeed)
                {
                    return new MarginEvaluationResult(
                        applied, true, skippedGap, facilitiesEvaluated, transitionsProposed);
                }

                applied |= gapResult.Applied;
                facilitiesEvaluated = Math.Max(facilitiesEvaluated, gapResult.FacilitiesEvaluated);
            }
        }

        var latestResult = await EvaluateMinuteAsync(
            latestMinute, allowTransitions: true, cancellationToken);
        return new MarginEvaluationResult(
            applied || latestResult.Applied,
            latestResult.PausedForStaleFeed,
            skippedGap,
            Math.Max(facilitiesEvaluated, latestResult.FacilitiesEvaluated),
            latestResult.TransitionsProposed);
    }

    private async Task<MarginEvaluationResult> EvaluateMinuteAsync(
        MinuteBucket minute,
        bool allowTransitions,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var batch = await evaluations.LoadInputsAsync(minute, cancellationToken);
        if (stalenessPolicy.Evaluate(batch.LatestRealTickUtc, now) == FeedState.Stale)
        {
            return new MarginEvaluationResult(false, true, false, batch.Facilities.Count, 0);
        }

        var transitions = allowTransitions
            ? BuildTransitions(minute, batch, now)
            : [];
        var applied = await unitOfWork.ExecuteInTransactionAsync(
            transactionToken => evaluations.TryApplyEvaluationAsync(
                minute, batch, transitions, transactionToken),
            cancellationToken);

        return new MarginEvaluationResult(
            applied, false, false, batch.Facilities.Count, transitions.Count);
    }

    private IReadOnlyList<MarginTransition> BuildTransitions(
        MinuteBucket minute,
        MarginEvaluationBatch batch,
        DateTimeOffset now)
    {
        var transitions = new List<MarginTransition>();
        foreach (var facility in batch.Facilities)
        {
            var orderedSnapshots = facility.RecentSnapshots
                .OrderBy(snapshot => snapshot.Minute.StartUtc)
                .ToArray();
            if (orderedSnapshots.Length == 0 || orderedSnapshots[^1].Minute.StartUtc != minute.StartUtc)
            {
                continue;
            }

            var newStatus = marginPolicy.Evaluate(facility.Status, orderedSnapshots);
            if (newStatus == facility.Status)
            {
                continue;
            }

            var eventType = (facility.Status, newStatus) switch
            {
                (FacilityStatus.Healthy, FacilityStatus.MarginCall) => MarginEventType.MarginCallIssued,
                (FacilityStatus.MarginCall, FacilityStatus.Healthy) => MarginEventType.MarginCallCured,
                (_, FacilityStatus.LiquidationRequired) => MarginEventType.LiquidationRequired,
                _ => throw new InvalidOperationException(
                    $"Unexpected margin transition: {facility.Status} -> {newStatus}.")
            };
            var evidenceSnapshots = eventType == MarginEventType.MarginCallIssued
                ? orderedSnapshots.TakeLast(MarginThresholds.ConsecutiveBreachMinutes)
                : orderedSnapshots.TakeLast(1);
            var evidence = MarginEvidence.Create(evidenceSnapshots);
            if (evidence.IsFailure)
            {
                throw new InvalidOperationException(evidence.Error.Description);
            }

            transitions.Add(new MarginTransition(
                Guid.NewGuid(), facility.FacilityId, facility.Version, facility.Status,
                newStatus, eventType, evidence.Value, now));
        }

        return transitions;
    }
}
