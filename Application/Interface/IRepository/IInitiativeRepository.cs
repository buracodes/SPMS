using Domain.Calculation;
using Domain.Enums;
using Domain.Entities;

namespace Application.Interface.IRepository;

public interface IInitiativeRepository : IGenericRepository<Initiative>
{
    Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null);

    Task<Initiative?> GetByIdWithMilestonesAsync(Guid id);

    /// <summary>Detail read: milestones plus the display names of the strategic links and the owner.</summary>
    Task<Initiative?> GetDetailAsync(Guid id);

    Task<IReadOnlyList<Initiative>> GetByOwnerIdAsync(Guid ownerId);

    /// <summary>All initiatives with their milestones loaded, for progress and milestone reporting.</summary>
    Task<IReadOnlyList<Initiative>> GetAllWithMilestonesAsync();

    /// <summary>Initiatives owned by the given user, with their milestones loaded.</summary>
    Task<IReadOnlyList<Initiative>> GetByOwnerIdWithMilestonesAsync(Guid ownerId);

    Task<IReadOnlyList<Initiative>> GetByStatusAsync(InitiativeStatus status);

    Task<IReadOnlyList<Initiative>> GetByPriorityAsync(InitiativePriority priority);

    /// <summary>
    /// Server-side searchable/filterable/sortable projection for the initiative grid, with the
    /// weighted progress already calculated in SQL-adjacent form so the list never loads
    /// milestone graphs and never issues one query per row.
    /// </summary>
    Task<InitiativeListPage> SearchAsync(InitiativeListQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Milestone weight/completion inputs for many initiatives in a single round trip, so a list
    /// of N initiatives costs one query instead of N.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<InitiativeProgressInput>>> GetProgressInputsAsync(
        IReadOnlyCollection<Guid> initiativeIds,
        CancellationToken cancellationToken);
}

/// <summary>Server-side filters for the initiative grid.</summary>
public sealed record InitiativeListQuery
{
    public string? Search { get; init; }

    public InitiativeStatus? Status { get; init; }

    public InitiativePriority? Priority { get; init; }

    public Guid? OwnerId { get; init; }

    public bool IncludeClosed { get; init; } = true;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;

    /// <summary>False for the legacy "return everything" list endpoint used by dashboards.</summary>
    public bool Paged { get; init; }
}

public sealed record InitiativeListRow(
    Guid Id,
    string Name,
    InitiativeStatus Status,
    InitiativePriority Priority,
    Guid OwnerId,
    string? OwnerName,
    Guid? GoalId,
    Guid? KraId,
    Guid? KpiId,
    decimal Budget,
    decimal ActualExpenditure,
    DateTime? StartDate,
    DateTime? EndDate,
    DateTime CreatedDate,
    int MilestoneCount,
    decimal TotalMilestoneWeight,
    decimal WeightedProgressTotal);

public sealed record InitiativeListPage(
    IReadOnlyList<InitiativeListRow> Items,
    int TotalCount,
    int Page,
    int PageSize);
