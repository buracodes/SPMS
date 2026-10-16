using Application.DTOS.Organization;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Organization.Requests;

public class GetOrganizationProfileRequest : IRequest<Response<OrganizationProfileDto>>
{
}

public class UpdateOrganizationIdentityCommand : IRequest<Response<OrganizationProfileDto>>
{
    public UpdateOrganizationIdentityDto Dto { get; set; } = null!;
}

// Core Values
public class CreateCoreValueCommand : IRequest<Response<CoreValueDto>>
{
    public CreateCoreValueDto Dto { get; set; } = null!;
}

public class UpdateCoreValueCommand : IRequest<Response<CoreValueDto>>
{
    public UpdateCoreValueDto Dto { get; set; } = null!;
}

public class DeleteCoreValueCommand : IRequest<Response<bool>>
{
    public Guid Id { get; set; }
}

// Strategic Themes
public class CreateStrategicThemeCommand : IRequest<Response<StrategicThemeDto>>
{
    public CreateStrategicThemeDto Dto { get; set; } = null!;
}

public class UpdateStrategicThemeCommand : IRequest<Response<StrategicThemeDto>>
{
    public UpdateStrategicThemeDto Dto { get; set; } = null!;
}

public class DeleteStrategicThemeCommand : IRequest<Response<bool>>
{
    public Guid Id { get; set; }
}

// SWOT Items
public class CreateSwotItemCommand : IRequest<Response<SwotItemDto>>
{
    public CreateSwotItemDto Dto { get; set; } = null!;
}

public class UpdateSwotItemCommand : IRequest<Response<SwotItemDto>>
{
    public UpdateSwotItemDto Dto { get; set; } = null!;
}

public class DeleteSwotItemCommand : IRequest<Response<bool>>
{
    public Guid Id { get; set; }
}

// PESTEL Items
public class CreatePestelItemCommand : IRequest<Response<PestelItemDto>>
{
    public CreatePestelItemDto Dto { get; set; } = null!;
}

public class UpdatePestelItemCommand : IRequest<Response<PestelItemDto>>
{
    public UpdatePestelItemDto Dto { get; set; } = null!;
}

public class DeletePestelItemCommand : IRequest<Response<bool>>
{
    public Guid Id { get; set; }
}
