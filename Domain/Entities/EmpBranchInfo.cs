using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class EmpBranchInfo
    {
        public Guid? EmpId { get; set; }
        public Guid BranchId { get; set; }
        public string BranchName { get; set; }
        public Guid PositionId { get; set; }
        public string? PositionLevel { get; set; }
        public string PositionName { get; set; }
        public string? Email { get; set; }
    }

    public class EmpBySuperVisor
    {
        //public Guid? Id { get; set; }
        public Guid BranchId { get; set; }
        public Guid? EmpId { get; set; }
        public string BranchName { get; set; }
        public Guid PositionId { get; set; }
        public string? PositionLevel { get; set; }
        public Guid? SupervisiorId { get; set; }
        public string PositionName { get; set; }
        public string FirstName { get; set; }
        public string? Email { get; set; }
    }
    public class supId
    {
        public Guid Id { get; set; }
    }
    public class Empids
    {
        public Guid EmpId { get; set; }
    }


    public class EmpPositionInfo
    {
        public Guid EmpId { get; set; }
        public Guid SupervisiorId { get; set; }
        public Guid BranchId { get; set; }

    }

    public class EmpMappedWithSuperv
    {
        public Guid EmpId { get; set; }
        public Guid SupervisiorEmpId { get; set; }

    }
}
