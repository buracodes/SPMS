using Application.DTOS.CommonDto;
using Application.DTOS.FiscalYear;
using Application.DTOS.Goal;
using AutoMapper;
using Domain.Common;
using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Mapping
{
    public class MappingProfile: Profile
    {
        public MappingProfile()
        {
            CreateMap<CommonEntity, CommonEntityDto>().ReverseMap();
            CreateMap<Goal, GetAllGoalDto>().ReverseMap();
            CreateMap<Goal, CreateGoalDto>().ReverseMap();
            CreateMap<FiscalYear, FiscalYearDto>().ReverseMap();
        }
    }
}
