using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Credit;

public sealed record MarginEvidence : ValueObject
{
    private MarginEvidence(IReadOnlyList<MinuteSnapshot> snapshots) => Snapshots = snapshots;

    public IReadOnlyList<MinuteSnapshot> Snapshots { get; }

    public static Result<MarginEvidence> Create(IEnumerable<MinuteSnapshot> snapshots)
    {
        var items = snapshots.OrderBy(snapshot => snapshot.Minute.StartUtc).ToArray();
        return items.Length is < 1 or > 3
            ? Result<MarginEvidence>.Failure(new Error("InvalidMarginEvidence", "Margin evidence must contain between one and three minute snapshots."))
            : Result<MarginEvidence>.Success(new MarginEvidence(items));
    }
}