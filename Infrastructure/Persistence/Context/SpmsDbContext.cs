using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Infrastructure.Persistence.Context
{
    public class SpmsDbContext : DbContext
    {
        public SpmsDbContext(DbContextOptions<SpmsDbContext> options) : base(options)
        {
            
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            {
                foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
            }

            modelBuilder.ApplyConfigurationsFromAssembly(
               typeof(SpmsDbContext).Assembly);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                if (typeof(CommonEntity).IsAssignableFrom(entityType.ClrType))
                {
                    ApplySoftDeleteQueryFilter(entityType.ClrType, modelBuilder);
                    ConfigureRowVersion(entityType.ClrType, modelBuilder, Database.ProviderName);
                }
            }
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            base.ConfigureConventions(configurationBuilder);
            configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
        }

        private static void ApplySoftDeleteQueryFilter(Type entityType, ModelBuilder modelBuilder)
        {
            var method = typeof(SpmsDbContext)
                .GetMethod(nameof(SetSoftDeleteQueryFilter), BindingFlags.NonPublic | BindingFlags.Static)
                ?.MakeGenericMethod(entityType);
            method?.Invoke(null, new object[] { modelBuilder });
        }

        // A record leaves the UI when it is deactivated, never when its row is removed. Two flags can
        // express that, and both are honoured: IsDeleted (set by GenericRepository.Delete for every
        // entity) and the generic EntityStatus lifecycle flag, where the entity has one, moving from
        // Active (1) to Inactive (0). Initiative and Milestone shadow Status with their execution
        // enums, where 0 is not a valid value, so for them the comparison is always true and only
        // IsDeleted takes effect.
        private static void SetSoftDeleteQueryFilter<TEntity>(ModelBuilder modelBuilder)
            where TEntity : CommonEntity
        {
            modelBuilder.Entity<TEntity>()
                .HasQueryFilter(e => !e.IsDeleted && e.Status != EntityStatus.Inactive);
        }

        private static void ConfigureRowVersion(Type entityType, ModelBuilder modelBuilder, string? providerName)
        {
            var method = typeof(SpmsDbContext)
                .GetMethod(nameof(SetRowVersion), BindingFlags.NonPublic | BindingFlags.Static)
                ?.MakeGenericMethod(entityType);
            method?.Invoke(null, new object?[] { modelBuilder, providerName });
        }

        private static void SetRowVersion<TEntity>(ModelBuilder modelBuilder, string? providerName)
            where TEntity : CommonEntity
        {
            var property = modelBuilder.Entity<TEntity>()
                .Property(e => e.RowVersion);

            if (providerName == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                property.IsConcurrencyToken();
                return;
            }

            property.IsRowVersion();
        }

        public DbSet<Goal> Goals { get; set; }
        public DbSet<Kra> Kras { get; set; }
        public DbSet<Kpi> Kpis { get; set; }
        public DbSet<KpiTarget> KpiTargets { get; set; }
        public DbSet<KpiActualValue> KpiActualValues { get; set; }
        public DbSet<FiscalYear> FiscalYears { get; set; }
        public DbSet<FiscalPeriod> FiscalPeriods { get; set; }
        public DbSet<Scorecard> Scorecards { get; set; }
        public DbSet<AuditLogEntry> AuditLogEntries { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<Permission> Permissions { get; set; }
        public DbSet<UserRole> UserRoles { get; set; }
        public DbSet<RolePermission> RolePermissions { get; set; }
        public DbSet<UserRight> UserRights { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<OrganizationProfile> OrganizationProfiles { get; set; }
        public DbSet<CoreValue> CoreValues { get; set; }
        public DbSet<StrategicTheme> StrategicThemes { get; set; }
        public DbSet<SwotItem> SwotItems { get; set; }
        public DbSet<PestelItem> PestelItems { get; set; }
        public DbSet<UnitsOfMeasure> UnitsOfMeasures { get; set; }
        public DbSet<PeriodLockConfig> PeriodLockConfigs { get; set; }
        public DbSet<SystemConfig> SystemConfigs { get; set; }
        public DbSet<RagThresholdConfig> RagThresholdConfigs { get; set; }
        public DbSet<KpiScoreSnapshot> KpiScoreSnapshots { get; set; }
        public DbSet<KraScoreSnapshot> KraScoreSnapshots { get; set; }
        public DbSet<GoalScoreSnapshot> GoalScoreSnapshots { get; set; }
        public DbSet<ScorecardScoreSnapshot> ScorecardScoreSnapshots { get; set; }
        public DbSet<Division> Divisions { get; set; }
        public DbSet<DivisionPlan> DivisionPlans { get; set; }
        public DbSet<DivisionContribution> DivisionContributions { get; set; }
        public DbSet<DivisionGoal> DivisionGoals { get; set; }
        public DbSet<DivisionScoreSnapshot> DivisionScoreSnapshots { get; set; }
        public DbSet<Initiative> Initiatives { get; set; }
        public DbSet<Milestone> Milestones { get; set; }
        public DbSet<InitiativeUpdateLog> InitiativeUpdateLogs { get; set; }
        public DbSet<InitiativeDependency> InitiativeDependencies { get; set; }

    }

    public class SecurityDbContext : DbContext
    {
        public SecurityDbContext(DbContextOptions<SecurityDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            {
                foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
            }
            modelBuilder.ApplyConfigurationsFromAssembly(
               typeof(SecurityDbContext).Assembly);

        }
    }

    public class hrDbContext : DbContext
    {
        public hrDbContext(DbContextOptions<hrDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            {
                foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
            }
            modelBuilder.ApplyConfigurationsFromAssembly(
               typeof(hrDbContext).Assembly);

        }
    }
}
