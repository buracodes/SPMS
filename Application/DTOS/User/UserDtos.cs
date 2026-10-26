namespace Application.DTOS.User;

public class UserDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    public Guid? DivisionId { get; set; }
    public string? DivisionName { get; set; }
    public List<string> Roles { get; set; } = new();
}
