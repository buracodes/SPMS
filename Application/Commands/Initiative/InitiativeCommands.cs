using Application.DTOS.Initiative;
using Application.Responses;
using MediatR;
using Domain.Enums;

namespace Application.Commands.Initiative;

public class CreateInitiativeCommand : IRequest<Response<InitiativeDto>>
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    public Guid? GoalId { get; set; }

    public Guid? KraId { get; set; }

    public Guid? KpiId { get; set; }

    public Guid OwnerId { get; set; }

    public InitiativePriority Priority { get; set; } = InitiativePriority.Medium;

    public InitiativeStatus Status { get; set; } = InitiativeStatus.NotStarted;

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public decimal Budget { get; set; }

    public string? Resources { get; set; }
}

public class UpdateInitiativeCommand : IRequest<Response<InitiativeDto>>
{
    public required Guid Id { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public Guid? GoalId { get; set; }

    public Guid? KraId { get; set; }

    public Guid? KpiId { get; set; }

    public InitiativePriority? Priority { get; set; }

    public InitiativeStatus? Status { get; set; }

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public decimal? Budget { get; set; }

    public string? Resources { get; set; }
}

public class DeleteInitiativeCommand : IRequest
{
    public required Guid Id { get; set; }
}

public class CreateMilestoneCommand : IRequest<Response<MilestoneDto>>
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    public int CompletionPercentage { get; set; }

    public DateTime DueDate { get; set; }

    public decimal Weight { get; set; } = 1m;

    public required Guid InitiativeId { get; set; }
}

public class UpdateMilestoneCommand : IRequest<Response<MilestoneDto>>
{
    public required Guid Id { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public int CompletionPercentage { get; set; }

    public DateTime DueDate { get; set; }

    public decimal Weight { get; set; }
}

public class DeleteMilestoneCommand : IRequest
{
    public required Guid Id { get; set; }
}