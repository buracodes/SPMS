using Application.CQRS.Division.Requests.Queries;
using Application.DTOS.Division;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using Application.Services;
using Domain.Entities;
using MediatR;

namespace Application.CQRS.Division.Handlers.Queries;

public class GetDivisionActualValuesQueryHandler : IRequestHandler<GetDivisionActualValuesQueryRequest, Response<IReadOnlyList<DivisionActualValueDto>>>
{
    private readonly IDivisionRepository divisionRepository;
    private readonly IDivisionPlanRepository divisionPlanRepository;
    private readonly IDivisionGoalRepository divisionGoalRepository;
    private readonly IKpiRepository kpiRepository;
    private readonly IKpiActualValueRepository kpiActualValueRepository;
    private readonly IUserRepository userRepository;
    private readonly IOrganizationalScopeService organizationalScope;

    public GetDivisionActualValuesQueryHandler(
        IDivisionRepository _divisionRepository,
        IDivisionPlanRepository _divisionPlanRepository,
        IDivisionGoalRepository _divisionGoalRepository,
        IKpiRepository _kpiRepository,
        IKpiActualValueRepository _kpiActualValueRepository,
        IUserRepository _userRepository,
        IOrganizationalScopeService _organizationalScope)
    {
        divisionRepository = _divisionRepository;
        divisionPlanRepository = _divisionPlanRepository;
        divisionGoalRepository = _divisionGoalRepository;
        kpiRepository = _kpiRepository;
        kpiActualValueRepository = _kpiActualValueRepository;
        userRepository = _userRepository;
        organizationalScope = _organizationalScope;
    }

    public async Task<Response<IReadOnlyList<DivisionActualValueDto>>> Handle(
        GetDivisionActualValuesQueryRequest request,
        CancellationToken cancellationToken)
    {
        var divisionId = request.DivisionId;
        await organizationalScope.EnsureDivisionAsync(divisionId, cancellationToken);

        var division = await divisionRepository.GetById(divisionId);
        if (division == null)
        {
            throw new NotFoundException(nameof(Domain.Entities.Division), divisionId);
        }

        // FR-906: scope the KPI set to this division's plans only. Without this the
        // query returned every division KPI on every scorecard, leaking sibling
        // divisions' KPIs into each other's actual-value view.
        var plans = await divisionPlanRepository.GetByDivisionAsync(divisionId);
        var planIds = plans.Select(p => p.Id).ToList();
        var goals = await divisionGoalRepository.GetByPlanIdsAsync(planIds);
        var goalIds = goals.Select(g => g.Id).ToHashSet();

        var divisionKpis = kpiRepository.GetQueryable()
            .Where(k => k.DivisionGoalId != null && goalIds.Contains(k.DivisionGoalId.Value) && !k.IsDeleted)
            .ToList();

        // The repository returns only current revisions: one effective value per
        // KPI + fiscal period across all sources, matching corporate reads and scores.
        var currentActuals = new List<KpiActualValue>();
        foreach (var kpi in divisionKpis)
        {
            currentActuals.AddRange(
                await kpiActualValueRepository.GetForFiscalYearAsync(kpi.Id, request.FiscalYearId));
        }

        var names = ActualAttributionNames.Resolve(
            userRepository, currentActuals.Select(a => a.RecordedBy));

        var result = new List<DivisionActualValueDto>(currentActuals.Count);
        foreach (var actual in currentActuals)
        {
            result.Add(new DivisionActualValueDto
            {
                Id = actual.Id,
                DivisionId = divisionId,
                KpiId = actual.KpiId,
                FiscalPeriodId = actual.FiscalPeriodId,
                FiscalMonth = actual.FiscalPeriod?.FiscalMonth ?? 0,
                Quarter = actual.FiscalPeriod?.Quarter ?? 0,
                Value = actual.Value,
                DataSource = actual.DataSource,
                AsOfDate = actual.AsOfDate,
                Note = actual.Note,
                RecordedBy = actual.RecordedBy,
                EnteredByName = actual.RecordedBy is Guid id && names.TryGetValue(id, out var name) ? name : null,
                CreatedDate = actual.CreatedDate
            });
        }

        return new Response<IReadOnlyList<DivisionActualValueDto>>
        {
            Success = true,
            Message = "Division actual values retrieved successfully.",
            Data = result.AsReadOnly()
        };
    }
}
