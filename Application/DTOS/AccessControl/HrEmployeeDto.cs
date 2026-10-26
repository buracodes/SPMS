namespace Application.DTOS.AccessControl
{
    public class HrEmployeeDto
    {
        public string EmpId { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public int Status { get; set; }
    }
}
