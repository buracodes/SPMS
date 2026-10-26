using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class GroupMember
    {
        public string UserId { get; set; }
        public Guid GroupId { get; set; }
    }
}
