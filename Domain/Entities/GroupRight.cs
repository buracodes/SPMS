using Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class GroupRight : CommonEntity
    {
        public Guid GroupId { get; set; }
        public Guid RightId { get; set; }
    }
}
