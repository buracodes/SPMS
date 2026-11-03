using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Explicit cross-division authorization for a local account. The employee's own HR division
/// is always the primary scope and is never stored here; a row only widens the caller to one
/// additional division. Rows are local SPMS authorization data (never written to HR) and are
/// resolved live, so deactivating or soft-deleting a row revokes the scope on the next request.
/// </summary>
public class UserDivisionScope : CommonEntity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid DivisionId { get; set; }
    public Division? Division { get; set; }
}
