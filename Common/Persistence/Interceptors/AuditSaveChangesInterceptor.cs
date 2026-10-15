using System.Text.Json;
using Microservices.Common.Context;
using Microservices.Common.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Microservices.Common.Persistence.Interceptors;

public class AuditSaveChangesInterceptor(IServiceProvider serviceProvider) : SaveChangesInterceptor
{
    private readonly AsyncLocal<List<AuditEntryData>?> pendingAuditEntries = new();

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        UpdateEntities(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        UpdateEntities(eventData.Context);
        CaptureAuditEntries(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        var entries = pendingAuditEntries.Value;
        if (entries is not null && entries.Count > 0)
        {
            using var scope = serviceProvider.CreateScope();
            var auditPublisher = scope.ServiceProvider.GetService<IAuditOutboxPublisher>();
            var currentUser = scope.ServiceProvider.GetService<ICurrentUser>() ?? SystemCurrentUser.Instance;

            if (auditPublisher is not null)
            {
                foreach (var entry in entries)
                {
                    var auditEvent = new EntityAuditedEvent
                    {
                        EntityName = entry.EntityName,
                        EntityId = entry.GetEntityId(),
                        Action = entry.Action,
                        OldValues = entry.OldValues.Count > 0 ? JsonSerializer.Serialize(entry.OldValues) : null,
                        NewValues = entry.NewValues.Count > 0 ? JsonSerializer.Serialize(entry.NewValues) : null,
                        UserId = currentUser.UserId,
                        ClientIP = currentUser.ClientIp,
                        UserAgent = currentUser.UserAgent,
                        DigitalSignatureHash = null
                    };

                    await auditPublisher.PublishAuditAsync(auditEvent, cancellationToken);
                }
            }

            pendingAuditEntries.Value = null;
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    private void UpdateEntities(DbContext? context)
    {
        if (context is null) return;

        var now = DateTimeOffset.UtcNow;
        var currentUser = ResolveCurrentUser();
        var userId = currentUser.UserId;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is ISoftDeletable softDeletable && entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                softDeletable.IsDeleted = true;
                softDeletable.DeletedAt = now;

                if (entry.Entity is IAuditableEntity auditOnDelete)
                {
                    auditOnDelete.UpdatedAt = now;
                    auditOnDelete.UpdatedBy = userId;
                }
                continue;
            }

            if (entry.Entity is IAuditableEntity auditable)
            {
                if (entry.State == EntityState.Added)
                {
                    auditable.CreatedAt = now;
                    auditable.CreatedBy ??= userId;
                }
                else if (entry.State == EntityState.Modified)
                {
                    auditable.UpdatedAt = now;
                    auditable.UpdatedBy = userId;
                }
            }
        }
    }

    private void CaptureAuditEntries(DbContext? context)
    {
        if (context is null) return;

        var entries = new List<AuditEntryData>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Detached or EntityState.Unchanged)
                continue;

            // Skip MassTransit internal tables and audit events themselves
            var entityName = entry.Entity.GetType().Name;
            if (entityName.StartsWith("Inbox") || entityName.StartsWith("Outbox") || entityName == nameof(EntityAuditedEvent))
                continue;

            var auditEntry = new AuditEntryData(entry)
            {
                EntityName = entityName,
                Action = entry.State switch
                {
                    EntityState.Added => "Created",
                    EntityState.Deleted => "Deleted",
                    EntityState.Modified => entry.Entity is ISoftDeletable { IsDeleted: true } ? "Deleted" : "Modified",
                    _ => entry.State.ToString()
                }
            };

            foreach (var property in entry.Properties)
            {
                if (property.IsTemporary)
                    continue;

                var propName = property.Metadata.Name;

                switch (entry.State)
                {
                    case EntityState.Added:
                        auditEntry.NewValues[propName] = property.CurrentValue;
                        break;
                    case EntityState.Deleted:
                        auditEntry.OldValues[propName] = property.OriginalValue;
                        break;
                    case EntityState.Modified:
                        if (property.IsModified)
                        {
                            auditEntry.OldValues[propName] = property.OriginalValue;
                            auditEntry.NewValues[propName] = property.CurrentValue;
                        }
                        break;
                }
            }

            entries.Add(auditEntry);
        }

        pendingAuditEntries.Value = entries;
    }

    private ICurrentUser ResolveCurrentUser()
    {
        using var scope = serviceProvider.CreateScope();
        return scope.ServiceProvider.GetService<ICurrentUser>() ?? SystemCurrentUser.Instance;
    }

    private IAuditOutboxPublisher? ResolveAuditPublisher()
    {
        using var scope = serviceProvider.CreateScope();
        return scope.ServiceProvider.GetService<IAuditOutboxPublisher>();
    }

    private sealed class AuditEntryData(EntityEntry entry)
    {
        public EntityEntry Entry { get; } = entry;
        public string EntityName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public Dictionary<string, object?> OldValues { get; } = [];
        public Dictionary<string, object?> NewValues { get; } = [];

        public string GetEntityId()
        {
            var primaryKey = Entry.Metadata.FindPrimaryKey();
            if (primaryKey is null) return string.Empty;

            var keyValues = primaryKey.Properties
                .Select(p => Entry.Property(p.Name).CurrentValue?.ToString() ?? string.Empty);

            return string.Join(":", keyValues);
        }
    }
}
