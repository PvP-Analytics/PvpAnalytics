using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PvpAnalytics.Application.Services;
using PvpAnalytics.Core.DTOs;
using PvpAnalytics.Shared.Constants;

namespace PvpAnalytics.Api.Controllers;

[ApiController]
[Route(AppConstants.RouteConstants.DiscussionsBase)]
public class DiscussionsController(IDiscussionService service) : ControllerBase
{
    private Guid? GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("userId")?.Value;

        return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
    }

    [AllowAnonymous]
    [HttpGet("match/{matchId:long}")]
    public async Task<ActionResult> GetThreadsForMatch(long matchId, CancellationToken ct = default)
    {
        var threads = await service.GetThreadsForMatchAsync(matchId, ct);
        return Ok(threads);
    }

    [AllowAnonymous]
    [HttpGet("thread/{threadId:long}")]
    public async Task<ActionResult> GetThread(long threadId, CancellationToken ct = default)
    {
        var thread = await service.GetThreadAsync(threadId, ct);
        return thread == null ? NotFound() : Ok(thread);
    }

    [Authorize]
    [HttpPost("thread")]
    public async Task<ActionResult> CreateThread([FromBody] CreateThreadDto dto, CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized();

        var thread = await service.CreateThreadAsync(dto, userId.Value, ct);
        return CreatedAtAction(nameof(GetThread), new { threadId = thread.Id }, thread);
    }

    [Authorize]
    [HttpPost("post")]
    public async Task<ActionResult> CreatePost([FromBody] CreatePostDto dto, CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized();

        var post = await service.CreatePostAsync(dto, userId.Value, ct);
        return CreatedAtAction(nameof(GetThread), new { threadId = post.ThreadId }, post);
    }
}

