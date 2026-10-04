using Microsoft.AspNetCore.Mvc;
using SanaCash.GoldCredit.Application.Abstractions.Health;

namespace SanaCash.GoldCredit.Presentation.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController(IPlatformHealthProbe healthProbe) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await healthProbe.CheckAsync(cancellationToken);
        return StatusCode(result.IsHealthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable, result);
    }
}