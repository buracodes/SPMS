using Application.DTOS.CommonDto;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.DTOS.Goal
{
    public class GoalDto : CommonEntityDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }
}
