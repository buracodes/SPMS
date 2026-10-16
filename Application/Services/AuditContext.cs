using Application.Interface.IService;

namespace Application.Services;

public sealed class AuditContext : IAuditContext
{
    public string? Reason { get; set; }
}