using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaymentService.Application.DTOs;
using PaymentService.Application.Models;
using PaymentService.Application.Services;
using PaymentService.Core.Entities;
using PaymentService.Core.Enum;
using PaymentService.Core.Repositories;
using PvpAnalytics.Shared.Constants;

namespace PaymentService.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class PaymentController(ICrudService<Payment> service, IRepository<Payment> repository) : ControllerBase
{
    private const string AdminRole = "Admin";
    private const int MaxPageSize = 100;
    private const int DefaultPageSize = 20;

    private string? GetCurrentUserId()
    {
        // Try NameIdentifier first (standard claim type)
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        // Fallback to "sub" claim (JWT standard, used by AuthService)
        if (string.IsNullOrWhiteSpace(userId))
        {
            userId = User.FindFirst("sub")?.Value;
        }

        return userId;
    }

    private bool IsAdmin()
    {
        return User.IsInRole(AdminRole);
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<Payment>>> GetAll(
        [FromQuery] GetPaymentRequest paymentRequest,
        CancellationToken ct = default)
    {
        var currentUserId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return Unauthorized(AppConstants.ErrorMessages.UserIdClaimNotFound);
        }

        var (validatedPage, validatedPageSize) =
            ValidatePaginationParameters(paymentRequest.Page, paymentRequest.PageSize);
        var query = ApplyFilters(repository.GetQueryable(), currentUserId, paymentRequest.UserId, paymentRequest.Status,
            paymentRequest.StartDate, paymentRequest.EndDate);
        query = ApplySorting(query, paymentRequest.SortBy, paymentRequest.SortOrder);

        var (items, total) = await repository.GetPagedAsync(query, validatedPage, validatedPageSize, ct);

        var response = new PaginatedResponse<Payment>
        {
            Items = items,
            Total = total,
            Page = validatedPage,
            PageSize = validatedPageSize
        };

        return Ok(response);
    }

    private static (int page, int pageSize) ValidatePaginationParameters(int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = DefaultPageSize;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;
        return (page, pageSize);
    }

    private IQueryable<Payment> ApplyFilters(
        IQueryable<Payment> query,
        string currentUserId,
        string? userId,
        PaymentStatus? status,
        DateTime? startDate,
        DateTime? endDate)
    {
        query = ApplyUserScopeFilter(query, currentUserId, userId);
        query = ApplyStatusFilter(query, status);
        query = ApplyDateRangeFilters(query, startDate, endDate);
        return query;
    }

    private IQueryable<Payment> ApplyUserScopeFilter(IQueryable<Payment> query, string currentUserId, string? userId)
    {
        if (!IsAdmin())
        {
            return query.Where(p => p.UserId == currentUserId);
        }

        if (!string.IsNullOrWhiteSpace(userId))
        {
            return query.Where(p => p.UserId == userId);
        }

        return query;
    }

    private static IQueryable<Payment> ApplyStatusFilter(IQueryable<Payment> query, PaymentStatus? status)
    {
        if (status.HasValue)
        {
            query = query.Where(p => p.Status == status.Value);
        }

        return query;
    }

    private static IQueryable<Payment> ApplyDateRangeFilters(
        IQueryable<Payment> query,
        DateTime? startDate,
        DateTime? endDate)
    {
        if (startDate.HasValue)
        {
            query = query.Where(p => p.CreatedAt >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(p => p.CreatedAt <= endDate.Value);
        }

        return query;
    }

    private static IQueryable<Payment> ApplySorting(IQueryable<Payment> query, string sortBy, string sortOrder)
    {
        var isAscending = string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase);
        var sortField = sortBy.ToLower();

        return sortField switch
        {
            "id" => isAscending ? query.OrderBy(p => p.Id) : query.OrderByDescending(p => p.Id),
            "amount" => isAscending ? query.OrderBy(p => p.Amount) : query.OrderByDescending(p => p.Amount),
            "status" => isAscending ? query.OrderBy(p => p.Status) : query.OrderByDescending(p => p.Status),
            "userid" => isAscending ? query.OrderBy(p => p.UserId) : query.OrderByDescending(p => p.UserId),
            "createdat" => isAscending ? query.OrderBy(p => p.CreatedAt) : query.OrderByDescending(p => p.CreatedAt),
            "updatedat" => isAscending ? query.OrderBy(p => p.UpdatedAt) : query.OrderByDescending(p => p.UpdatedAt),
            _ => query.OrderByDescending(p => p.CreatedAt)
        };
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<Payment>> Get(long id, CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return Unauthorized(AppConstants.ErrorMessages.UserIdClaimNotFound);
        }

        var entity = await service.GetAsync([id], ct);
        if (entity is null)
        {
            return NotFound();
        }

        // Admins can access any payment, regular users can only access their own
        if (!IsAdmin() && entity.UserId != currentUserId)
        {
            // Rely on default authentication/authorization scheme; do not pass a custom scheme string.
            return Forbid();
        }

        return Ok(entity);
    }

    [HttpPost]
    public async Task<ActionResult<Payment>> Create([FromBody] CreatePaymentRequest request, CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return Unauthorized(AppConstants.ErrorMessages.UserIdClaimNotFound);
        }

        // Create payment entity from DTO
        var payment = new Payment
        {
            Amount = request.Amount,
            TransactionId = request.TransactionId,
            PaymentMethod = request.PaymentMethod,
            Description = request.Description,
            Status = PaymentStatus.Pending, // Default to Pending
            UserId = currentUserId, // Set from JWT token
            CreatedAt = DateTime.UtcNow // Set server timestamp
        };

        var created = await service.CreateAsync(payment, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdatePaymentRequest request, CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return Unauthorized(AppConstants.ErrorMessages.UserIdClaimNotFound);
        }

        var existing = await service.GetAsync([id], ct);
        if (existing is null)
        {
            return NotFound();
        }

        // Admins can update any payment, regular users can only update their own
        if (!IsAdmin() && existing.UserId != currentUserId)
        {
            return Forbid();
        }

        // Update only writable fields (selective update to prevent modifying immutable fields)
        existing.Amount = request.Amount;
        existing.Status = request.Status;
        existing.Description = request.Description;
        existing.UpdatedAt = DateTime.UtcNow; // Set server timestamp

        await service.UpdateAsync(existing, ct);
        return NoContent();
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return Unauthorized(AppConstants.ErrorMessages.UserIdClaimNotFound);
        }

        var existing = await service.GetAsync([id], ct);
        if (existing is null)
        {
            return NotFound();
        }

        // Admins can delete any payment, regular users can only delete their own
        if (!IsAdmin() && existing.UserId != currentUserId)
        {
            return Forbid();
        }

        await service.DeleteAsync(existing, ct);
        return NoContent();
    }
}