using HnHMapperServer.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HnHMapperServer.Infrastructure.Data;

public sealed class ApplicationDbContext : IdentityDbContext<IdentityUser, IdentityRole, string>
{
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        IHttpContextAccessor httpContextAccessor)
        : base(options)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Gets the current tenant ID from HttpContext.Items
    /// </summary>
    private string? GetCurrentTenantId()
    {
        return _httpContextAccessor?.HttpContext?.Items["TenantId"] as string;
    }

    /// <summary>
    /// Enables bypassing of tenant filters for superadmin operations
    /// </summary>
    public ApplicationDbContext BypassTenantFilter()
    {
        ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        return this;
    }

    public DbSet<GridDataEntity> Grids => Set<GridDataEntity>();
    public DbSet<MarkerEntity> Markers => Set<MarkerEntity>();
    public DbSet<TileDataEntity> Tiles => Set<TileDataEntity>();
    public DbSet<MapInfoEntity> Maps => Set<MapInfoEntity>();
    public DbSet<ConfigEntity> Config => Set<ConfigEntity>();
    public DbSet<TokenEntity> Tokens => Set<TokenEntity>();
    public DbSet<CustomMarkerEntity> CustomMarkers => Set<CustomMarkerEntity>();
    public DbSet<PingEntity> Pings => Set<PingEntity>();

    // Multi-tenancy tables
    public DbSet<TenantEntity> Tenants => Set<TenantEntity>();
    public DbSet<TenantUserEntity> TenantUsers => Set<TenantUserEntity>();
    public DbSet<TenantPermissionEntity> TenantPermissions => Set<TenantPermissionEntity>();
    public DbSet<TenantInvitationEntity> TenantInvitations => Set<TenantInvitationEntity>();
    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();

    // Notification and timer tables
    public DbSet<NotificationEntity> Notifications => Set<NotificationEntity>();
    public DbSet<TimerEntity> Timers => Set<TimerEntity>();
    public DbSet<TimerWarningEntity> TimerWarnings => Set<TimerWarningEntity>();
    public DbSet<NotificationPreferenceEntity> NotificationPreferences => Set<NotificationPreferenceEntity>();
    public DbSet<TimerHistoryEntity> TimerHistory => Set<TimerHistoryEntity>();

    // Cookbook tables
    public DbSet<FoodEntity> Foods => Set<FoodEntity>();
    public DbSet<FoodIngredientEntity> FoodIngredients => Set<FoodIngredientEntity>();
    public DbSet<FoodFepEntity> FoodFeps => Set<FoodFepEntity>();
    public DbSet<FoodSubmissionEntity> FoodSubmissions => Set<FoodSubmissionEntity>();
    public DbSet<VerifiedContributorEntity> VerifiedContributors => Set<VerifiedContributorEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<GridDataEntity>(entity =>
        {
            // COMPOSITE PRIMARY KEY: Allow same Grid ID across different tenants
            entity.HasKey(e => new { e.Id, e.TenantId });

            entity.Property(e => e.CoordX).IsRequired();
            entity.Property(e => e.CoordY).IsRequired();
            entity.Property(e => e.Map).IsRequired();
            entity.Property(e => e.TenantId).IsRequired();

            entity.HasIndex(e => new { e.Map, e.CoordX, e.CoordY });
            entity.HasIndex(e => e.TenantId);

            // Foreign key to Tenants
            entity.HasOne<TenantEntity>()
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MarkerEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Key).IsRequired();
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.GridId).IsRequired();
            entity.Property(e => e.TenantId).IsRequired();

            // COMPOSITE UNIQUE INDEX: Allow same Marker Key across different tenants
            entity.HasIndex(e => new { e.Key, e.TenantId }).IsUnique();
            entity.HasIndex(e => e.GridId);
            entity.HasIndex(e => e.TenantId);

            // Foreign key to Tenants
            entity.HasOne<TenantEntity>()
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TileDataEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MapId).IsRequired();
            entity.Property(e => e.CoordX).IsRequired();
            entity.Property(e => e.CoordY).IsRequired();
            entity.Property(e => e.Zoom).IsRequired();
            entity.Property(e => e.TenantId).IsRequired();
            entity.Property(e => e.FileSizeBytes).IsRequired().HasDefaultValue(0);
            entity.Property(e => e.OriginalTenantId).IsRequired(false);

            entity.HasIndex(e => new { e.MapId, e.Zoom, e.CoordX, e.CoordY }).IsUnique();
            entity.HasIndex(e => e.TenantId);

            // Foreign key to Tenants
            entity.HasOne<TenantEntity>()
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MapInfoEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.TenantId).IsRequired();

            // Set default value for CreatedAt to CURRENT_TIMESTAMP in SQLite
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .IsRequired();

            // Index for efficient cleanup queries
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.TenantId);

            // Foreign key to Tenants
            entity.HasOne<TenantEntity>()
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ConfigEntity>(entity =>
        {
            // COMPOSITE PRIMARY KEY: Allow same Config Key across different tenants
            entity.HasKey(e => new { e.Key, e.TenantId });
            entity.Property(e => e.Value).IsRequired();
            entity.Property(e => e.TenantId).IsRequired();

            // Foreign key to Tenants
            entity.HasOne<TenantEntity>()
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TokenEntity>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .IsRequired();

            entity.Property(e => e.DisplayToken)
                .IsRequired();

            entity.Property(e => e.TokenHash)
                .IsRequired();

            entity.Property(e => e.TenantId)
                .IsRequired();

            entity.Property(e => e.UserId)
                .IsRequired();

            entity.Property(e => e.Name)
                .HasMaxLength(128)
                .IsRequired();

            entity.Property(e => e.Scopes)
                .HasMaxLength(1024)
                .IsRequired(false);

            entity.Property(e => e.CreatedAt)
                .IsRequired();

            entity.Property(e => e.LastUsedAt)
                .IsRequired(false);

            entity.Property(e => e.ExpiresAt)
                .IsRequired(false);

            entity.HasIndex(e => e.DisplayToken)
                .IsUnique();

            entity.HasIndex(e => e.TokenHash)
                .IsUnique();

            entity.HasIndex(e => e.TenantId);

            entity.HasIndex(e => new { e.UserId, e.Name })
                .IsUnique();

            entity.HasOne<TenantEntity>()
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<IdentityUser>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomMarkerEntity>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Title)
                .HasMaxLength(80)
                .IsRequired();

            entity.Property(e => e.Description)
                .HasMaxLength(1000)
                .IsRequired(false);

            entity.Property(e => e.Icon)
                .HasMaxLength(256)
                .IsRequired();

            entity.Property(e => e.CreatedBy)
                .IsRequired();

            entity.Property(e => e.PlacedAt)
                .IsRequired();

            entity.Property(e => e.UpdatedAt)
                .IsRequired();

            entity.Property(e => e.TenantId)
                .IsRequired();

            entity.HasIndex(e => e.MapId);
            entity.HasIndex(e => e.GridId);
            entity.HasIndex(e => e.CreatedBy);
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.MapId, e.CoordX, e.CoordY, e.X, e.Y }).IsUnique();
            entity.HasIndex(e => new { e.MapId, e.PlacedAt })
                .HasAnnotation("Sqlite:IndexColumnOrder", new[] { "ASC", "DESC" });

            // Composite foreign key to GridDataEntity (Id, TenantId)
            // Since Grids now has composite PK, we need to reference both columns
            entity.HasOne<GridDataEntity>()
                .WithMany()
                .HasForeignKey(e => new { e.GridId, e.TenantId })
                .OnDelete(DeleteBehavior.Cascade);

            // Foreign key to MapInfoEntity
            entity.HasOne<MapInfoEntity>()
                .WithMany()
                .HasForeignKey(e => e.MapId)
                .OnDelete(DeleteBehavior.Cascade);

            // Foreign key to Tenants
            entity.HasOne<TenantEntity>()
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PingEntity>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.MapId)
                .IsRequired();

            entity.Property(e => e.CoordX)
                .IsRequired();

            entity.Property(e => e.CoordY)
                .IsRequired();

            entity.Property(e => e.X)
                .IsRequired();

            entity.Property(e => e.Y)
                .IsRequired();

            entity.Property(e => e.CreatedBy)
                .IsRequired();

            entity.Property(e => e.CreatedAt)
                .IsRequired();

            entity.Property(e => e.ExpiresAt)
                .IsRequired();

            entity.Property(e => e.TenantId)
                .IsRequired();

            // Indexes for efficient queries
            entity.HasIndex(e => e.MapId);
            entity.HasIndex(e => e.CreatedBy);
            entity.HasIndex(e => e.ExpiresAt);
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.TenantId, e.ExpiresAt });

            // Foreign key to MapInfoEntity
            entity.HasOne<MapInfoEntity>()
                .WithMany()
                .HasForeignKey(e => e.MapId)
                .OnDelete(DeleteBehavior.Cascade);

            // Foreign key to Tenants
            entity.HasOne<TenantEntity>()
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Multi-tenancy entity configurations
        modelBuilder.Entity<TenantEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.StorageQuotaMB).IsRequired().HasDefaultValue(1024);
            entity.Property(e => e.CurrentStorageMB).IsRequired().HasDefaultValue(0.0);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
        });

        modelBuilder.Entity<TenantUserEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).IsRequired();
            entity.Property(e => e.UserId).IsRequired();

            // Map Role enum to RoleString column in database
            entity.Property(e => e.RoleString)
                .HasColumnName("Role")
                .IsRequired();
            entity.Ignore(e => e.Role); // Don't map the enum property directly

            entity.Property(e => e.JoinedAt).IsRequired();

            // Unique constraint on TenantId + UserId
            entity.HasIndex(e => new { e.TenantId, e.UserId }).IsUnique();
            entity.HasIndex(e => e.UserId);

            // Foreign keys
            entity.HasOne<TenantEntity>()
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<IdentityUser>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TenantPermissionEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantUserId).IsRequired();

            // Map Permission enum to PermissionString column in database
            entity.Property(e => e.PermissionString)
                .HasColumnName("Permission")
                .IsRequired();
            entity.Ignore(e => e.Permission); // Don't map the enum property directly

            entity.HasIndex(e => e.TenantUserId);
            entity.HasIndex(e => e.PermissionString);

            // Foreign key - link to TenantUser.Permissions navigation property
            entity.HasOne<TenantUserEntity>()
                .WithMany(tu => tu.Permissions)
                .HasForeignKey(e => e.TenantUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TenantInvitationEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).IsRequired();
            entity.Property(e => e.InviteCode).IsRequired();
            entity.Property(e => e.CreatedBy).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.ExpiresAt).IsRequired();
            entity.Property(e => e.Status).IsRequired().HasDefaultValue("Active");
            entity.Property(e => e.PendingApproval).IsRequired().HasDefaultValue(false);

            entity.HasIndex(e => e.InviteCode).IsUnique();
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.Status, e.ExpiresAt });

            // Foreign key
            entity.HasOne<TenantEntity>()
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditLogEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Timestamp).IsRequired();
            entity.Property(e => e.Action).IsRequired();

            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => new { e.TenantId, e.Timestamp });
            entity.HasIndex(e => new { e.UserId, e.Timestamp });
            entity.HasIndex(e => e.Action);
            entity.HasIndex(e => new { e.EntityType, e.EntityId });
        });

        // Notification and timer entity configurations
        modelBuilder.Entity<NotificationEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).IsRequired();
            entity.Property(e => e.Type).IsRequired();
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Message).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.Priority).IsRequired().HasDefaultValue("Normal");
            entity.Property(e => e.IsRead).IsRequired().HasDefaultValue(false);
            entity.Property(e => e.CreatedAt).IsRequired();

            // Indexes for efficient queries
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Type);
            entity.HasIndex(e => new { e.TenantId, e.UserId, e.IsRead });
            entity.HasIndex(e => new { e.TenantId, e.CreatedAt });
            entity.HasIndex(e => e.ExpiresAt);

            // Foreign key to Tenants
            entity.HasOne<TenantEntity>()
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            // Foreign key to AspNetUsers (nullable)
            entity.HasOne<IdentityUser>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired(false);
        });

        modelBuilder.Entity<TimerEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).IsRequired();
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.Type).IsRequired();
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(1000).IsRequired(false);
            entity.Property(e => e.ReadyAt).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.IsCompleted).IsRequired().HasDefaultValue(false);
            entity.Property(e => e.NotificationSent).IsRequired().HasDefaultValue(false);
            entity.Property(e => e.PreExpiryWarningSent).IsRequired().HasDefaultValue(false);

            // Indexes for efficient queries
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Type);
            entity.HasIndex(e => e.MarkerId);
            entity.HasIndex(e => e.CustomMarkerId);
            entity.HasIndex(e => new { e.TenantId, e.IsCompleted, e.ReadyAt });
            entity.HasIndex(e => new { e.TenantId, e.UserId, e.IsCompleted });

            // Foreign key to Tenants
            entity.HasOne<TenantEntity>()
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            // Foreign key to AspNetUsers
            entity.HasOne<IdentityUser>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Foreign key to MarkerEntity (nullable)
            entity.HasOne<MarkerEntity>()
                .WithMany()
                .HasForeignKey(e => e.MarkerId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            // Foreign key to CustomMarkerEntity (nullable)
            entity.HasOne<CustomMarkerEntity>()
                .WithMany()
                .HasForeignKey(e => e.CustomMarkerId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
        });

        modelBuilder.Entity<TimerWarningEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TimerId).IsRequired();
            entity.Property(e => e.WarningMinutes).IsRequired();
            entity.Property(e => e.SentAt).IsRequired();

            // Unique index to prevent duplicate warnings for same timer+interval
            entity.HasIndex(e => new { e.TimerId, e.WarningMinutes }).IsUnique();

            // Index for efficient queries
            entity.HasIndex(e => e.TimerId);

            // Foreign key to TimerEntity (cascade delete when timer is deleted)
            entity.HasOne<TimerEntity>()
                .WithMany()
                .HasForeignKey(e => e.TimerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NotificationPreferenceEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.NotificationType).IsRequired();
            entity.Property(e => e.Enabled).IsRequired().HasDefaultValue(true);
            entity.Property(e => e.PlaySound).IsRequired().HasDefaultValue(false);
            entity.Property(e => e.ShowBrowserNotification).IsRequired().HasDefaultValue(false);
            entity.Property(e => e.PreExpiryWarningMinutes).IsRequired().HasDefaultValue(5);

            // Unique constraint: one preference per user per notification type
            entity.HasIndex(e => new { e.UserId, e.NotificationType }).IsUnique();

            // Foreign key to AspNetUsers
            entity.HasOne<IdentityUser>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TimerHistoryEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TimerId).IsRequired();
            entity.Property(e => e.TenantId).IsRequired();
            entity.Property(e => e.CompletedAt).IsRequired();
            entity.Property(e => e.Type).IsRequired();
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);

            // Indexes for efficient queries
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.TimerId);
            entity.HasIndex(e => e.MarkerId);
            entity.HasIndex(e => e.CustomMarkerId);
            entity.HasIndex(e => new { e.TenantId, e.CompletedAt });
            entity.HasIndex(e => new { e.TenantId, e.Type, e.CompletedAt });

            // Foreign key to Tenants
            entity.HasOne<TenantEntity>()
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Cookbook entity configurations
        modelBuilder.Entity<FoodEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.ResourceType).HasMaxLength(256).IsRequired(false);
            entity.Property(e => e.Energy).IsRequired();
            entity.Property(e => e.Hunger).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(2000).IsRequired(false);
            entity.Property(e => e.IsSmoked).IsRequired().HasDefaultValue(false);
            entity.Property(e => e.SubmittedBy).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.ApprovedBy).IsRequired(false);
            entity.Property(e => e.ApprovedAt).IsRequired(false);
            entity.Property(e => e.IsVerified).IsRequired().HasDefaultValue(false);
            entity.Property(e => e.TenantId).IsRequired(false); // NULL for global recipes

            // Indexes for efficient queries
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.ResourceType);
            entity.HasIndex(e => e.IsVerified);
            entity.HasIndex(e => new { e.TenantId, e.Name });
            entity.HasIndex(e => e.CreatedAt);

            // Composite index for tenant-scoped queries with sorting (performance optimization)
            entity.HasIndex(e => new { e.TenantId, e.CreatedAt })
                .HasDatabaseName("IX_Foods_TenantId_CreatedAt");

            // Foreign key to Tenants (nullable for global recipes)
            entity.HasOne<TenantEntity>()
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired(false);

            // Foreign key to AspNetUsers (SubmittedBy)
            entity.HasOne<IdentityUser>()
                .WithMany()
                .HasForeignKey(e => e.SubmittedBy)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FoodIngredientEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FoodId).IsRequired();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Quantity).IsRequired().HasPrecision(18, 4);
            entity.Property(e => e.Quality).IsRequired(false);

            // Index for efficient queries
            entity.HasIndex(e => e.FoodId);
            entity.HasIndex(e => e.Name);

            // Foreign key to Foods with cascade delete
            entity.HasOne(i => i.Food)
                .WithMany(f => f.Ingredients)
                .HasForeignKey(i => i.FoodId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FoodFepEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FoodId).IsRequired();
            entity.Property(e => e.AttributeName).IsRequired().HasMaxLength(50);
            entity.Property(e => e.BaseValue).IsRequired().HasPrecision(18, 4);

            // Index for efficient queries
            entity.HasIndex(e => e.FoodId);
            entity.HasIndex(e => e.AttributeName);

            // Composite covering index for stat-based queries (performance optimization)
            // This allows SQLite to efficiently find foods by stat using index-only scans
            entity.HasIndex(e => new { e.AttributeName, e.FoodId, e.BaseValue })
                .HasDatabaseName("IX_FoodFeps_AttributeName_FoodId_BaseValue");

            // Foreign key to Foods with cascade delete
            entity.HasOne(f => f.Food)
                .WithMany(fo => fo.Feps)
                .HasForeignKey(f => f.FoodId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FoodSubmissionEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).IsRequired(false);
            entity.Property(e => e.SubmittedBy).IsRequired();
            entity.Property(e => e.SubmittedAt).IsRequired();
            entity.Property(e => e.Status).IsRequired().HasMaxLength(50).HasDefaultValue("Pending");
            entity.Property(e => e.DataJson).IsRequired();
            entity.Property(e => e.ReviewedBy).IsRequired(false);
            entity.Property(e => e.ReviewedAt).IsRequired(false);
            entity.Property(e => e.ReviewNotes).HasMaxLength(1000).IsRequired(false);
            entity.Property(e => e.ApprovedFoodId).IsRequired(false);

            // Indexes for efficient queries
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.SubmittedBy);
            entity.HasIndex(e => new { e.TenantId, e.Status });
            entity.HasIndex(e => e.SubmittedAt);

            // Foreign key to Tenants (nullable for global submission requests)
            entity.HasOne<TenantEntity>()
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired(false);

            // Foreign key to AspNetUsers (SubmittedBy)
            entity.HasOne<IdentityUser>()
                .WithMany()
                .HasForeignKey(e => e.SubmittedBy)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<VerifiedContributorEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).IsRequired();
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.VerifiedAt).IsRequired();
            entity.Property(e => e.VerifiedBy).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(500).IsRequired(false);

            // Unique constraint: one verification per user per tenant
            entity.HasIndex(e => new { e.TenantId, e.UserId }).IsUnique();

            // Indexes for efficient queries
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.UserId);

            // Foreign key to Tenants
            entity.HasOne<TenantEntity>()
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            // Foreign key to AspNetUsers (UserId)
            entity.HasOne<IdentityUser>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Foreign key to AspNetUsers (VerifiedBy)
            entity.HasOne<IdentityUser>()
                .WithMany()
                .HasForeignKey(e => e.VerifiedBy)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Global query filters for automatic tenant isolation
        // These filters automatically add "WHERE TenantId = {currentTenantId}" to all queries
        // GetCurrentTenantId() is evaluated at query time (not model creation time)
        // Can be bypassed using IgnoreQueryFilters() for superadmin operations
        modelBuilder.Entity<MapInfoEntity>()
            .HasQueryFilter(m => m.TenantId == GetCurrentTenantId());

        modelBuilder.Entity<GridDataEntity>()
            .HasQueryFilter(g => g.TenantId == GetCurrentTenantId());

        modelBuilder.Entity<TileDataEntity>()
            .HasQueryFilter(t => t.TenantId == GetCurrentTenantId());

        modelBuilder.Entity<MarkerEntity>()
            .HasQueryFilter(m => m.TenantId == GetCurrentTenantId());

        modelBuilder.Entity<CustomMarkerEntity>()
            .HasQueryFilter(c => c.TenantId == GetCurrentTenantId());

        modelBuilder.Entity<PingEntity>()
            .HasQueryFilter(p => p.TenantId == GetCurrentTenantId());

        modelBuilder.Entity<TokenEntity>()
            .HasQueryFilter(t => t.TenantId == GetCurrentTenantId());

        modelBuilder.Entity<ConfigEntity>()
            .HasQueryFilter(c => c.TenantId == GetCurrentTenantId());

        modelBuilder.Entity<NotificationEntity>()
            .HasQueryFilter(n => n.TenantId == GetCurrentTenantId());

        modelBuilder.Entity<TimerEntity>()
            .HasQueryFilter(t => t.TenantId == GetCurrentTenantId());

        modelBuilder.Entity<TimerHistoryEntity>()
            .HasQueryFilter(t => t.TenantId == GetCurrentTenantId());

        // Cookbook query filters - hybrid tenancy (global + tenant-specific)
        // Foods: Show global recipes (TenantId IS NULL) OR tenant-specific recipes
        modelBuilder.Entity<FoodEntity>()
            .HasQueryFilter(f => f.TenantId == null || f.TenantId == GetCurrentTenantId());

        // FoodSubmissions: Show only submissions for current tenant or global requests
        modelBuilder.Entity<FoodSubmissionEntity>()
            .HasQueryFilter(s => s.TenantId == null || s.TenantId == GetCurrentTenantId());

        // VerifiedContributors: Show only verified contributors in current tenant
        modelBuilder.Entity<VerifiedContributorEntity>()
            .HasQueryFilter(v => v.TenantId == GetCurrentTenantId());
    }
}

public sealed class GridDataEntity
{
    public string Id { get; set; } = string.Empty;
    public int CoordX { get; set; }
    public int CoordY { get; set; }
    public DateTime NextUpdate { get; set; }
    public int Map { get; set; }
    public string TenantId { get; set; } = string.Empty;
}

public sealed class MarkerEntity
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string GridId { get; set; } = string.Empty;
    public int PositionX { get; set; }
    public int PositionY { get; set; }
    public string Image { get; set; } = string.Empty;
    public bool Hidden { get; set; }
    public long MaxReady { get; set; }
    public long MinReady { get; set; }
    public bool Ready { get; set; }
    public string TenantId { get; set; } = string.Empty;
}

public sealed class TileDataEntity
{
    public int Id { get; set; }
    public int MapId { get; set; }
    public int CoordX { get; set; }
    public int CoordY { get; set; }
    public int Zoom { get; set; }
    public string File { get; set; } = string.Empty;
    public long Cache { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public int FileSizeBytes { get; set; } = 0;
    public string? OriginalTenantId { get; set; }
}

public sealed class MapInfoEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Hidden { get; set; }
    public int Priority { get; set; }

    /// <summary>
    /// UTC timestamp when the map was created (SQLite stores as TEXT in ISO 8601 format)
    /// Used for auto-cleanup of empty maps
    /// </summary>
    public DateTime CreatedAt { get; set; }

    public string TenantId { get; set; } = string.Empty;
}

public sealed class ConfigEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
}

public sealed class TokenEntity
{
    // Primary key (string GUID). Plaintext token is NEVER stored; only its hash.
    public string Id { get; set; } = string.Empty;

    // Full token with tenant prefix (e.g., "warrior-shield-42_abc123...")
    public string DisplayToken { get; set; } = string.Empty;

    // SHA-256 hex of the secret portion only (not including tenant prefix)
    public string TokenHash { get; set; } = string.Empty;

    // Tenant ID extracted from token prefix
    public string TenantId { get; set; } = string.Empty;

    // Owner (ASP.NET Core Identity user id)
    public string UserId { get; set; } = string.Empty;

    // Friendly display name/label (unique per user)
    public string Name { get; set; } = string.Empty;

    // Comma-separated scopes mapped to lowercase `auth` claims (e.g. "upload,map")
    public string? Scopes { get; set; }

    // Audit fields
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// Represents a custom marker created by users on the map
/// </summary>
public sealed class CustomMarkerEntity
{
    /// <summary>
    /// Primary key
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to MapInfoEntity
    /// </summary>
    public int MapId { get; set; }

    /// <summary>
    /// Foreign key to GridDataEntity (string ID)
    /// </summary>
    public string GridId { get; set; } = string.Empty;

    /// <summary>
    /// Grid coordinate X (from Grids table)
    /// </summary>
    public int CoordX { get; set; }

    /// <summary>
    /// Grid coordinate Y (from Grids table)
    /// </summary>
    public int CoordY { get; set; }

    /// <summary>
    /// Position X within the grid (0-100)
    /// </summary>
    public int X { get; set; }

    /// <summary>
    /// Position Y within the grid (0-100)
    /// </summary>
    public int Y { get; set; }

    /// <summary>
    /// Marker title (max 80 characters)
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Optional description (max 1000 characters)
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Icon name/path (must be from allowed icon list)
    /// </summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// Username of the creator (from ASP.NET Identity)
    /// </summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the marker was placed (immutable after creation)
    /// </summary>
    public DateTime PlacedAt { get; set; }

    /// <summary>
    /// UTC timestamp when the marker was last updated
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Whether the marker is hidden
    /// </summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// Tenant ID for multi-tenancy isolation
    /// </summary>
    public string TenantId { get; set; } = string.Empty;
}

/// <summary>
/// Represents a temporary ping marker placed by users on the map
/// Auto-expires after 60 seconds
/// </summary>
public sealed class PingEntity
{
    /// <summary>
    /// Primary key
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to MapInfoEntity
    /// </summary>
    public int MapId { get; set; }

    /// <summary>
    /// Grid coordinate X
    /// </summary>
    public int CoordX { get; set; }

    /// <summary>
    /// Grid coordinate Y
    /// </summary>
    public int CoordY { get; set; }

    /// <summary>
    /// Position X within the grid (0-100)
    /// </summary>
    public int X { get; set; }

    /// <summary>
    /// Position Y within the grid (0-100)
    /// </summary>
    public int Y { get; set; }

    /// <summary>
    /// Username of the creator (from ASP.NET Identity)
    /// </summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the ping was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// UTC timestamp when the ping expires (CreatedAt + 60 seconds)
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Tenant ID for multi-tenancy isolation
    /// </summary>
    public string TenantId { get; set; } = string.Empty;
}

/// <summary>
/// Represents a notification for a user in the system
/// Notifications are shown in the notification center and can have actions
/// </summary>
public sealed class NotificationEntity
{
    /// <summary>
    /// Primary key
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Tenant ID for multi-tenancy isolation
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// User ID who should receive this notification
    /// NULL means all users in the tenant
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Type of notification (MarkerTimerExpired, StandaloneTimerExpired, MapEvent, etc.)
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Notification title (displayed prominently)
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Notification message/body
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Action type when notification is clicked (NavigateToMarker, NavigateToMap, OpenUrl, NoAction)
    /// </summary>
    public string? ActionType { get; set; }

    /// <summary>
    /// JSON string with action parameters (e.g., {markerId: 123, mapId: 5})
    /// </summary>
    public string? ActionData { get; set; }

    /// <summary>
    /// Whether the user has read this notification
    /// </summary>
    public bool IsRead { get; set; }

    /// <summary>
    /// When the notification was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the notification expires and should be auto-deleted
    /// NULL means never expires
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Priority level (Low, Normal, High, Urgent)
    /// </summary>
    public string Priority { get; set; } = "Normal";
}

/// <summary>
/// Represents a timer (either attached to a marker or standalone)
/// Timers generate notifications when they expire
/// </summary>
public sealed class TimerEntity
{
    /// <summary>
    /// Primary key
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Tenant ID for multi-tenancy isolation
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// User ID who created this timer
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Type of timer (Marker, Standalone)
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Foreign key to MarkerEntity (NULL for standalone timers)
    /// </summary>
    public int? MarkerId { get; set; }

    /// <summary>
    /// Foreign key to CustomMarkerEntity (NULL for resource markers or standalone timers)
    /// </summary>
    public int? CustomMarkerId { get; set; }

    /// <summary>
    /// User-defined title for the timer
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Optional description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// When the timer will expire (UTC timestamp)
    /// </summary>
    public DateTime ReadyAt { get; set; }

    /// <summary>
    /// When the timer was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Whether the timer has completed/expired
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// Whether the expiry notification has been sent
    /// </summary>
    public bool NotificationSent { get; set; }

    /// <summary>
    /// Whether the pre-expiry warning notification has been sent
    /// </summary>
    public bool PreExpiryWarningSent { get; set; }
}

/// <summary>
/// User preferences for notifications
/// Controls which notification types the user wants to receive and how
/// </summary>
public sealed class NotificationPreferenceEntity
{
    /// <summary>
    /// Primary key
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// User ID
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Type of notification (MarkerTimer, StandaloneTimer, MapEvent, etc.)
    /// </summary>
    public string NotificationType { get; set; } = string.Empty;

    /// <summary>
    /// Whether this notification type is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to play sound for this notification type
    /// </summary>
    public bool PlaySound { get; set; } = false;

    /// <summary>
    /// Whether to show browser push notification
    /// </summary>
    public bool ShowBrowserNotification { get; set; } = false;

    /// <summary>
    /// Minutes before timer expires to show pre-expiry warning (for timer notifications)
    /// </summary>
    public int PreExpiryWarningMinutes { get; set; } = 5;
}

/// <summary>
/// Historical record of completed timers
/// Used for learning resource respawn patterns
/// </summary>
public sealed class TimerHistoryEntity
{
    /// <summary>
    /// Primary key
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Original timer ID
    /// </summary>
    public int TimerId { get; set; }

    /// <summary>
    /// Tenant ID for multi-tenancy isolation
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// When the timer completed/expired
    /// </summary>
    public DateTime CompletedAt { get; set; }

    /// <summary>
    /// Actual duration in minutes from creation to completion
    /// </summary>
    public int? Duration { get; set; }

    /// <summary>
    /// Type of timer (Marker, Standalone)
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Marker ID if it was a marker timer
    /// </summary>
    public int? MarkerId { get; set; }

    /// <summary>
    /// Custom marker ID if it was a custom marker timer
    /// </summary>
    public int? CustomMarkerId { get; set; }

    /// <summary>
    /// Timer title (copied from TimerEntity)
    /// </summary>
    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// Tracks which pre-expiry warnings have been sent for a timer
/// Supports multiple warnings at different intervals (1 day, 4 hours, 1 hour, 10 minutes)
/// </summary>
public sealed class TimerWarningEntity
{
    /// <summary>
    /// Primary key
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to TimerEntity
    /// </summary>
    public int TimerId { get; set; }

    /// <summary>
    /// Warning interval in minutes (e.g., 1440 for 1 day, 60 for 1 hour)
    /// </summary>
    public int WarningMinutes { get; set; }

    /// <summary>
    /// UTC timestamp when the warning was sent
    /// </summary>
    public DateTime SentAt { get; set; }
}
