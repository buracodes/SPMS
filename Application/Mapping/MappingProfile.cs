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
            // CommonEntity.Status (the soft-delete row state, EntityStatus.Active) wins
            // AutoMapper's exact-name match and would mask the lifecycle status with
            // "Active"; the lifecycle status must come from FiscalYearStatus so
            // Open/Closed/Archived reach the API and the UI's FR-703 edit gating.
            CreateMap<FiscalYear, FiscalYearDto>()
                .ForMember(d => d.Status, o => o.MapFrom(s => s.FiscalYearStatus.ToString()))
                .ReverseMap();
        }
    }
}
