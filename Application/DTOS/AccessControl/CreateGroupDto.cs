using Application.DTOS.CommonDto;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.DTOS.AccessControl
{
    public class CreateGroupDto : CommonEntityDto
    {
        public string Name { get; set; }
        public string GroupCode { get; set; }
        public string? Description { get; set; }
    }
}
