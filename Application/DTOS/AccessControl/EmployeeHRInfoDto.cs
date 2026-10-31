using System;
using System.Collections.Generic;
using System.Text;

namespace Application.DTOS.AccessControl
{
    public class EmployeeHRInfoDto
    {
        public Guid? EmpId { get; set; }
        public string? IdCode { get; set; }
        public string? FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }

        public Guid? PositionId { get; set; }
        public string? PositionName { get; set; }
        public string? PositionLevel { get; set; }

        public Guid? DivisionId { get; set; }
        public string? DivisionName { get; set; }

        public Guid? DepartmentId { get; set; }
        public string? DepartmentName { get; set; }

        public Guid? BranchId { get; set; }
        public string? BranchName { get; set; }

        public string FullName => $"{FirstName} {MiddleName} {LastName}".Trim();
    }
}
