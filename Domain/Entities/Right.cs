using Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class Right : CommonEntity
    {
        public string Name { get; set; }
        public string RightCode { get; set; }
    }
}
