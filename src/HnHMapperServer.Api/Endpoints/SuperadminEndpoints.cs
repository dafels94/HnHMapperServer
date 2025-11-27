using HnHMapperServer.Core.DTOs;
using HnHMapperServer.Core.Enums;
using HnHMapperServer.Core.Extensions;
using HnHMapperServer.Core.Interfaces;
using HnHMapperServer.Infrastructure.Data;
using HnHMapperServer.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace HnHMapperServer.Api.Endpoints;

/// <summary>
/// Superadmin endpoints for managing all tenants globally.
/// Requires Superadmin role.
/// </summary>
public static class SuperadminEndpoints
{
    public static void MapSuperadminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/superadmin")
            .RequireAuthorization("SuperadminOnly");

        // GET /api/superadmin/tenants - List all tenants
        group.MapGet("/tenants", GetAllTenants);

        // POST /api/superadmin/tenants - Create new tenant
        group.MapPost("/tenants", CreateTenant);

        // GET /api/superadmin/tenants/{tenantId} - View tenant details
        group.MapGet("/tenants/{tenantId}", GetTenantDetails);

        // PUT /api/superadmin/tenants/{tenantId} - Update tenant (name, quota, status)
        group.MapPut("/tenants/{tenantId}", UpdateTenant);

        // PUT /api/superadmin/tenants/{tenantId}/quota - Update storage quota
        group.MapPut("/tenants/{tenantId}/quota", UpdateStorageQuota);

        // PATCH /api/superadmin/tenants/{tenantId}/status - Suspend/activate tenant
        group.MapPatch("/tenants/{tenantId}/status", UpdateTenantStatus);

        // POST /api/superadmin/tenants/{tenantId}/suspend - Suspend tenant
        group.MapPost("/tenants/{tenantId}/suspend", SuspendTenant);

        // POST /api/superadmin/tenants/{tenantId}/activate - Activate tenant
        group.MapPost("/tenants/{tenantId}/activate", ActivateTenant);

        // NOTE: Audit logs endpoint is registered in AuditEndpoints.cs
        // GET /api/superadmin/audit-logs is handled by AuditEndpoints.MapAuditEndpoints()

        // GET /api/superadmin/config - Get all system configuration values
        group.MapGet("/config", GetAllConfig);

        // PUT /api/superadmin/config/{key} - Update a single configuration value
        group.MapPut("/config/{key}", UpdateConfig);

        // PUT /api/superadmin/tenants/{tenantId}/users/{userId}/permissions - Update user permissions
        group.MapPut("/tenants/{tenantId}/users/{userId}/permissions", UpdateUserPermissions);

        // PUT /api/superadmin/tenants/{tenantId}/users/{userId}/role - Change user role
        group.MapPut("/tenants/{tenantId}/users/{userId}/role", UpdateUserRole);

        // POST /api/superadmin/tenants/{tenantId}/users/{userId}/password - Reset user password
        group.MapPost("/tenants/{tenantId}/users/{userId}/password", ResetUserPassword);

        // DELETE /api/superadmin/tenants/{tenantId}/users/{userId} - Remove user from tenant
        group.MapDelete("/tenants/{tenantId}/users/{userId}", RemoveUserFromTenant);

        // GET /api/superadmin/users/unassigned - List users not assigned to any tenant
        group.MapGet("/users/unassigned", GetUnassignedUsers);

        // POST /api/superadmin/users/{userId}/assign-tenant - Assign user to tenant
        group.MapPost("/users/{userId}/assign-tenant", AssignUserToTenant);

        // === Map & Marker Monitoring ===

        // GET /api/superadmin/maps - List all maps across all tenants
        group.MapGet("/maps", GetAllMaps);

        // GET /api/superadmin/tenants/{tenantId}/maps - List maps for specific tenant
        group.MapGet("/tenants/{tenantId}/maps", GetTenantMaps);

        // GET /api/superadmin/markers - List all markers across tenants (paginated)
        group.MapGet("/markers", GetAllMarkers);

        // GET /api/superadmin/tenants/{tenantId}/markers - List markers for specific tenant
        group.MapGet("/tenants/{tenantId}/markers", GetTenantMarkers);

        // GET /api/superadmin/custom-markers - List all custom markers across tenants (paginated)
        group.MapGet("/custom-markers", GetAllCustomMarkers);

        // GET /api/superadmin/tenants/{tenantId}/custom-markers - Custom markers for specific tenant
        group.MapGet("/tenants/{tenantId}/custom-markers", GetTenantCustomMarkers);

        // === Food Database Management ===

        // POST /api/superadmin/foods/import - Import foods from JSON file
        group.MapPost("/foods/import", ImportFoods);

        // GET /api/superadmin/tenants/{tenantId}/statistics - Enhanced tenant statistics
        group.MapGet("/tenants/{tenantId}/statistics", GetTenantStatistics);

        // DELETE /api/superadmin/tenants/{tenantId}/maps/{mapId} - Delete map for tenant
        group.MapDelete("/tenants/{tenantId}/maps/{mapId}", DeleteTenantMap);

        // === Cross-Tenant Map Viewing ===

        // GET /api/superadmin/tenants/{tenantId}/map-view-data - Get all map view data for tenant
        group.MapGet("/tenants/{tenantId}/map-view-data", GetTenantMapViewData);

        // GET /api/superadmin/tenants/{tenantId}/custom-markers-data - Get custom markers for map viewing
        group.MapGet("/tenants/{tenantId}/custom-markers-data", GetTenantCustomMarkersData);
    }

    /// <summary>
    /// GET /api/superadmin/tenants
    /// Lists all tenants with statistics (user count, storage usage, etc.)
    /// </summary>
    private static async Task<IResult> GetAllTenants(
        ApplicationDbContext db)
    {
        var tenants = await db.Tenants
            .IgnoreQueryFilters() // Superadmin sees all tenants
            .ToListAsync();

        var tenantDtos = new List<TenantListDto>();

        foreach (var tenant in tenants)
        {
            var userCount = await db.TenantUsers
                .IgnoreQueryFilters()
                .Where(tu => tu.TenantId == tenant.Id)
                .CountAsync();

            var tokenCount = await db.Tokens
                .IgnoreQueryFilters()
                .Where(t => t.TenantId == tenant.Id)
                .CountAsync();

            tenantDtos.Add(new TenantListDto
            {
                Id = tenant.Id,
                Name = tenant.Name,
                StorageQuotaMB = tenant.StorageQuotaMB,
                CurrentStorageMB = tenant.CurrentStorageMB,
                CreatedAt = tenant.CreatedAt,
                IsActive = tenant.IsActive,
                UserCount = userCount,
                TokenCount = tokenCount
            });
        }

        return Results.Ok(tenantDtos);
    }

    /// <summary>
    /// POST /api/superadmin/tenants
    /// Creates a new tenant with auto-generated ID
    /// </summary>
    private static async Task<IResult> CreateTenant(
        CreateTenantDto dto,
        ITenantService tenantService,
        IAuditService auditService,
        HttpContext context,
        ILogger<Program> logger)
    {
        try
        {
            var newTenant = await tenantService.CreateTenantAsync(dto);

            // Log audit entry
            var username = context.User.Identity?.Name ?? "Unknown";
            await auditService.LogAsync(new AuditEntry
            {
                TenantId = newTenant.Id,
                UserId = username,
                Action = "TenantCreated",
                EntityType = "Tenant",
                EntityId = newTenant.Id,
                NewValue = $"Created tenant {newTenant.Id} (Name: {newTenant.Name}) with quota {dto.StorageQuotaMB}MB"
            });

            logger.LogInformation("SuperAdmin {Username} created tenant {TenantId}", username, newTenant.Id);
            return Results.Created($"/api/superadmin/tenants/{newTenant.Id}", newTenant);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating tenant");
            return Results.Problem("Failed to create tenant");
        }
    }

    /// <summary>
    /// GET /api/superadmin/tenants/{tenantId}
    /// Gets detailed information about a specific tenant including users
    /// </summary>
    private static async Task<IResult> GetTenantDetails(
        string tenantId,
        ApplicationDbContext db,
        UserManager<IdentityUser> userManager)
    {
        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant == null)
        {
            return Results.NotFound(new { error = "Tenant not found" });
        }

        var userCount = await db.TenantUsers
            .IgnoreQueryFilters()
            .Where(tu => tu.TenantId == tenantId)
            .CountAsync();

        var tokenCount = await db.Tokens
            .IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId)
            .CountAsync();

        // Get tenant users with permissions (use AsNoTracking to ensure fresh data)
        var tenantUsers = await db.TenantUsers
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(tu => tu.TenantId == tenantId)
            .Include(tu => tu.Permissions)
            .ToListAsync();

        var userDtos = new List<TenantUserDto>();

        foreach (var tenantUser in tenantUsers)
        {
            var identityUser = await userManager.FindByIdAsync(tenantUser.UserId);
            if (identityUser == null) continue;

            userDtos.Add(new TenantUserDto
            {
                Id = tenantUser.Id,
                UserId = tenantUser.UserId,
                Username = identityUser.UserName ?? string.Empty,
                TenantId = tenantUser.TenantId,
                Role = tenantUser.Role.ToClaimValue(),
                Permissions = tenantUser.Permissions.Select(p => p.Permission.ToClaimValue()).ToList(),
                JoinedAt = tenantUser.JoinedAt,
                PendingApproval = tenantUser.PendingApproval
            });
        }

        var details = new TenantDetailsDto
        {
            Id = tenant.Id,
            Name = tenant.Name,
            StorageQuotaMB = tenant.StorageQuotaMB,
            CurrentStorageMB = tenant.CurrentStorageMB,
            CreatedAt = tenant.CreatedAt,
            IsActive = tenant.IsActive,
            UserCount = userCount,
            TokenCount = tokenCount,
            Users = userDtos
        };

        return Results.Ok(details);
    }

    /// <summary>
    /// PUT /api/superadmin/tenants/{tenantId}
    /// Updates tenant information (name, quota, status)
    /// </summary>
    private static async Task<IResult> UpdateTenant(
        string tenantId,
        UpdateTenantDto dto,
        ITenantService tenantService,
        IAuditService auditService,
        HttpContext context,
        ILogger<Program> logger)
    {
        try
        {
            var updatedTenant = await tenantService.UpdateTenantAsync(tenantId, dto);

            // Log audit entry
            var username = context.User.Identity?.Name ?? "Unknown";
            await auditService.LogAsync(new AuditEntry
            {
                TenantId = tenantId,
                UserId = username,
                Action = "TenantUpdated",
                EntityType = "Tenant",
                EntityId = tenantId,
                OldValue = null, // TenantService handles the delta tracking
                NewValue = $"Name: {dto.Name}, Quota: {dto.StorageQuotaMB}MB, Active: {dto.IsActive}"
            });

            return Results.Ok(updatedTenant);
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Failed to update tenant {TenantId}", tenantId);
            return Results.NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating tenant {TenantId}", tenantId);
            return Results.Problem("Failed to update tenant");
        }
    }

    /// <summary>
    /// PUT /api/superadmin/tenants/{tenantId}/quota
    /// Updates the storage quota for a tenant
    /// </summary>
    private static async Task<IResult> UpdateStorageQuota(
        string tenantId,
        UpdateStorageQuotaDto dto,
        ApplicationDbContext db,
        ILogger<Program> logger,
        IAuditService auditService,
        HttpContext context)
    {
        if (dto.StorageQuotaMB < 0)
        {
            return Results.BadRequest(new { error = "Storage quota must be non-negative" });
        }

        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant == null)
        {
            return Results.NotFound(new { error = "Tenant not found" });
        }

        var oldQuota = tenant.StorageQuotaMB;
        tenant.StorageQuotaMB = dto.StorageQuotaMB;
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Superadmin updated storage quota for tenant {TenantId}: {OldQuota}MB -> {NewQuota}MB",
            tenantId, oldQuota, dto.StorageQuotaMB);

        // Audit log
        await auditService.LogAsync(new AuditEntry
        {
            UserId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            TenantId = tenantId,
            Action = "UpdateStorageQuota",
            EntityType = "Tenant",
            EntityId = tenantId,
            OldValue = JsonSerializer.Serialize(new { storageQuotaMB = oldQuota }),
            NewValue = JsonSerializer.Serialize(new { storageQuotaMB = dto.StorageQuotaMB })
        });

        return Results.Ok(new
        {
            message = "Storage quota updated successfully",
            oldQuota = oldQuota,
            newQuota = dto.StorageQuotaMB
        });
    }

    /// <summary>
    /// PATCH /api/superadmin/tenants/{tenantId}/status
    /// Suspends or activates a tenant
    /// Suspended tenants cannot authenticate
    /// </summary>
    private static async Task<IResult> UpdateTenantStatus(
        string tenantId,
        UpdateTenantStatusDto dto,
        ApplicationDbContext db,
        ILogger<Program> logger,
        IAuditService auditService,
        HttpContext context)
    {
        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant == null)
        {
            return Results.NotFound(new { error = "Tenant not found" });
        }

        var oldStatus = tenant.IsActive;
        tenant.IsActive = dto.IsActive;
        await db.SaveChangesAsync();

        var action = dto.IsActive ? "activated" : "suspended";
        logger.LogWarning(
            "Superadmin {Action} tenant {TenantId}",
            action, tenantId);

        // Audit log
        await auditService.LogAsync(new AuditEntry
        {
            UserId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            TenantId = tenantId,
            Action = dto.IsActive ? "ActivateTenant" : "SuspendTenant",
            EntityType = "Tenant",
            EntityId = tenantId,
            OldValue = JsonSerializer.Serialize(new { isActive = oldStatus }),
            NewValue = JsonSerializer.Serialize(new { isActive = dto.IsActive })
        });

        return Results.Ok(new
        {
            message = $"Tenant {action} successfully",
            tenantId = tenantId,
            isActive = dto.IsActive
        });
    }

    /// <summary>
    /// POST /api/superadmin/tenants/{tenantId}/suspend
    /// Suspends a tenant (shorthand for PATCH with IsActive=false)
    /// </summary>
    private static async Task<IResult> SuspendTenant(
        string tenantId,
        ApplicationDbContext db,
        ILogger<Program> logger,
        IAuditService auditService,
        HttpContext context)
    {
        return await UpdateTenantStatus(
            tenantId,
            new UpdateTenantStatusDto { IsActive = false },
            db,
            logger,
            auditService,
            context);
    }

    /// <summary>
    /// POST /api/superadmin/tenants/{tenantId}/activate
    /// Activates a tenant (shorthand for PATCH with IsActive=true)
    /// </summary>
    private static async Task<IResult> ActivateTenant(
        string tenantId,
        ApplicationDbContext db,
        ILogger<Program> logger,
        IAuditService auditService,
        HttpContext context)
    {
        return await UpdateTenantStatus(
            tenantId,
            new UpdateTenantStatusDto { IsActive = true },
            db,
            logger,
            auditService,
            context);
    }

    /// <summary>
    /// GET /api/superadmin/config
    /// Gets all global system configuration values
    /// </summary>
    private static async Task<IResult> GetAllConfig(
        IConfigRepository configRepository,
        ILogger<Program> logger)
    {
        try
        {
            // Get global config values (TenantId = "__global__")
            // Currently we only manage these as global: prefix
            // Other settings like title, defaultHide, mainMap are tenant-scoped
            var configValues = new Dictionary<string, string>();

            var prefix = await configRepository.GetGlobalValueAsync("prefix");
            if (!string.IsNullOrEmpty(prefix))
                configValues["prefix"] = prefix;

            // For backwards compatibility, also include other config keys
            // (These will eventually be moved to tenant-specific management)
            var title = await configRepository.GetValueAsync("title");
            if (!string.IsNullOrEmpty(title))
                configValues["title"] = title;

            var defaultHide = await configRepository.GetValueAsync("defaultHide");
            if (!string.IsNullOrEmpty(defaultHide))
                configValues["defaultHide"] = defaultHide;

            logger.LogInformation("SuperAdmin: Loaded {Count} config values", configValues.Count);
            return Results.Ok(configValues);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading config values");
            return Results.Problem("Failed to load config values");
        }
    }

    /// <summary>
    /// PUT /api/superadmin/config/{key}
    /// Updates a single configuration value
    /// </summary>
    private static async Task<IResult> UpdateConfig(
        string key,
        UpdateConfigDto dto,
        IConfigRepository configRepository,
        IAuditService auditService,
        HttpContext context,
        ILogger<Program> logger)
    {
        try
        {
            // Determine if this is a global or tenant-scoped config key
            var isGlobalKey = key == "prefix"; // Only prefix is global for now

            string? oldValue;

            if (isGlobalKey)
            {
                // Get old value and save as global
                oldValue = await configRepository.GetGlobalValueAsync(key);
                await configRepository.SetGlobalValueAsync(key, dto.Value);
            }
            else
            {
                // Get old value and save as tenant-scoped
                oldValue = await configRepository.GetValueAsync(key);
                await configRepository.SetValueAsync(key, dto.Value);
            }

            // Log audit entry
            var username = context.User.Identity?.Name ?? "Unknown";
            await auditService.LogAsync(new AuditEntry
            {
                TenantId = null, // Global config
                UserId = username,
                Action = "ConfigUpdated",
                EntityType = "Config",
                EntityId = key,
                OldValue = oldValue,
                NewValue = dto.Value
            });

            logger.LogInformation("SuperAdmin: Updated {Scope} config {Key} = {Value}",
                isGlobalKey ? "GLOBAL" : "tenant",
                key,
                dto.Value);
            return Results.Ok(new { key, value = dto.Value });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating config {Key}", key);
            return Results.Problem("Failed to update config");
        }
    }

    /// <summary>
    /// PUT /api/superadmin/tenants/{tenantId}/users/{userId}/permissions
    /// Updates permissions for a user within a tenant
    /// </summary>
    private static async Task<IResult> UpdateUserPermissions(
        string tenantId,
        string userId,
        UpdateUserPermissionsDto dto,
        ApplicationDbContext db,
        IAuditService auditService,
        HttpContext context,
        ILogger<Program> logger)
    {
        try
        {
            // Find tenant user
            var tenantUser = await db.TenantUsers
                .IgnoreQueryFilters()
                .Include(tu => tu.Permissions)
                .FirstOrDefaultAsync(tu => tu.TenantId == tenantId && tu.UserId == userId);

            if (tenantUser == null)
            {
                logger.LogWarning("TenantUser not found: TenantId={TenantId}, UserId={UserId}", tenantId, userId);
                return Results.NotFound(new { error = "User not found in this tenant" });
            }

            // Get old permissions for audit log
            var oldPermissions = tenantUser.Permissions.Select(p => p.Permission).ToList();

            // Remove existing permissions
            db.TenantPermissions.RemoveRange(tenantUser.Permissions);

            // Add new permissions
            foreach (var permission in dto.Permissions)
            {
                db.TenantPermissions.Add(new HnHMapperServer.Core.Models.TenantPermissionEntity
                {
                    TenantUserId = tenantUser.Id,
                    Permission = permission.ToPermission()
                });
            }

            await db.SaveChangesAsync();

            // Log audit entry
            var username = context.User.Identity?.Name ?? "Unknown";
            await auditService.LogAsync(new AuditEntry
            {
                TenantId = tenantId,
                UserId = username,
                Action = "SuperAdminUpdatedUserPermissions",
                EntityType = "TenantUser",
                EntityId = userId,
                OldValue = JsonSerializer.Serialize(oldPermissions),
                NewValue = JsonSerializer.Serialize(dto.Permissions)
            });

            logger.LogInformation("SuperAdmin updated permissions for user {UserId} in tenant {TenantId}", userId, tenantId);
            return Results.Ok(new { message = "Permissions updated successfully" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating user permissions");
            return Results.Problem("Failed to update user permissions");
        }
    }

    /// <summary>
    /// PUT /api/superadmin/tenants/{tenantId}/users/{userId}/role
    /// Changes the role of a user within a tenant (TenantAdmin <-> TenantUser)
    /// </summary>
    private static async Task<IResult> UpdateUserRole(
        string tenantId,
        string userId,
        UpdateUserRoleDto dto,
        ApplicationDbContext db,
        IAuditService auditService,
        HttpContext context,
        ILogger<Program> logger)
    {
        try
        {
            // Validate role
            if (dto.Role != TenantRole.TenantAdmin.ToClaimValue() && dto.Role != TenantRole.TenantUser.ToClaimValue())
            {
                return Results.BadRequest(new { error = "Role must be either 'TenantAdmin' or 'TenantUser'" });
            }

            // Find tenant user
            var tenantUser = await db.TenantUsers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(tu => tu.TenantId == tenantId && tu.UserId == userId);

            if (tenantUser == null)
            {
                logger.LogWarning("TenantUser not found: TenantId={TenantId}, UserId={UserId}", tenantId, userId);
                return Results.NotFound(new { error = "User not found in this tenant" });
            }

            var oldRole = tenantUser.Role;

            // Check if role is already the same
            if (oldRole.ToClaimValue() == dto.Role)
            {
                return Results.Ok(new { message = "User already has this role" });
            }

            // Update role
            tenantUser.Role = dto.Role.ToTenantRole();
            await db.SaveChangesAsync();

            // Log audit entry
            var username = context.User.Identity?.Name ?? "Unknown";
            await auditService.LogAsync(new AuditEntry
            {
                TenantId = tenantId,
                UserId = username,
                Action = "SuperAdminChangedUserRole",
                EntityType = "TenantUser",
                EntityId = userId,
                OldValue = oldRole.ToClaimValue(),
                NewValue = dto.Role
            });

            logger.LogInformation("SuperAdmin changed role for user {UserId} in tenant {TenantId}: {OldRole} -> {NewRole}",
                userId, tenantId, oldRole, dto.Role);
            return Results.Ok(new { message = $"User role changed to {dto.Role}" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating user role");
            return Results.Problem("Failed to update user role");
        }
    }

    /// <summary>
    /// POST /api/superadmin/tenants/{tenantId}/users/{userId}/password
    /// Resets a user's password (SuperAdmin privilege)
    /// </summary>
    private static async Task<IResult> ResetUserPassword(
        string tenantId,
        string userId,
        ResetPasswordDto dto,
        UserManager<IdentityUser> userManager,
        IAuditService auditService,
        HttpContext context,
        ILogger<Program> logger)
    {
        try
        {
            // Validate password
            if (string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 6)
            {
                return Results.BadRequest(new { error = "Password must be at least 6 characters long" });
            }

            // Find user
            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
            {
                logger.LogWarning("User not found: UserId={UserId}", userId);
                return Results.NotFound(new { error = "User not found" });
            }

            // Remove old password and set new one
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var result = await userManager.ResetPasswordAsync(user, token, dto.Password);

            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                logger.LogWarning("Failed to reset password for user {UserId}: {Errors}", userId, errors);
                return Results.BadRequest(new { error = "Failed to reset password", details = errors });
            }

            // Log audit entry
            var username = context.User.Identity?.Name ?? "Unknown";
            await auditService.LogAsync(new AuditEntry
            {
                TenantId = tenantId,
                UserId = username,
                Action = "SuperAdminResetPassword",
                EntityType = "User",
                EntityId = userId,
                OldValue = null,
                NewValue = $"Password reset for user {user.UserName}"
            });

            logger.LogInformation("SuperAdmin reset password for user {UserId} ({Username}) in tenant {TenantId}",
                userId, user.UserName, tenantId);
            return Results.Ok(new { message = "Password reset successfully" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error resetting user password");
            return Results.Problem("Failed to reset user password");
        }
    }

    /// <summary>
    /// DELETE /api/superadmin/tenants/{tenantId}/users/{userId}
    /// Removes a user from a tenant (revokes all access and permissions)
    /// </summary>
    private static async Task<IResult> RemoveUserFromTenant(
        string tenantId,
        string userId,
        ApplicationDbContext db,
        UserManager<IdentityUser> userManager,
        IAuditService auditService,
        HttpContext context,
        ILogger<Program> logger)
    {
        try
        {
            // Find tenant user
            var tenantUser = await db.TenantUsers
                .IgnoreQueryFilters()
                .Include(tu => tu.Permissions)
                .FirstOrDefaultAsync(tu => tu.TenantId == tenantId && tu.UserId == userId);

            if (tenantUser == null)
            {
                logger.LogWarning("TenantUser not found: TenantId={TenantId}, UserId={UserId}", tenantId, userId);
                return Results.NotFound(new { error = "User not found in this tenant" });
            }

            // Get user for audit log
            var user = await userManager.FindByIdAsync(userId);
            var username = user?.UserName ?? userId;

            // Remove permissions
            db.TenantPermissions.RemoveRange(tenantUser.Permissions);

            // Remove tenant user
            db.TenantUsers.Remove(tenantUser);

            // Remove tenant-specific tokens (if they exist)
            var tenantTokens = await db.Tokens
                .IgnoreQueryFilters()
                .Where(t => t.TenantId == tenantId && t.UserId == userId)
                .ToListAsync();

            if (tenantTokens.Any())
            {
                db.Tokens.RemoveRange(tenantTokens);
            }

            await db.SaveChangesAsync();

            // Log audit entry
            var adminUsername = context.User.Identity?.Name ?? "Unknown";
            await auditService.LogAsync(new AuditEntry
            {
                TenantId = tenantId,
                UserId = adminUsername,
                Action = "SuperAdminRemovedUser",
                EntityType = "TenantUser",
                EntityId = userId,
                OldValue = $"User {username} (Role: {tenantUser.Role})",
                NewValue = "User removed from tenant"
            });

            logger.LogInformation("SuperAdmin removed user {UserId} ({Username}) from tenant {TenantId}",
                userId, username, tenantId);
            return Results.Ok(new { message = "User removed from tenant successfully" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error removing user from tenant");
            return Results.Problem("Failed to remove user from tenant");
        }
    }

    /// <summary>
    /// GET /api/superadmin/users/unassigned
    /// Lists all users who are not assigned to any tenant
    /// </summary>
    private static async Task<IResult> GetUnassignedUsers(
        ApplicationDbContext db,
        UserManager<IdentityUser> userManager)
    {
        try
        {
            // Get all users
            var allUsers = await userManager.Users.ToListAsync();

            // Get all user IDs that have tenant assignments
            var assignedUserIds = await db.TenantUsers
                .IgnoreQueryFilters()
                .Select(tu => tu.UserId)
                .Distinct()
                .ToListAsync();

            // Filter to users without any tenant
            var unassignedUsers = allUsers
                .Where(u => !assignedUserIds.Contains(u.Id))
                .Select(u => new
                {
                    userId = u.Id,
                    username = u.UserName,
                    registeredAt = u.LockoutEnd // Using as proxy for registration date if available
                })
                .ToList();

            return Results.Ok(unassignedUsers);
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to get unassigned users: {ex.Message}");
        }
    }

    // ===========================
    // Map & Marker Monitoring Endpoints
    // ===========================

    /// <summary>
    /// GET /api/superadmin/maps
    /// Lists all maps across all tenants with statistics
    /// </summary>
    private static async Task<IResult> GetAllMaps(
        ApplicationDbContext db,
        ILogger<Program> logger)
    {
        try
        {
            var maps = await db.Maps
                .IgnoreQueryFilters()
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();

            // Load all tenants
            var tenants = await db.Tenants
                .IgnoreQueryFilters()
                .ToDictionaryAsync(t => t.Id, t => t.Name);

            var mapDtos = new List<GlobalMapDto>();

            foreach (var map in maps)
            {
                var tileCount = await db.Tiles
                    .IgnoreQueryFilters()
                    .CountAsync(t => t.MapId == map.Id);

                var markerCount = await db.Markers
                    .IgnoreQueryFilters()
                    .CountAsync(m => db.Grids
                        .IgnoreQueryFilters()
                        .Any(g => g.Id == m.GridId && g.Map == map.Id));

                var customMarkerCount = await db.CustomMarkers
                    .IgnoreQueryFilters()
                    .CountAsync(cm => cm.MapId == map.Id);

                mapDtos.Add(new GlobalMapDto
                {
                    Id = map.Id,
                    Name = map.Name,
                    TenantId = map.TenantId,
                    TenantName = tenants.GetValueOrDefault(map.TenantId, "Unknown"),
                    Hidden = map.Hidden,
                    Priority = map.Priority,
                    CreatedAt = map.CreatedAt,
                    TileCount = tileCount,
                    MarkerCount = markerCount,
                    CustomMarkerCount = customMarkerCount
                });
            }

            logger.LogInformation("SuperAdmin: Loaded {Count} maps across all tenants", mapDtos.Count);
            return Results.Ok(mapDtos);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading all maps");
            return Results.Problem("Failed to load maps");
        }
    }

    /// <summary>
    /// GET /api/superadmin/tenants/{tenantId}/maps
    /// Lists all maps for a specific tenant
    /// </summary>
    private static async Task<IResult> GetTenantMaps(
        string tenantId,
        ApplicationDbContext db,
        ILogger<Program> logger)
    {
        try
        {
            var tenant = await db.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            if (tenant == null)
                return Results.NotFound(new { error = "Tenant not found" });

            var maps = await db.Maps
                .IgnoreQueryFilters()
                .Where(m => m.TenantId == tenantId)
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();

            var mapDtos = new List<GlobalMapDto>();

            foreach (var map in maps)
            {
                var tileCount = await db.Tiles
                    .IgnoreQueryFilters()
                    .CountAsync(t => t.MapId == map.Id);

                var markerCount = await db.Markers
                    .IgnoreQueryFilters()
                    .CountAsync(m => db.Grids
                        .IgnoreQueryFilters()
                        .Any(g => g.Id == m.GridId && g.Map == map.Id));

                var customMarkerCount = await db.CustomMarkers
                    .IgnoreQueryFilters()
                    .CountAsync(cm => cm.MapId == map.Id);

                mapDtos.Add(new GlobalMapDto
                {
                    Id = map.Id,
                    Name = map.Name,
                    TenantId = map.TenantId,
                    TenantName = tenant.Name,
                    Hidden = map.Hidden,
                    Priority = map.Priority,
                    CreatedAt = map.CreatedAt,
                    TileCount = tileCount,
                    MarkerCount = markerCount,
                    CustomMarkerCount = customMarkerCount
                });
            }

            logger.LogInformation("SuperAdmin: Loaded {Count} maps for tenant {TenantId}", mapDtos.Count, tenantId);
            return Results.Ok(mapDtos);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading maps for tenant {TenantId}", tenantId);
            return Results.Problem("Failed to load tenant maps");
        }
    }

    /// <summary>
    /// GET /api/superadmin/markers?page=1&pageSize=100&tenantId=...
    /// Lists all game markers across all tenants (paginated)
    /// </summary>
    private static async Task<IResult> GetAllMarkers(
        ApplicationDbContext db,
        ILogger<Program> logger,
        int page = 1,
        int pageSize = 100,
        string? tenantId = null)
    {
        try
        {
            if (pageSize > 500) pageSize = 500; // Max page size
            if (page < 1) page = 1;

            var query = db.Markers
                .IgnoreQueryFilters()
                .AsQueryable();

            if (!string.IsNullOrEmpty(tenantId))
                query = query.Where(m => m.TenantId == tenantId);

            var totalCount = await query.CountAsync();
            var markers = await query
                .OrderByDescending(m => m.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var markerDtos = new List<GlobalMarkerDto>();

            foreach (var marker in markers)
            {
                var tenant = await db.Tenants
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(t => t.Id == marker.TenantId);

                // Get grid to find map
                var grid = await db.Grids
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(g => g.Id == marker.GridId && g.TenantId == marker.TenantId);

                var map = grid != null ? await db.Maps
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(m => m.Id == grid.Map)
                    : null;

                markerDtos.Add(new GlobalMarkerDto
                {
                    Id = marker.Id,
                    Name = marker.Name,
                    Key = marker.Key,
                    TenantId = marker.TenantId,
                    TenantName = tenant?.Name ?? "Unknown",
                    GridId = marker.GridId,
                    MapId = grid?.Map ?? 0,
                    MapName = map?.Name ?? "Unknown",
                    PositionX = marker.PositionX,
                    PositionY = marker.PositionY,
                    Image = marker.Image,
                    Hidden = marker.Hidden,
                    Ready = marker.Ready,
                    MaxReady = marker.MaxReady,
                    MinReady = marker.MinReady
                });
            }

            logger.LogInformation("SuperAdmin: Loaded {Count}/{Total} markers (page {Page})",
                markerDtos.Count, totalCount, page);

            return Results.Ok(new
            {
                page,
                pageSize,
                totalCount,
                totalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                markers = markerDtos
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading markers");
            return Results.Problem("Failed to load markers");
        }
    }

    /// <summary>
    /// GET /api/superadmin/tenants/{tenantId}/markers?page=1&pageSize=100
    /// Lists all game markers for a specific tenant (paginated)
    /// </summary>
    private static async Task<IResult> GetTenantMarkers(
        string tenantId,
        ApplicationDbContext db,
        ILogger<Program> logger,
        int page = 1,
        int pageSize = 100)
    {
        return await GetAllMarkers(db, logger, page, pageSize, tenantId);
    }

    /// <summary>
    /// GET /api/superadmin/custom-markers?page=1&pageSize=100&tenantId=...
    /// Lists all custom markers across all tenants (paginated)
    /// </summary>
    private static async Task<IResult> GetAllCustomMarkers(
        ApplicationDbContext db,
        ILogger<Program> logger,
        int page = 1,
        int pageSize = 100,
        string? tenantId = null)
    {
        try
        {
            if (pageSize > 500) pageSize = 500; // Max page size
            if (page < 1) page = 1;

            var query = db.CustomMarkers
                .IgnoreQueryFilters()
                .AsQueryable();

            if (!string.IsNullOrEmpty(tenantId))
                query = query.Where(cm => cm.TenantId == tenantId);

            var totalCount = await query.CountAsync();
            var customMarkers = await query
                .OrderByDescending(cm => cm.PlacedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Load all referenced tenants and maps
            var tenantIds = customMarkers.Select(cm => cm.TenantId).Distinct().ToList();
            var mapIds = customMarkers.Select(cm => cm.MapId).Distinct().ToList();

            var tenants = await db.Tenants
                .IgnoreQueryFilters()
                .Where(t => tenantIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name);

            var maps = await db.Maps
                .IgnoreQueryFilters()
                .Where(m => mapIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.Name);

            var customMarkerDtos = new List<GlobalCustomMarkerDto>();

            foreach (var cm in customMarkers)
            {
                customMarkerDtos.Add(new GlobalCustomMarkerDto
                {
                    Id = cm.Id,
                    Title = cm.Title,
                    Description = cm.Description,
                    TenantId = cm.TenantId,
                    TenantName = tenants.GetValueOrDefault(cm.TenantId, "Unknown"),
                    MapId = cm.MapId,
                    MapName = maps.GetValueOrDefault(cm.MapId, "Unknown"),
                    GridId = cm.GridId,
                    CoordX = cm.CoordX,
                    CoordY = cm.CoordY,
                    X = cm.X,
                    Y = cm.Y,
                    Icon = cm.Icon,
                    CreatedBy = cm.CreatedBy,
                    PlacedAt = cm.PlacedAt,
                    UpdatedAt = cm.UpdatedAt,
                    Hidden = cm.Hidden
                });
            }

            logger.LogInformation("SuperAdmin: Loaded {Count}/{Total} custom markers (page {Page})",
                customMarkerDtos.Count, totalCount, page);

            return Results.Ok(new
            {
                page,
                pageSize,
                totalCount,
                totalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                customMarkers = customMarkerDtos
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading custom markers");
            return Results.Problem("Failed to load custom markers");
        }
    }

    /// <summary>
    /// GET /api/superadmin/tenants/{tenantId}/custom-markers?page=1&pageSize=100
    /// Lists all custom markers for a specific tenant (paginated)
    /// </summary>
    private static async Task<IResult> GetTenantCustomMarkers(
        string tenantId,
        ApplicationDbContext db,
        ILogger<Program> logger,
        int page = 1,
        int pageSize = 100)
    {
        return await GetAllCustomMarkers(db, logger, page, pageSize, tenantId);
    }

    /// <summary>
    /// GET /api/superadmin/tenants/{tenantId}/statistics
    /// Gets comprehensive statistics for a tenant (maps, markers, grids, tiles, storage)
    /// </summary>
    private static async Task<IResult> GetTenantStatistics(
        string tenantId,
        ApplicationDbContext db,
        ILogger<Program> logger)
    {
        try
        {
            var tenant = await db.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            if (tenant == null)
                return Results.NotFound(new { error = "Tenant not found" });

            var mapCount = await db.Maps
                .IgnoreQueryFilters()
                .CountAsync(m => m.TenantId == tenantId);

            var gridCount = await db.Grids
                .IgnoreQueryFilters()
                .CountAsync(g => g.TenantId == tenantId);

            var tileCount = await db.Tiles
                .IgnoreQueryFilters()
                .CountAsync(t => t.TenantId == tenantId);

            var markerCount = await db.Markers
                .IgnoreQueryFilters()
                .CountAsync(m => m.TenantId == tenantId);

            var customMarkerCount = await db.CustomMarkers
                .IgnoreQueryFilters()
                .CountAsync(cm => cm.TenantId == tenantId);

            var userCount = await db.TenantUsers
                .IgnoreQueryFilters()
                .CountAsync(tu => tu.TenantId == tenantId);

            var tokenCount = await db.Tokens
                .IgnoreQueryFilters()
                .CountAsync(t => t.TenantId == tenantId);

            var stats = new TenantStatisticsDto
            {
                TenantId = tenant.Id,
                TenantName = tenant.Name,
                MapCount = mapCount,
                GridCount = gridCount,
                TileCount = tileCount,
                MarkerCount = markerCount,
                CustomMarkerCount = customMarkerCount,
                UserCount = userCount,
                TokenCount = tokenCount,
                StorageUsageMB = tenant.CurrentStorageMB,
                StorageQuotaMB = tenant.StorageQuotaMB
            };

            logger.LogInformation("SuperAdmin: Loaded statistics for tenant {TenantId}", tenantId);
            return Results.Ok(stats);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading tenant statistics for {TenantId}", tenantId);
            return Results.Problem("Failed to load tenant statistics");
        }
    }

    /// <summary>
    /// DELETE /api/superadmin/tenants/{tenantId}/maps/{mapId}
    /// Deletes a map and all associated data (tiles, markers, custom markers) for a tenant
    /// </summary>
    private static async Task<IResult> DeleteTenantMap(
        string tenantId,
        int mapId,
        ApplicationDbContext db,
        IAuditService auditService,
        HttpContext context,
        ILogger<Program> logger)
    {
        try
        {
            var map = await db.Maps
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.Id == mapId && m.TenantId == tenantId);

            if (map == null)
                return Results.NotFound(new { error = "Map not found" });

            // Count related entities before deletion
            var tileCount = await db.Tiles
                .IgnoreQueryFilters()
                .CountAsync(t => t.MapId == mapId && t.TenantId == tenantId);

            var customMarkerCount = await db.CustomMarkers
                .IgnoreQueryFilters()
                .CountAsync(cm => cm.MapId == mapId && cm.TenantId == tenantId);

            // Delete map (cascade will handle tiles, markers, custom markers)
            db.Maps.Remove(map);
            await db.SaveChangesAsync();

            // Log audit entry
            var username = context.User.Identity?.Name ?? "Unknown";
            await auditService.LogAsync(new AuditEntry
            {
                TenantId = tenantId,
                UserId = username,
                Action = "SuperAdminDeletedMap",
                EntityType = "Map",
                EntityId = mapId.ToString(),
                OldValue = $"Map '{map.Name}' with {tileCount} tiles and {customMarkerCount} custom markers",
                NewValue = "Map deleted"
            });

            logger.LogWarning("SuperAdmin {Username} deleted map {MapId} ({MapName}) for tenant {TenantId}",
                username, mapId, map.Name, tenantId);

            return Results.Ok(new
            {
                message = "Map deleted successfully",
                mapId,
                mapName = map.Name,
                tilesDeleted = tileCount,
                customMarkersDeleted = customMarkerCount
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting map {MapId} for tenant {TenantId}", mapId, tenantId);
            return Results.Problem("Failed to delete map");
        }
    }

    /// <summary>
    /// POST /api/superadmin/users/{userId}/assign-tenant
    /// Assigns an unassigned user to a tenant with specified role and permissions
    /// </summary>
    private static async Task<IResult> AssignUserToTenant(
        string userId,
        AssignUserToTenantDto dto,
        ApplicationDbContext db,
        UserManager<IdentityUser> userManager,
        ILogger<Program> logger,
        ClaimsPrincipal adminUser)
    {
        try
        {
            var adminUsername = adminUser.Identity?.Name ?? "unknown";

            // Validate user exists
            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
                return Results.NotFound(new { error = "User not found" });

            // Validate tenant exists
            var tenant = await db.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == dto.TenantId);

            if (tenant == null)
                return Results.NotFound(new { error = "Tenant not found" });

            // Check if user is already in a tenant
            var existingAssignment = await db.TenantUsers
                .IgnoreQueryFilters()
                .AnyAsync(tu => tu.UserId == userId);

            if (existingAssignment)
                return Results.BadRequest(new { error = "User is already assigned to a tenant" });

            // Validate role
            if (dto.Role != TenantRole.TenantAdmin.ToClaimValue() && dto.Role != TenantRole.TenantUser.ToClaimValue())
                return Results.BadRequest(new { error = $"Role must be '{TenantRole.TenantAdmin.ToClaimValue()}' or '{TenantRole.TenantUser.ToClaimValue()}'" });

            // Validate permissions
            var validPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Permission.Map.ToClaimValue(),
                Permission.Markers.ToClaimValue(),
                Permission.Pointer.ToClaimValue(),
                Permission.Upload.ToClaimValue(),
                Permission.Writer.ToClaimValue()
            };

            foreach (var permission in dto.Permissions)
            {
                if (!validPermissions.Contains(permission))
                    return Results.BadRequest(new { error = $"Invalid permission: {permission}" });
            }

            // Create TenantUser with immediate approval (JoinedAt = now)
            var tenantUser = new HnHMapperServer.Core.Models.TenantUserEntity
            {
                TenantId = dto.TenantId,
                UserId = userId,
                Role = dto.Role.ToTenantRole(),
                JoinedAt = DateTime.UtcNow // Immediate approval for SuperAdmin assignment
            };

            db.TenantUsers.Add(tenantUser);
            await db.SaveChangesAsync();

            // Add permissions
            foreach (var permission in dto.Permissions)
            {
                db.TenantPermissions.Add(new HnHMapperServer.Core.Models.TenantPermissionEntity
                {
                    TenantUserId = tenantUser.Id,
                    Permission = permission.ToPermission()
                });
            }

            await db.SaveChangesAsync();

            // Log audit
            db.AuditLogs.Add(new HnHMapperServer.Core.Models.AuditLogEntity
            {
                Timestamp = DateTime.UtcNow.ToString("O"),
                UserId = adminUsername,
                TenantId = dto.TenantId,
                Action = "SuperAdminAssignedUser",
                EntityType = "TenantUser",
                EntityId = userId,
                NewValue = $"User {user.UserName} assigned to tenant {tenant.Name} as {dto.Role} with permissions: {string.Join(", ", dto.Permissions)}"
            });
            await db.SaveChangesAsync();

            logger.LogInformation("SuperAdmin {AdminUsername} assigned user {UserId} ({Username}) to tenant {TenantId} as {Role}",
                adminUsername, userId, user.UserName, dto.TenantId, dto.Role);

            return Results.Ok(new
            {
                message = "User assigned to tenant successfully",
                tenantId = dto.TenantId,
                tenantName = tenant.Name,
                role = dto.Role,
                permissions = dto.Permissions
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error assigning user to tenant");
            return Results.Problem("Failed to assign user to tenant");
        }
    }

    // ===========================
    // Cross-Tenant Map Viewing Endpoints
    // ===========================

    /// <summary>
    /// GET /api/superadmin/tenants/{tenantId}/map-view-data
    /// Returns all data needed for map viewing: markers, maps, and config
    /// </summary>
    private static async Task<IResult> GetTenantMapViewData(
        string tenantId,
        ApplicationDbContext db,
        HnHMapperServer.Api.Services.MapRevisionCache revisionCache,
        ILogger<Program> logger)
    {
        try
        {
            // Verify tenant exists
            var tenant = await db.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            if (tenant == null)
                return Results.NotFound(new { error = "Tenant not found" });

            // Get markers (bypassing tenant filter)
            var markers = await db.Markers
                .IgnoreQueryFilters()
                .Where(m => m.TenantId == tenantId)
                .ToListAsync();

            var markerModels = markers.Select(m => {
                // Get grid to find map
                var grid = db.Grids
                    .IgnoreQueryFilters()
                    .FirstOrDefault(g => g.Id == m.GridId && g.TenantId == tenantId);

                return new
                {
                    Id = m.Id,
                    Name = m.Name,
                    Map = grid?.Map ?? 0,
                    Position = new { X = m.PositionX, Y = m.PositionY },
                    Image = m.Image,
                    Hidden = m.Hidden,
                    MinReady = m.MinReady,
                    MaxReady = m.MaxReady,
                    Ready = m.Ready
                };
            }).ToList();

            // Get maps
            var config = await db.Config
                .IgnoreQueryFilters()
                .Where(c => c.TenantId == tenantId)
                .ToListAsync();

            var mainMapIdStr = config.FirstOrDefault(c => c.Key == "mainMapId")?.Value;
            int? mainMapId = null;
            if (!string.IsNullOrEmpty(mainMapIdStr) && int.TryParse(mainMapIdStr, out var parsedMainMapId))
                mainMapId = parsedMainMapId;

            var maps = await db.Maps
                .IgnoreQueryFilters()
                .Where(m => m.TenantId == tenantId && !m.Hidden)
                .OrderByDescending(m => m.Priority)
                .ThenBy(m => m.Name)
                .ToListAsync();

            var mapModels = maps.Select(m => new
            {
                ID = m.Id,
                MapInfo = new
                {
                    Name = m.Name,
                    Hidden = m.Hidden,
                    Priority = m.Priority,
                    Revision = revisionCache.Get(m.Id),
                    IsMainMap = mainMapId.HasValue && mainMapId.Value == m.Id
                },
                Size = 0
            }).ToList();

            // Get config
            var title = config.FirstOrDefault(c => c.Key == "title")?.Value ?? "HnH Mapper";

            // Superadmin gets all permissions for viewing
            var permissions = new[] { "Map", "Markers", "Pointer", "Upload", "Writer" };

            var response = new
            {
                Markers = markerModels,
                Maps = mapModels,
                Config = new
                {
                    Title = title,
                    Permissions = permissions,
                    MainMapId = mainMapId
                }
            };

            logger.LogInformation("SuperAdmin: Loaded map view data for tenant {TenantId} ({MapCount} maps, {MarkerCount} markers)",
                tenantId, mapModels.Count, markerModels.Count);

            return Results.Json(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading map view data for tenant {TenantId}", tenantId);
            return Results.Problem("Failed to load map view data");
        }
    }

    /// <summary>
    /// GET /api/superadmin/tenants/{tenantId}/custom-markers-data?mapId={mapId}
    /// Returns custom markers for a tenant, optionally filtered by map ID
    /// </summary>
    private static async Task<IResult> GetTenantCustomMarkersData(
        string tenantId,
        ApplicationDbContext db,
        ILogger<Program> logger,
        int? mapId = null)
    {
        try
        {
            // Verify tenant exists
            var tenant = await db.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            if (tenant == null)
                return Results.NotFound(new { error = "Tenant not found" });

            var query = db.CustomMarkers
                .IgnoreQueryFilters()
                .Where(cm => cm.TenantId == tenantId);

            if (mapId.HasValue)
                query = query.Where(cm => cm.MapId == mapId.Value);

            var customMarkers = await query.ToListAsync();

            var response = customMarkers.Select(cm => new
            {
                Id = cm.Id,
                MapId = cm.MapId,
                GridId = cm.GridId,
                CoordX = cm.CoordX,
                CoordY = cm.CoordY,
                X = cm.X,
                Y = cm.Y,
                Title = cm.Title,
                Description = cm.Description,
                Icon = cm.Icon,
                CreatedBy = cm.CreatedBy,
                PlacedAt = cm.PlacedAt,
                UpdatedAt = cm.UpdatedAt,
                Hidden = cm.Hidden
            }).ToList();

            logger.LogInformation("SuperAdmin: Loaded {Count} custom markers for tenant {TenantId} (mapId: {MapId})",
                response.Count, tenantId, mapId);

            return Results.Json(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading custom markers for tenant {TenantId}", tenantId);
            return Results.Problem("Failed to load custom markers");
        }
    }

    // ============================
    // Food Database Management
    // ============================

    private static async Task<IResult> ImportFoods(
        HttpContext context,
        IFoodImportService foodImportService,
        ILogger<Program> logger)
    {
        try
        {
            // Check if request has a file upload
            if (context.Request.HasFormContentType && context.Request.Form.Files.Count > 0)
            {
                var file = context.Request.Form.Files[0];
                using var stream = file.OpenReadStream();
                using var reader = new StreamReader(stream);
                var jsonContent = await reader.ReadToEndAsync();

                logger.LogInformation("Importing foods from uploaded file: {FileName} ({Length} bytes)",
                    file.FileName, file.Length);

                var result = await foodImportService.ImportFromJsonContentAsync(jsonContent);
                return Results.Ok(result);
            }

            // Otherwise, read from default path
            var defaultPath = Path.Combine("C:", "Users", "AAAAAAAAAA", "Documents", "haven", "map", "tools", "food-info2.json");
            if (!File.Exists(defaultPath))
            {
                return Results.BadRequest(new
                {
                    error = "No file uploaded and default file not found",
                    defaultPath
                });
            }

            logger.LogInformation("Importing foods from default path: {Path}", defaultPath);
            var importResult = await foodImportService.ImportFromJsonFileAsync(defaultPath);
            return Results.Ok(importResult);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error importing foods");
            return Results.Problem(
                title: "Import failed",
                detail: ex.Message,
                statusCode: 500
            );
        }
    }
}

/// <summary>
/// DTO for updating a configuration value
/// </summary>
public record UpdateConfigDto(string Value);

/// <summary>
/// DTO for updating user permissions
/// </summary>
public record UpdateUserPermissionsDto(List<string> Permissions);

/// <summary>
/// DTO for updating user role
/// </summary>
public record UpdateUserRoleDto(string Role);

/// <summary>
/// DTO for resetting user password
/// </summary>
public record ResetPasswordDto(string Password);

/// <summary>
/// DTO for assigning a user to a tenant
/// </summary>
public record AssignUserToTenantDto(
    string TenantId,
    string Role,
    List<string> Permissions
);
