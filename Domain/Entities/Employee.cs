using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class Employee
    {

        public Guid EmpId { get; set; }
        public int? EcxIdCode { get; set; }
        public string? IdCode { get; set; }
        public string? EcxId { get; set; }
        public string? AdId { get; set; }
        public string FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public DateTime? JoinDate { get; set; }
        public string? Sex { get; set; }
        public DateTime? DateOfBityh { get; set; }
        public string? PlaceOfBith { get; set; }
        public string? MartialStatus { get; set; }
        public string? Salutation { get; set; }
        public string? Nationality { get; set; }
        public string? PensionNoPrivate { get; set; }
        public string? PensionNoGovernment { get; set; }
        public string? File { get; set; }
        public bool? Crime { get; set; }
        public string? CrimeDescription { get; set; }
        public bool? ExistedEmployee { get; set; }
        public int? CurrentStatus { get; set; }
        public int? Status { get; set; }
        public string? AttendanceId { get; set; }



    }

    public class EmpList
    {
        public Employee Employee { get; set; }
    }


    public class EmployeeSupervisor
    {

        public Guid EmpId { get; set; }
        public int? EcxIdCode { get; set; }
        public string? IdCode { get; set; }
        public string? EcxId { get; set; }
        public string? AdId { get; set; }
        public string FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public DateTime? JoinDate { get; set; }
        public string? Sex { get; set; }
        public DateTime? DateOfBityh { get; set; }
        public string? PlaceOfBith { get; set; }
        public string? MartialStatus { get; set; }
        public string? Salutation { get; set; }
        public string? Nationality { get; set; }
        public string? PensionNoPrivate { get; set; }
        public string? PensionNoGovernment { get; set; }
        public string? File { get; set; }
        public bool? Crime { get; set; }
        public string? CrimeDescription { get; set; }
        public bool? ExistedEmployee { get; set; }
        public int? CurrentStatus { get; set; }
        public int? Status { get; set; }
        public string? AttendanceId { get; set; }

        public Guid BranchId { get; set; }
        public string BranchName { get; set; }
        public Guid PositionId { get; set; }
        public Guid? SupervisiorId { get; set; }
        public string? Email { get; set; }

    }
}
