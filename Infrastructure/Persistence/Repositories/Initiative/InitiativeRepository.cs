using Application.Interface.IRepository;
using Domain.Calculation;
using Domain.Enums;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class InitiativeRepository : GenericRepository<Initiative>, IInitiativeRepository
{
    public InitiativeRepository(SpmsDbContext context) : base(context)
    {
    }

    public async Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null)
    {
        return await context.Initiatives
            .Where(i => i.Name == name
                && i.Id != excludeId)
            .AnyAsync();
    }

    public async Task<Initiative?> GetByIdWithMilestonesAsync(Guid id)
    {
        return await context.Initiatives
            .Include(i => i.Milestones)
            .FirstOrDefaultAsync(i => i.Id == id);
    }

    public async Task<Initiative?> GetDetailAsync(Guid id)
    {
        return await context.Initiatives
            .AsNoTracking()
            .Include(i => i.Milestones)
            .Include(i => i.Owner)
            .Include(i => i.Goal)
            .Include(i => i.Kra)
            .Include(i => i.Kpi)
            .Include(i => i.Dependencies).ThenInclude(d => d.DependsOnInitiative)
            .FirstOrDefaultAsync(i => i.Id == id);
    }

    public async Task<IReadOnlyList<Initiative>> GetByOwnerIdAsync(Guid ownerId)
    {
        return await context.Initiatives
            .Where(i => i.OwnerId == ownerId)
            .OrderByDescending(i => i.CreatedDate)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Initiative>> GetAllWithMilestonesAsync()
    {
        return await context.Initiatives
            .Include(i => i.Milestones)
            .OrderByDescending(i => i.CreatedDate)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Initiative>> GetByOwnerIdWithMilestonesAsync(Guid ownerId)
    {
        return await context.Initiatives
            .Include(i => i.Milestones)
            .Where(i => i.OwnerId == ownerId)
            .OrderByDescending(i => i.CreatedDate)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Initiative>> GetByStatusAsync(InitiativeStatus status)
    {
        return await context.Initiatives
            .Where(i => i.Status == status)
            .OrderByDescending(i => i.CreatedDate)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Initiative>> GetByPriorityAsync(InitiativePriority priority)
    {
        return await context.Initiatives
            .Where(i => i.Priority == priority)
            .OrderByDescending(i => i.CreatedDate)
            .ToListAsync();
    }

    public async Task<InitiativeListPage> SearchAsync(InitiativeListQuery query, CancellationToken cancellationToken)
    {
        var initiatives = context.Initiatives.AsNoTracking();

        if (!query.IncludeClosed)
        {
            initiatives = initiatives.Where(i => !i.IsClosed);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            initiatives = initiatives.Where(i =>
                EF.Functions.Like(i.Name, $"%{search}%")
                || (i.Description != null && EF.Functions.Like(i.Description, $"%{search}%")));
        }

        if (query.Status.HasValue)
        {
            initiatives = initiatives.Where(i => i.Status == query.Status.Value);
        }

        if (query.Priority.HasValue)
        {
            initiatives = initiatives.Where(i => i.Priority == query.Priority.Value);
        }

        if (query.OwnerId.HasValue)
        {
            initiatives = initiatives.Where(i => i.OwnerId == query.OwnerId.Value);
        }

        var total = await initiatives.CountAsync(cancellationToken);

        if (query.Paged)
        {
            var page = query.Page < 1 ? 1 : query.Page;
            var pageSize = query.PageSize is < 1 or > 200 ? 25 : query.PageSize;
            initiatives = initiatives
                .OrderByDescending(i => i.CreatedDate)
                .ThenBy(i => i.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize);
        }
        else
        {
            initiatives = initiatives
                .OrderByDescending(i => i.CreatedDate)
                .ThenBy(i => i.Name);
        }

        // Single grouped sub-query for the milestone aggregates: one round trip regardless of
        // how many initiatives are on the page, instead of one query per row.
        var aggregates = await context.Milestones
            .AsNoTracking()
            .GroupBy(m => m.InitiativeId)
            .Select(g => new
            {
                InitiativeId = g.Key,
                MilestoneCount = g.Count(),
                TotalWeight = g.Sum(m => m.Weight),
                WeightedProgressTotal = g.Sum(m => m.Weight * m.CompletionPercentage)
            })
            .ToDictionaryAsync(
                a => a.InitiativeId,
                a => (MilestoneCount: a.MilestoneCount, TotalWeight: a.TotalWeight, WeightedProgress: a.WeightedProgressTotal),
                cancellationToken);

        var rows = await initiatives
            .Select(i => new
            {
                i.Id,
                i.Name,
                i.Status,
                i.Priority,
                i.OwnerId,
                OwnerName = i.Owner != null ? i.Owner.FullName : null,
                i.GoalId,
                i.KraId,
                i.KpiId,
                i.Budget,
                i.ActualExpenditure,
                i.StartDate,
                i.EndDate,
                i.CreatedDate
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r =>
            {
                aggregates.TryGetValue(r.Id, out var aggregate);
                return new InitiativeListRow(
                    r.Id,
                    r.Name,
                    r.Status,
                    r.Priority,
                    r.OwnerId,
                    r.OwnerName,
                    r.GoalId,
                    r.KraId,
                    r.KpiId,
                    r.Budget,
                    r.ActualExpenditure,
                    r.StartDate,
                    r.EndDate,
                    r.CreatedDate,
                    aggregate.MilestoneCount,
                    aggregate.TotalWeight,
                    aggregate.WeightedProgress);
            })
            .ToList();

        return new InitiativeListPage(items, total, query.Page, query.PageSize);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<InitiativeProgressInput>>> GetProgressInputsAsync(
        IReadOnlyCollection<Guid> initiativeIds,
        CancellationToken cancellationToken)
    {
        if (initiativeIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<InitiativeProgressInput>>();
        }

        var rows = await context.Milestones
            .AsNoTracking()
            .Where(m => initiativeIds.Contains(m.InitiativeId))
            .Select(m => new { m.InitiativeId, m.Id, m.Weight, m.CompletionPercentage })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.InitiativeId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<InitiativeProgressInput>)g
                    .Select(m => new InitiativeProgressInput(m.Id, m.Weight, m.CompletionPercentage))
                    .ToList());
    }
}
