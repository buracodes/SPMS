using Application.Interface.IRepository;
using Domain.Entities;
using Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class KpiRepository : GenericRepository<Kpi>, IKpiRepository
{
    public KpiRepository(SpmsDbContext context) : base(context)
    {
    }

    public async Task<Kpi?> GetByIdWithScorecardAsync(Guid id)
    {
        return await context.Kpis
            .Include(k => k.Scorecard)
            .ThenInclude(s => s.FiscalYear)
            .Include(k => k.DivisionOwner)
            .FirstOrDefaultAsync(k => k.Id == id);
    }

    public async Task<IReadOnlyList<Kpi>> GetAllKpisWithDetailsAsync()
    {
        return await context.Kpis
            .AsNoTracking()
            .Include(k => k.Owner)
            .Include(k => k.DivisionOwner)
            .Include(k => k.UnitOfMeasure)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Kpi>> GetByScorecardAsync(Guid scorecardId)
    {
        return await context.Kpis
            .AsNoTracking()
            .Include(k => k.Kra)
            .Where(k => k.ScorecardId == scorecardId)
            .OrderBy(k => k.Name)
            .ToListAsync();
    }

    public async Task<bool> ExistsUnderDivisionGoalAsync(Guid divisionGoalId, string name)
    {
        return await context.Kpis.AnyAsync(k => k.DivisionGoalId == divisionGoalId && k.Name == name);
    }

    public async Task<decimal> GetDivisionKpiWeightsSumAsync(Guid divisionGoalId, Guid? excludeKpiId = null)
    {
        var query = context.Kpis.Where(k => k.DivisionGoalId == divisionGoalId);
        if (excludeKpiId.HasValue)
        {
            query = query.Where(k => k.Id != excludeKpiId.Value);
        }
        return await query.SumAsync(k => k.Weight);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetDivisionKpiCountsByGoalAsync(IReadOnlyList<Guid> divisionGoalIds)
    {
        var rows = await context.Kpis
            .Where(k => k.DivisionGoalId != null && divisionGoalIds.Contains(k.DivisionGoalId!.Value))
            .GroupBy(k => k.DivisionGoalId!.Value)
            .Select(g => new { DivisionGoalId = g.Key, Count = g.Count() })
            .ToListAsync();

        return rows.ToDictionary(r => r.DivisionGoalId, r => r.Count);
    }
}
