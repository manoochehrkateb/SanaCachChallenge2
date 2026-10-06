using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SanaCash.GoldCredit.Application.Abstractions.Authentication;
using SanaCash.GoldCredit.Domain.Shared;

namespace SanaCash.GoldCredit.Infrastructure.Authentication;

public class HttpCurrentClient(IHttpContextAccessor httpContextAccessor) : ICurrentClient
{
    private ClaimsPrincipal User => httpContextAccessor.HttpContext?.User
        ?? throw new InvalidOperationException("No authenticated HTTP client is available.");

    public ClientId ClientId
    {
        get
        {
            var claim = User.FindFirst("client_id")?.Value;
            if (!Guid.TryParse(claim, out var value))
            {
                throw new InvalidOperationException("Authenticated token has no valid client_id claim.");
            }

            var clientId = Domain.Shared.ClientId.Create(value);
            return clientId.IsSuccess
                ? clientId.Value
                : throw new InvalidOperationException("Authenticated token has no valid client_id claim.");
        }
    }

    public bool IsRiskOfficer => User.IsInRole("risk-officer");
}
