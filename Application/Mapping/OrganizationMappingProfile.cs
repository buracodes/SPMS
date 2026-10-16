using Application.DTOS.Organization;
using AutoMapper;
using Domain.Entities;

namespace Application.Mapping;

public class OrganizationMappingProfile : Profile
{
    public OrganizationMappingProfile()
    {
        CreateMap<OrganizationProfile, OrganizationProfileDto>();
        CreateMap<UpdateOrganizationIdentityDto, OrganizationProfile>();

        CreateMap<CoreValue, CoreValueDto>();
        CreateMap<CreateCoreValueDto, CoreValue>();
        CreateMap<UpdateCoreValueDto, CoreValue>();

        CreateMap<StrategicTheme, StrategicThemeDto>();
        CreateMap<CreateStrategicThemeDto, StrategicTheme>();
        CreateMap<UpdateStrategicThemeDto, StrategicTheme>();

        CreateMap<SwotItem, SwotItemDto>();
        CreateMap<CreateSwotItemDto, SwotItem>();
        CreateMap<UpdateSwotItemDto, SwotItem>();

        CreateMap<PestelItem, PestelItemDto>();
        CreateMap<CreatePestelItemDto, PestelItem>();
        CreateMap<UpdatePestelItemDto, PestelItem>();
    }
}
