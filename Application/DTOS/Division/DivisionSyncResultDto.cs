namespace Application.DTOS.Division;

public class DivisionSyncResultDto
{
    public int TotalHrDivisions { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    /// <summary>HR divisions that could not be applied because a different local row already owns the name.</summary>
    public List<DivisionSyncConflictDto> Conflicts { get; set; } = new();
}

public class DivisionSyncConflictDto
{
    public Guid HrDivisionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
