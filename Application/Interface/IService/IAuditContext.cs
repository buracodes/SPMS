namespace Application.Interface.IService;

/// <summary>
/// Request-scoped channel between command handlers and the persistence audit
/// interceptor. A handler that performs a write the user must justify (for example an
/// actual-value override on a closed scorecard) sets <see cref="Reason"/> so the
/// append-only <see cref="Domain.Entities.AuditLogEntry"/> records why the write
/// happened. The interceptor clears it after every audited save to keep reasons from
/// leaking into unrelated saves.
/// </summary>
public interface IAuditContext
{
    string? Reason { get; set; }
}