using Application.CQRS.Division.Requests.Queries;
using Application.CQRS.KpiActuals;
using Application.DTOS.KpiActual;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using Domain.Entities;
using MediatR;

namespace Application.CQRS.Division.Handlers.Queries;

public class GetDivisionActualValueHistoryQueryHandler : IRequestHandler<GetDivisionActualValueHistoryQueryRequest, Response<ActualValueHistoryDto>>
{
    private readonly IDivisionRepository divisionRepository;
    private readonly IDivisionPlanRepository divisionPlanRepository;
    private readonly IDivisionGoalRepository divisionGoalRepository;
    private readonly IKpiRepository kpiRepository;
    private readonly IFiscalPeriodRepository fiscalPeriodRepository;
    private readonly IKpiActualValueRepository kpiActualValueRepository;
    private readonly IUserRepository userRepository;
    private readonly IAuditLogEntryRepository auditLogRepository;
    private readonly IOrganizationalScopeService organizationalScope;

    public GetDivisionActualValueHistoryQueryHandler(
        IDivisionRepository _divisionRepository,
        IDivisionPlanRepository _divisionPlanRepository,
        IDivisionGoalRepository _divisionGoalRepository,
        IKpiRepository _kpiRepository,
        IFiscalPeriodRepository _fiscalPeriodRepository,
        IKpiActualValueRepository _kpiActualValueRepository,
        IUserRepository _userRepository,
        IAuditLogEntryRepository _auditLogRepository,
        IOrganizationalScopeService _organizationalScope)
    {
        divisionRepository = _divisionRepository;
        divisionPlanRepository = _divisionPlanRepository;
        divisionGoalRepository = _divisionGoalRepository;
        kpiRepository = _kpiRepository;
        fiscalPeriodRepository = _fiscalPeriodRepository;
        kpiActualValueRepository = _kpiActualValueRepository;
        userRepository = _userRepository;
        auditLogRepository = _auditLogRepository;
        organizationalScope = _organizationalScope;
    }

    public async Task<Response<ActualValueHistoryDto>> Handle(
        GetDivisionActualValueHistoryQueryRequest request,
        CancellationToken cancellationToken)
    {
        await organizationalScope.EnsureDivisionAsync(request.DivisionId, cancellationToken);

        var division = await divisionRepository.GetById(request.DivisionId);
        if (division == null)
        {
            throw new NotFoundException(nameof(Domain.Entities.Division), request.DivisionId);
        }

        var kpi = await kpiRepository.GetByIdWithScorecardAsync(request.KpiId);
        if (kpi == null
            || kpi.DivisionGoalId == null
            || kpi.ScorecardId != request.ScorecardId
            || kpi.Scorecard == null)
        {
            // Corporate KPIs and KPIs from another scorecard never have division history.
            throw new NotFoundException(nameof(Kpi), request.KpiId);
        }

        var plan = await divisionPlanRepository.GetByDivisionAndScorecardAsync(
            request.DivisionId,
            request.ScorecardId);
        if (plan == null)
        {
            throw new NotFoundException(nameof(DivisionPlan), request.ScorecardId);
        }

        var divisionGoal = await divisionGoalRepository.GetById(kpi.DivisionGoalId.Value);
        if (divisionGoal == null || divisionGoal.DivisionPlanId != plan.Id)
        {
            // A KPI under a sibling division's plan must not leak through a shared scorecard.
            throw new NotFoundException(nameof(Kpi), request.KpiId);
        }

        var period = await fiscalPeriodRepository.GetById(request.FiscalPeriodId);
        if (period == null)
        {
            throw new NotFoundException(nameof(FiscalPeriod), request.FiscalPeriodId);
        }

        if (kpi.Scorecard.FiscalYearId != period.FiscalYearId)
        {
            throw new BadRequestException("The fiscal period does not belong to the KPI's scorecard.");
        }

        var values = await kpiActualValueRepository.GetByKpiAndPeriodAsync(
            request.KpiId,
            request.FiscalPeriodId);
        var chain = ActualRevisionHistory.Build(values);

        var history = await ActualHistoryProjection.BuildAsync(
            auditLogRepository,
            userRepository,
            request.ScorecardId,
            kpi,
            period,
            chain,
            cancellationToken);

        return new Response<ActualValueHistoryDto>
        {
            Success = true,
            Message = "Division actual value history retrieved successfully.",
            Data = history
        };
    }
}
