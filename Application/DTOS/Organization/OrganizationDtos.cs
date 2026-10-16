using Domain.Enums;

namespace Application.DTOS.Organization;

public class OrganizationProfileDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Vision { get; set; } = string.Empty;
    public string Mission { get; set; } = string.Empty;

    public List<CoreValueDto> CoreValues { get; set; } = new();
    public List<StrategicThemeDto> StrategicThemes { get; set; } = new();
    public List<SwotItemDto> SwotItems { get; set; } = new();
    public List<PestelItemDto> PestelItems { get; set; } = new();
}

public class UpdateOrganizationIdentityDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Vision { get; set; } = string.Empty;
    public string Mission { get; set; } = string.Empty;
}

public class CoreValueDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class CreateCoreValueDto
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UpdateCoreValueDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class StrategicThemeDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Description { get; set; }
    public string? Color { get; set; }
}

public class CreateStrategicThemeDto
{
    public string Title { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Description { get; set; }
    public string? Color { get; set; }
}

public class UpdateStrategicThemeDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Description { get; set; }
    public string? Color { get; set; }
}

public class SwotItemDto
{
    public Guid Id { get; set; }
    public SwotCategory Category { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ImpactLevel Impact { get; set; }
}

public class CreateSwotItemDto
{
    public SwotCategory Category { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ImpactLevel Impact { get; set; } = ImpactLevel.Medium;
}

public class UpdateSwotItemDto
{
    public Guid Id { get; set; }
    public SwotCategory Category { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ImpactLevel Impact { get; set; }
}

public class PestelItemDto
{
    public Guid Id { get; set; }
    public PestelCategory Category { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ImpactLevel Impact { get; set; }
}

public class CreatePestelItemDto
{
    public PestelCategory Category { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ImpactLevel Impact { get; set; } = ImpactLevel.Medium;
}

public class UpdatePestelItemDto
{
    public Guid Id { get; set; }
    public PestelCategory Category { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ImpactLevel Impact { get; set; }
}
