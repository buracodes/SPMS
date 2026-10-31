using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class Position
    {
        public Guid PositionId { get; set; }
        public Guid BranchId { get; set; }
        public string PositionName { get; set; }
        public string BranchName { get; set; }
        public string PositionLevel { get; set; }
    }
    public class PositionLookupResponse
    {
        public List<string> Positions { get; set; }
        public List<string> Departments { get; set; }
    }
}
