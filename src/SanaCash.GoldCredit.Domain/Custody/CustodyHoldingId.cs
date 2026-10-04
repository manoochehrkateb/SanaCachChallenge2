using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Domain.Custody;

public sealed record CustodyHoldingId : ValueObject
{
    private CustodyHoldingId(ClientId clientId, Instrument instrument)
    {
        ClientId = clientId;
        Instrument = instrument;
    }

    public ClientId ClientId { get; }
    public Instrument Instrument { get; }

    public static CustodyHoldingId Create(ClientId clientId, Instrument instrument) => new(clientId, instrument);
}