using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PvpAnalytics.Application.Services;
using PvpAnalytics.Core.DTOs;
using PvpAnalytics.Shared.Constants;

namespace PvpAnalytics.Api.Controllers;

[ApiController]
[Route(AppConstants.RouteConstants.MatchesBase)]
public class MatchDetailsController(IMatchDetailService detailService) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("{id:long}/detail")]
    public async Task<ActionResult<MatchDetailDto>> GetDetail(long id, CancellationToken ct)
    {
        var detail = await detailService.GetMatchDetailAsync(id, ct);
        return detail is null ? NotFound() : Ok(detail);
    }
}