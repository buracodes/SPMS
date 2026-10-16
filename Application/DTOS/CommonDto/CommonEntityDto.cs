using Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.DTOS.CommonDto
{
    public abstract class CommonEntityDto
    {
        public Guid CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
        public EntityStatus Status { get; set; }
    }
}
