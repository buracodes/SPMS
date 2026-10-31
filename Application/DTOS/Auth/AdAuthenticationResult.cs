using System;

namespace Application.DTOS.Auth;

/// <summary>
/// Transient outcome of an Active Directory bind. Authentication state is never persisted on the
/// user row: SPMS trusts only the current bind result plus a valid application session.
/// </summary>
public sealed record AdAuthenticationResult(
    bool IsAuthenticated,
    Guid ObjectId,
    string Username,
    string FullName,
    string? Email)
{
    public static AdAuthenticationResult Failed { get; } =
        new(false, Guid.Empty, string.Empty, string.Empty, null);
}
