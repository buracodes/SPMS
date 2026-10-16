using Application.DTOS.Initiative;
using Application.Responses;
using Domain.Enums;
using MediatR;

namespace Application.CQRS.Initiative.Requests.Commands;

/// <summary>
/// FR-403 progress update for a single milestone. The initiative's overall progress is
/// recalculated by the backend afterwards; clients never send it.
/// </summary>
public class UpdateMilestoneProgressCommandRequest : IRequest<Response<InitiativeDto>>
{
    public required UpdateMilestoneProgressDto Dto { get; set; }
}

public class UpdateMilestoneProgressDto
{
    public Guid InitiativeId { get; set; }

    public Guid MilestoneId { get; set; }

    /// <summary>0-100. Setting 100 automatically moves the milestone to Completed (FR-403).</summary>
    public int CompletionPercentage { get; set; }

    /// <summary>Optional explicit status; ignored (forced to Completed) when completion is 100%.</summary>
    public MilestoneStatus? Status { get; set; }

    /// <summary>FR-403 progress note. Attachments are out of scope: the application has no file store.</summary>
    public string? Comments { get; set; }
}

/// <summary>FR-405: record closure information and lock the initiative.</summary>
public class CloseInitiativeCommandRequest : IRequest<Response<InitiativeDto>>
{
    public required CloseInitiativeDto Dto { get; set; }
}

public class CloseInitiativeDto
{
    public Guid InitiativeId { get; set; }

    /// <summary>Required by the FRD.</summary>
    public DateTime ActualEndDate { get; set; }

    /// <summary>Final spend. Defaults to the current actual expenditure when omitted.</summary>
    public decimal? FinalActualExpenditure { get; set; }

    /// <summary>FR-405: optional, per the functional requirement.</summary>
    public string OutcomeSummary { get; set; } = string.Empty;

    public string? LessonsLearned { get; set; }
}

/// <summary>Reopens a closed initiative. Reserved for users holding the close permission.</summary>
public class ReopenInitiativeCommandRequest : IRequest<Response<InitiativeDto>>
{
    public Guid InitiativeId { get; set; }

    public string? Reason { get; set; }
}

public class AddInitiativeDependencyCommandRequest : IRequest<Response<InitiativeDto>>
{
    public Guid InitiativeId { get; set; }

    public Guid DependsOnInitiativeId { get; set; }
}

public class RemoveInitiativeDependencyCommandRequest : IRequest<Response<InitiativeDto>>
{
    public Guid InitiativeId { get; set; }

    public Guid DependsOnInitiativeId { get; set; }
}
