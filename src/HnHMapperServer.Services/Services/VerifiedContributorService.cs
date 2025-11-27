using HnHMapperServer.Core.DTOs;
using HnHMapperServer.Core.Interfaces;
using HnHMapperServer.Infrastructure.Data;
using HnHMapperServer.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HnHMapperServer.Services.Services;

/// <summary>
/// Service for managing verified contributors (users trusted to auto-publish recipes)
/// </summary>
public class VerifiedContributorService : IVerifiedContributorService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly UserManager<IdentityUser> _userManager;

    public VerifiedContributorService(
        ApplicationDbContext context,
        ITenantContextAccessor tenantContext,
        UserManager<IdentityUser> userManager)
    {
        _context = context;
        _tenantContext = tenantContext;
        _userManager = userManager;
    }

    public async Task<List<VerifiedContributorDto>> GetAllVerifiedContributorsAsync(CancellationToken cancellationToken = default)
    {
        var contributors = await _context.VerifiedContributors
            .ToListAsync(cancellationToken);

        var dtos = new List<VerifiedContributorDto>();

        foreach (var contributor in contributors)
        {
            var user = await _userManager.FindByIdAsync(contributor.UserId);
            dtos.Add(new VerifiedContributorDto
            {
                Id = contributor.Id,
                TenantId = contributor.TenantId,
                UserId = contributor.UserId,
                UserName = user?.UserName ?? "Unknown",
                VerifiedAt = contributor.VerifiedAt,
                VerifiedBy = contributor.VerifiedBy,
                Notes = contributor.Notes
            });
        }

        return dtos;
    }

    public async Task<bool> IsVerifiedContributorAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _context.VerifiedContributors
            .AnyAsync(v => v.UserId == userId, cancellationToken);
    }

    public async Task<VerifiedContributorDto> AddVerifiedContributorAsync(string userId, string verifiedBy, string? notes, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetCurrentTenantId() ?? throw new InvalidOperationException("Tenant context is required");

        // Check if already verified
        var existing = await _context.VerifiedContributors
            .FirstOrDefaultAsync(v => v.TenantId == tenantId && v.UserId == userId, cancellationToken);

        if (existing != null)
        {
            throw new InvalidOperationException("User is already a verified contributor");
        }

        var contributor = new VerifiedContributorEntity
        {
            TenantId = tenantId,
            UserId = userId,
            VerifiedAt = DateTime.UtcNow,
            VerifiedBy = verifiedBy,
            Notes = notes
        };

        _context.VerifiedContributors.Add(contributor);
        await _context.SaveChangesAsync(cancellationToken);

        var user = await _userManager.FindByIdAsync(userId);

        return new VerifiedContributorDto
        {
            Id = contributor.Id,
            TenantId = contributor.TenantId,
            UserId = contributor.UserId,
            UserName = user?.UserName ?? "Unknown",
            VerifiedAt = contributor.VerifiedAt,
            VerifiedBy = contributor.VerifiedBy,
            Notes = contributor.Notes
        };
    }

    public async Task<bool> RemoveVerifiedContributorAsync(string userId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetCurrentTenantId() ?? throw new InvalidOperationException("Tenant context is required");

        var contributor = await _context.VerifiedContributors
            .FirstOrDefaultAsync(v => v.TenantId == tenantId && v.UserId == userId, cancellationToken);

        if (contributor == null)
        {
            return false;
        }

        _context.VerifiedContributors.Remove(contributor);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
