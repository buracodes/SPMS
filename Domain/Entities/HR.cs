using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class HR
    {
        public Guid EmpId { get; set; }
        public string FirstName { get; set; }
        public string MiddleName { get; set; }
        public string LastName { get; set; }
        public int Status { get; set; }
    }
}
