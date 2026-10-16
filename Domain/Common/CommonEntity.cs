using Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Common
{
    public abstract class CommonEntity
    {
        public Guid Id { get; set; }
        public EntityStatus Status { get; set; } = EntityStatus.Active;
        public bool IsDeleted { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        public Guid CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; } 
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; } 
    }
}
