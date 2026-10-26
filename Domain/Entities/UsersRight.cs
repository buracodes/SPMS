using Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class UsersRight : CommonEntity
    {
        public Guid UserId { get; set; }
        public Guid RightId { get; set; }
    }
}
