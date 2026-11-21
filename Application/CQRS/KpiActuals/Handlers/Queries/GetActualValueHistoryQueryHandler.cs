using Application.CQRS.KpiActuals.Requests.Queries;
using Application.DTOS.KpiActual;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using Domain.Entities;
using MediatR;

namespace Application.CQRS.KpiActuals.Handlers.Queries
{
    public class GetActualValueHistoryQueryHandler : IRequestHandler<GetActualValueHistoryQueryRequest, Response<ActualValueHistoryDto>>
    {
        private readonly IKpiRepository kpiRepository;
        private readonly IFiscalPeriodRepository fiscalPeriodRepository;
        private readonly IKpiActualValueRepository kpiActualValueRepository;
        private readonly IUserRepository userRepository;
        private readonly IAuditLogEntryRepository auditLogRepository;
        private readonly ICurrentUserService currentUserService;
        private readonly IOrganizationalScopeService organizationalScope;

        public GetActualValueHistoryQueryHandler(
            IKpiRepository _kpiRepository,
            IFiscalPeriodRepository _fiscalPeriodRepository,
            IKpiActualValueRepository _kpiActualValueRepository,
            IUserRepository _userRepository,
            IAuditLogEntryRepository _auditLogRepository,
            ICurrentUserService _currentUserService,
            IOrganizationalScopeService _organizationalScope)
        {
            kpiRepository = _kpiRepository;
            fiscalPeriodRepository = _fiscalPeriodRepository;
            kpiActualValueRepository = _kpiActualValueRepository;
            userRepository = _userRepository;
            auditLogRepository = _auditLogRepository;
            currentUserService = _currentUserService;
            organizationalScope = _organizationalScope;
        }

        public async Task<Response<ActualValueHistoryDto>> Handle(GetActualValueHistoryQueryRequest request, CancellationToken cancellationToken)
        {
            var kpi = await kpiRepository.GetByIdWithScorecardAsync(request.KpiId);
            if (kpi == null
                || kpi.ScorecardId != request.ScorecardId
                || kpi.Scorecard == null
                || kpi.DivisionGoalId.HasValue)
            {
                // One generic not-found response prevents probing another scorecard's KPI or
                // using a division KPI id on the corporate endpoint.
                throw new NotFoundException(nameof(Kpi), request.KpiId);
            }

            await KpiActualReadGuard.EnsureReadAllowedAsync(kpi, currentUserService, organizationalScope, cancellationToken);

            var period = await fiscalPeriodRepository.GetById(request.FiscalPeriodId);
            if (period == null)
            {
                throw new NotFoundException(nameof(FiscalPeriod), request.FiscalPeriodId);
            }

            if (kpi.Scorecard.FiscalYearId != period.FiscalYearId)
            {
                throw new BadRequestException("The fiscal period does not belong to the KPI's scorecard.");
            }

            // All revisions, including superseded ones: IsCurrent is deliberately not
            // filtered here. Supersession never sets IsDeleted, so no filter opt-out is
            // needed for an authorized audit-history view.
            var values = await kpiActualValueRepository.GetByKpiAndPeriodAsync(request.KpiId, request.FiscalPeriodId);
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
                Message = "Actual value history retrieved successfully.",
                Data = history
            };
        }
    }
}
