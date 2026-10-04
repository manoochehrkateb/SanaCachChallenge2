using SanaCash.GoldCredit.Domain.Credit.Enums;
using SanaCash.GoldCredit.Domain.Shared;

namespace SanaCash.GoldCredit.Domain.Credit;

public sealed record FacilityMinuteInput(
    FacilityId FacilityId,
    FacilityStatus Status,
    long Version,
    IReadOnlyList<MinuteSnapshot> RecentSnapshots);