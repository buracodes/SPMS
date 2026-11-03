namespace Application.DTOS.User;

public class UserDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public Guid? DivisionId { get; set; }
    public string? DivisionName { get; set; }

    /// <summary>HR identifiers shown by the KPI owner picker; null for local-only accounts.</summary>
    public string? EmployeeCode { get; set; }
    public string? PositionName { get; set; }
    public string? PositionLevel { get; set; }

    public List<string> Roles { get; set; } = new();
}
