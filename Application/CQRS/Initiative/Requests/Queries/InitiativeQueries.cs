using Application.DTOS.Initiative;
using MediatR;

namespace Application.CQRS.Initiative.Requests.Queries;

/// <summary>
/// Server-side searchable/filterable/sortable initiative grid. Returns only the fields the
/// grid renders, with progress and budget already calculated by the backend.
/// </summary>
public class GetAllInitiativeQuery : IRequest<PagedInitiativeDto>
{
    public string? Search { get; set; }

    public Domain.Enums.InitiativeStatus? Status { get; set; }

    public Domain.Enums.InitiativePriority? Priority { get; set; }

    public Guid? OwnerId { get; set; }

    public bool IncludeClosed { get; set; } = true;

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 25;

    /// <summary>
    /// When false the whole (filtered) set is returned in one page. Dashboard widgets that
    /// genuinely need every initiative use this so they are not forced through pagination.
    /// </summary>
    public bool Paged { get; set; } = true;
}

/// <summary>FR-403 progress/change history for an initiative, newest first.</summary>
public class GetInitiativeHistoryQueryRequest : IRequest<List<InitiativeUpdateLogDto>>
{
    public Guid InitiativeId { get; set; }

    /// <summary>Restrict to a single milestone's history when supplied.</summary>
    public Guid? MilestoneId { get; set; }
}

/// <summary>Portfolio metrics for dashboards and reports.</summary>
public class GetInitiativeSummaryQuery : IRequest<InitiativeSummaryDto>
{
    /// <summary>When set, metrics are limited to initiatives owned by this user.</summary>
    public Guid? OwnerId { get; set; }
}
