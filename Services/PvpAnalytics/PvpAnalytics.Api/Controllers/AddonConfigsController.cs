using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PvpAnalytics.Application.Services;
using PvpAnalytics.Core.Entities;

namespace PvpAnalytics.Api.Controllers;

[ApiController]
[Route("api/addon-configs")]
public class AddonConfigsController(IAddonConfigService service) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("by-spec/{spec}")]
    public async Task<ActionResult> GetBySpec(string spec, CancellationToken ct)
    {
        var configs = await service.GetBySpecAsync(spec, ct);
        return Ok(configs);
    }

    [AllowAnonymous]
    [HttpGet("by-composition/{composition}")]
    public async Task<ActionResult> GetByComposition(string composition, CancellationToken ct)
    {
        var configs = await service.GetByCompositionAsync(composition, ct);
        return Ok(configs);
    }

    [AllowAnonymous]
    [HttpGet("by-type/{addonType}")]
    public async Task<ActionResult> GetByType(string addonType, CancellationToken ct)
    {
        var configs = await service.GetByTypeAsync(addonType, ct);
        return Ok(configs);
    }

    [AllowAnonymous]
    [HttpGet("{id:long}")]
    public async Task<ActionResult> GetById(long id, CancellationToken ct)
    {
        var config = await service.GetByIdAsync(id, ct);
        if (config == null) return NotFound();
        return Ok(config);
    }

    [Authorize]
    [HttpPost]
    public async Task<ActionResult> Create([FromBody] AddonConfig config, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();
        config.CreatedByUserId = userId;
        var created = await service.CreateAsync(config, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [Authorize]
    [HttpPut("{id:long}")]
    public async Task<ActionResult> Update(long id, [FromBody] AddonConfig config, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();
        var updated = await service.UpdateAsync(id, config, userId, ct);
        if (!updated) return NotFound();
        return NoContent();
    }

    [Authorize]
    [HttpDelete("{id:long}")]
    public async Task<ActionResult> Delete(long id, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();
        var deleted = await service.DeleteAsync(id, userId, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }

    private bool TryGetUserId(out Guid userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(value) || !Guid.TryParse(value, out userId))
        {
            userId = default;
            return false;
        }
        return true;
    }
}
