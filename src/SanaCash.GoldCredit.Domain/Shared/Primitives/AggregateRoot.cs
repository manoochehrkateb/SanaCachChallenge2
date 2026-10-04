namespace SanaCash.GoldCredit.Domain.Shared.Primitives;

public abstract class AggregateRoot<TId> : Entity<TId> where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id) : base(id)
    {
    }

    public long Version { get; private set; }
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    protected void MarkChanged() => Version = checked(Version + 1);

    protected void RestoreVersion(long version)
    {
        if (version < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        Version = version;
    }

    public IReadOnlyList<IDomainEvent> PullDomainEvents()
    {
        var events = _domainEvents.ToArray();
        _domainEvents.Clear();
        return events;
    }
}