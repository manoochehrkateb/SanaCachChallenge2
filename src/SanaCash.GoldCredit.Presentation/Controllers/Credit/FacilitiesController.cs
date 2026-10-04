using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SanaCash.GoldCredit.Application.Credit.ExecuteDrawdown;
using SanaCash.GoldCredit.Application.Credit.GetFacility;
using SanaCash.GoldCredit.Application.Credit.GetMarginEvents;
using SanaCash.GoldCredit.Application.Credit.PledgeCollateral;
using SanaCash.GoldCredit.Application.Credit.ReleaseCollateral;
using SanaCash.GoldCredit.Application.Credit.RepayDebt;
using SanaCash.GoldCredit.Domain.Shared.Primitives;
using SanaCash.GoldCredit.Presentation.Contracts.Credit;

namespace SanaCash.GoldCredit.Presentation.Controllers.Credit;

[ApiController]
[Authorize]
[Route("api/facilities/{facilityId:guid}")]
public sealed class FacilitiesController(
    PledgeCollateralHandler pledgeHandler,
    ReleaseCollateralHandler releaseHandler,
    ExecuteDrawdownHandler drawdownHandler,
    RepayDebtHandler repaymentHandler,
    GetFacilityHandler getFacilityHandler,
    GetMarginEventsHandler getMarginEventsHandler) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetFacility(Guid facilityId, CancellationToken cancellationToken)
    {
        var details = await getFacilityHandler.HandleAsync(facilityId, cancellationToken);
        return details is null
            ? NotFound()
            : Ok(new FacilityResponse(details.FacilityId, details.CollateralFineMg, details.DebtIrr,
                details.Status, details.LtvBps, details.IsLtvInfinite, details.PriceTimestampUtc));
    }

    [HttpGet("margin-events")]
    public async Task<IActionResult> GetMarginEvents(Guid facilityId, CancellationToken cancellationToken)
    {
        var result = await getMarginEventsHandler.HandleAsync(facilityId, cancellationToken);
        if (result.IsFailure)
        {
            return ToResponse(result);
        }

        var events = result.Value.Select(item => new MarginEventResponse(
            item.EventId,
            item.EventType,
            item.EpisodeNumber,
            item.OccurredAtUtc,
            System.Text.Json.JsonDocument.Parse(item.EvidenceJson).RootElement.Clone()));
        return Ok(events);
    }

    [HttpPost("pledges")]
    public async Task<IActionResult> Pledge(
        Guid facilityId,
        [FromBody] PledgeRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken) =>
        ToResponse(await pledgeHandler.HandleAsync(
            new PledgeCollateralCommand(facilityId, request.FineWeightMg, idempotencyKey ?? string.Empty),
            cancellationToken));

    [HttpPost("releases")]
    public async Task<IActionResult> Release(
        Guid facilityId,
        [FromBody] ReleaseRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken) =>
        ToResponse(await releaseHandler.HandleAsync(
            new ReleaseCollateralCommand(facilityId, request.FineWeightMg, idempotencyKey ?? string.Empty),
            cancellationToken));

    [HttpPost("drawdowns")]
    public async Task<IActionResult> Drawdown(
        Guid facilityId,
        [FromBody] DrawdownRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken) =>
        ToResponse(await drawdownHandler.HandleAsync(
            new ExecuteDrawdownCommand(facilityId, request.AmountIrr, idempotencyKey ?? string.Empty),
            cancellationToken));

    [HttpPost("repayments")]
    public async Task<IActionResult> Repay(
        Guid facilityId,
        [FromBody] RepaymentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken) =>
        ToResponse(await repaymentHandler.HandleAsync(
            new RepayDebtCommand(facilityId, request.AmountIrr, idempotencyKey ?? string.Empty),
            cancellationToken));

    private IActionResult ToResponse<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        var status = result.Error.Code switch
        {
            "FacilityNotFound" => StatusCodes.Status404NotFound,
            "IdempotencyKeyMissing" or "InvalidIrr" or "InvalidFineWeight" => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status409Conflict
        };
        var problem = new ProblemDetails
        {
            Status = status,
            Title = result.Error.Code,
            Detail = result.Error.Description
        };
        problem.Extensions["code"] = result.Error.Code;
        return StatusCode(status, problem);
    }
}