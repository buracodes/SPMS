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

    /// <summary>HR identifiers shown by the owner pickers; null for local-only accounts.</summary>
    public string? EmployeeCode { get; set; }
    public string? PositionName { get; set; }
    public string? PositionLevel { get; set; }

    /// <summary>
    /// HR name parts. HR stores the father's name in MiddleName and the grandfather's name in
    /// LastName; FullName deliberately keeps the shorter corporate form (first + father), while
    /// these fields let a picker show the complete first + father + grandfather name.
    /// </summary>
    public string? FirstName { get; set; }
    public string? FatherName { get; set; }
    public string? GrandfatherName { get; set; }
    public string? DepartmentName { get; set; }

    public List<string> Roles { get; set; } = new();
}
