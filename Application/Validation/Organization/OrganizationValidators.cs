using Application.DTOS.Organization;
using FluentValidation;

namespace Application.Validation.Organization;

public class UpdateOrganizationIdentityDtoValidator : AbstractValidator<UpdateOrganizationIdentityDto>
{
    public UpdateOrganizationIdentityDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Vision).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Mission).NotEmpty().MaximumLength(2000);
    }
}

public class CreateCoreValueDtoValidator : AbstractValidator<CreateCoreValueDto>
{
    public CreateCoreValueDtoValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
    }
}

public class UpdateCoreValueDtoValidator : AbstractValidator<UpdateCoreValueDto>
{
    public UpdateCoreValueDtoValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
    }
}

public class CreateStrategicThemeDtoValidator : AbstractValidator<CreateStrategicThemeDto>
{
    public CreateStrategicThemeDtoValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
    }
}

public class UpdateStrategicThemeDtoValidator : AbstractValidator<UpdateStrategicThemeDto>
{
    public UpdateStrategicThemeDtoValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
    }
}

public class CreateSwotItemDtoValidator : AbstractValidator<CreateSwotItemDto>
{
    public CreateSwotItemDtoValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.Impact).IsInEnum();
    }
}

public class UpdateSwotItemDtoValidator : AbstractValidator<UpdateSwotItemDto>
{
    public UpdateSwotItemDtoValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.Impact).IsInEnum();
    }
}

public class CreatePestelItemDtoValidator : AbstractValidator<CreatePestelItemDto>
{
    public CreatePestelItemDtoValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.Impact).IsInEnum();
    }
}

public class UpdatePestelItemDtoValidator : AbstractValidator<UpdatePestelItemDto>
{
    public UpdatePestelItemDtoValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.Impact).IsInEnum();
    }
}
