using Application.DTOS.CommonDto;
using Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.DTOS.Goal
{
    public class UpdateGoalDto 
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public EntityStatus? Status { get; set; }
    }
}
