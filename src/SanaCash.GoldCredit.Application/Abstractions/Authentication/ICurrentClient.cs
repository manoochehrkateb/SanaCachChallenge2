using SanaCash.GoldCredit.Domain.Shared;

namespace SanaCash.GoldCredit.Application.Abstractions.Authentication;

public interface ICurrentClient
{
    ClientId ClientId { get; }
    bool IsRiskOfficer { get; }
}