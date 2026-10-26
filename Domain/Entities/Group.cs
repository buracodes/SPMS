using Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class Group : CommonEntity
    {
        public string Name { get; set; }
        public string GroupCode { get; set; }
    }
}
