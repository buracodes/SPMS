using Application.Interface.IService;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Infrastructure.Persistence.Interceptors
{
    public class AuditableEntitySaveChangesInterceptor : SaveChangesInterceptor
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly ICurrentUserService currentUserService;
        private readonly ILogger<AuditableEntitySaveChangesInterceptor> logger;
        private readonly IAuditContext auditContext;

        public AuditableEntitySaveChangesInterceptor(
            ICurrentUserService currentUserService,
            ILogger<AuditableEntitySaveChangesInterceptor> logger,
            IAuditContext auditContext)
        {
            this.currentUserService = currentUserService;
            this.logger = logger;
            this.auditContext = auditContext;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not null)
            {
                await ApplyAuditAsync(eventData.Context, cancellationToken);
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (eventData.Context is not null)
            {
                ApplyAuditAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
            }

            return base.SavingChanges(eventData, result);
        }

        private async Task ApplyAuditAsync(DbContext context, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var userId = currentUserService.UserId;
            var userName = currentUserService.UserName;
            var auditEntries = new List<AuditLogEntry>();

            foreach (var entry in context.ChangeTracker.Entries<CommonEntity>())
            {
                var action = ResolveAction(entry.State);
                if (action is null)
                {
                    continue;
                }

                string? oldValues = null;

                switch (entry.State)
                {
                    case EntityState.Added:
                        entry.Entity.CreatedDate = now;
                        entry.Entity.CreatedBy = userId ?? Guid.Empty;
                        break;

                    case EntityState.Modified:
                        entry.Entity.UpdatedDate = now;
                        entry.Entity.UpdatedBy = userId;
                        var databaseValues = await entry.GetDatabaseValuesAsync(cancellationToken);
                        oldValues = databaseValues is null ? null : SerializeValues(databaseValues);
                        break;

                    case EntityState.Deleted:
                        oldValues = SerializeValues(entry.CurrentValues);
                        break;
                }

                var newValues = entry.State == EntityState.Deleted ? null : SerializeValues(entry.CurrentValues);

                auditEntries.Add(new AuditLogEntry
                {
                    Id = Guid.NewGuid(),
                    EntityType = entry.Metadata.ClrType.Name,
                    EntityId = entry.Property(nameof(CommonEntity.Id)).CurrentValue?.ToString() ?? string.Empty,
                    Action = action.Value,
                    OldValues = oldValues,
                    NewValues = newValues,
                    PerformedBy = userId,
                    PerformedByName = userName,
                    PerformedAt = now,
                    Reason = auditContext.Reason
                });
            }

            if (auditEntries.Count > 0)
            {
                context.AddRange(auditEntries);

                // A reason is scoped to the write that justified it. Clear it so a later
                // save in the same request cannot inherit another command's reason.
                auditContext.Reason = null;
            }
        }

        private static AuditAction? ResolveAction(EntityState state)
        {
            return state switch
            {
                EntityState.Added => AuditAction.Create,
                EntityState.Modified => AuditAction.Update,
                EntityState.Deleted => AuditAction.Delete,
                _ => null
            };
        }

        private static string SerializeValues(PropertyValues values)
        {
            var dictionary = new Dictionary<string, object?>();
            foreach (var property in values.Properties)
            {
                if (property.PropertyInfo is null)
                {
                    continue;
                }

                var value = values[property];
                if (value is byte[] bytes)
                {
                    value = Convert.ToBase64String(bytes);
                }

                dictionary[property.Name] = value;
            }

            return JsonSerializer.Serialize(dictionary, JsonOptions);
        }
    }
}