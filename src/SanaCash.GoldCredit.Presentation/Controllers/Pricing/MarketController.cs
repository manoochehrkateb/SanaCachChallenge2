using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SanaCash.GoldCredit.Application.Pricing.GetCandles;
using SanaCash.GoldCredit.Presentation.Contracts.Pricing;

namespace SanaCash.GoldCredit.Presentation.Controllers.Pricing;

[ApiController]
[Authorize]
[Route("api/market/instruments/{instrument}/candles")]
public sealed class MarketController(GetCandlesHandler getCandlesHandler) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetCandles(
        string instrument,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var result = await getCandlesHandler.HandleAsync(instrument, from, to, cancellationToken);
        if (result.IsFailure)
        {
            var status = result.Error.Code == "InvalidCandleRange"
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status404NotFound;
            var problem = new ProblemDetails
            {
                Status = status,
                Title = result.Error.Code,
                Detail = result.Error.Description
            };
            problem.Extensions["code"] = result.Error.Code;
            return StatusCode(status, problem);
        }

        return Ok(result.Value.Select(candle => new CandleResponse(
            candle.MinuteStartUtc, candle.OpenIrr, candle.HighIrr, candle.LowIrr,
            candle.CloseIrr, candle.TickCount, candle.CarriedForward)));
    }
}