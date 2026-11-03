namespace Application.DTOS.HR
{
    public class HrDivisionDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        /// <summary>HR lifecycle flag (1 = active); mirrored into the local Division.IsActive.</summary>
        public int Status { get; set; }
    }
}
