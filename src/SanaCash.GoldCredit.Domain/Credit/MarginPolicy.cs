using SanaCash.GoldCredit.Domain.Credit.Enums;

namespace SanaCash.GoldCredit.Domain.Credit;

public sealed class MarginPolicy
{
    public FacilityStatus Evaluate(FacilityStatus currentStatus, IReadOnlyList<MinuteSnapshot> recentSnapshots)
    {
        if (currentStatus == FacilityStatus.LiquidationRequired)
        {
            return FacilityStatus.LiquidationRequired;
        }

        if (recentSnapshots.Count == 0)
        {
            return currentStatus;
        }

        var orderedSnapshots = recentSnapshots
            .OrderBy(snapshot => snapshot.Minute.StartUtc)
            .ToArray();
        var currentLtv = orderedSnapshots[^1].Ltv;
        if (currentLtv.Exceeds(MarginThresholds.LiquidationLtvBps))
        {
            return FacilityStatus.LiquidationRequired;
        }

        if (currentStatus == FacilityStatus.MarginCall && !currentLtv.Exceeds(MarginThresholds.CureLtvBps))
        {
            return FacilityStatus.Healthy;
        }

        var breachWindow = orderedSnapshots.TakeLast(MarginThresholds.ConsecutiveBreachMinutes).ToArray();
        if (currentStatus == FacilityStatus.Healthy
            && breachWindow.Length == MarginThresholds.ConsecutiveBreachMinutes
            && AreConsecutiveMinutes(breachWindow)
            && breachWindow.All(snapshot => snapshot.Ltv.Exceeds(MarginThresholds.MarginCallLtvBps)))
        {
            return FacilityStatus.MarginCall;
        }

        return currentStatus;
    }

    private static bool AreConsecutiveMinutes(IReadOnlyList<MinuteSnapshot> snapshots)
    {
        for (var index = 1; index < snapshots.Count; index++)
        {
            if (snapshots[index].Minute.StartUtc - snapshots[index - 1].Minute.StartUtc != TimeSpan.FromMinutes(1))
            {
                return false;
            }
        }

        return true;
    }
}