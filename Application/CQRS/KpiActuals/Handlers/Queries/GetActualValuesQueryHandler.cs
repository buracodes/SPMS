using Application.CQRS.KpiActuals.Requests.Queries;
using Application.DTOS.KpiActual;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using Application.Services;
using Domain.Entities;
using MediatR;

namespace Application.CQRS.KpiActuals.Handlers.Queries
{
    public class GetActualValuesQueryHandler : IRequestHandler<GetActualValuesQueryRequest, Response<GetActualValuesDto>>
    {
        private readonly IKpiRepository kpiRepository;
        private readonly IFiscalYearRepository fiscalYearRepository;
        private readonly IFiscalPeriodRepository fiscalPeriodRepository;
        private readonly IKpiActualValueRepository kpiActualValueRepository;
        private readonly IUserRepository userRepository;
        private readonly ICurrentUserService currentUserService;
        private readonly IOrganizationalScopeService organizationalScope;

        public GetActualValuesQueryHandler(
            IKpiRepository _kpiRepository,
            IFiscalYearRepository _fiscalYearRepository,
            IFiscalPeriodRepository _fiscalPeriodRepository,
            IKpiActualValueRepository _kpiActualValueRepository,
            IUserRepository _userRepository,
            ICurrentUserService _currentUserService,
            IOrganizationalScopeService _organizationalScope)
        {
            kpiRepository = _kpiRepository;
            fiscalYearRepository = _fiscalYearRepository;
            fiscalPeriodRepository = _fiscalPeriodRepository;
            kpiActualValueRepository = _kpiActualValueRepository;
            userRepository = _userRepository;
            currentUserService = _currentUserService;
            organizationalScope = _organizationalScope;
        }

        public async Task<Response<GetActualValuesDto>> Handle(GetActualValuesQueryRequest request, CancellationToken cancellationToken)
        {
            var kpi = await kpiRepository.GetById(request.KpiId);
            if (kpi == null)
            {
                throw new NotFoundException(nameof(Kpi), request.KpiId);
            }

            await KpiActualReadGuard.EnsureReadAllowedAsync(kpi, currentUserService, organizationalScope, cancellationToken);

            var fiscalYear = await fiscalYearRepository.GetById(request.FiscalYearId);
            if (fiscalYear == null)
            {
                throw new NotFoundException(nameof(FiscalYear), request.FiscalYearId);
            }

            var periods = await fiscalPeriodRepository.GetByFiscalYearAsync(request.FiscalYearId);
            var periodById = periods.ToDictionary(p => p.Id);

            // The repository returns only current revisions: exactly one per KPI + period,
            // regardless of source. No per-source folding here.
            var values = await kpiActualValueRepository.GetForFiscalYearAsync(request.KpiId, request.FiscalYearId);
            var names = ActualAttributionNames.Resolve(
                userRepository, values.Select(v => v.RecordedBy));

            var result = new GetActualValuesDto
            {
                KpiId = request.KpiId,
                FiscalYearId = request.FiscalYearId
            };

            foreach (var value in values)
            {
                var period = periodById.GetValueOrDefault(value.FiscalPeriodId);
                result.Values.Add(new ActualValueDto
                {
                    Id = value.Id,
                    KpiId = value.KpiId,
                    FiscalPeriodId = value.FiscalPeriodId,
                    FiscalMonth = period?.FiscalMonth ?? 0,
                    Quarter = period?.Quarter ?? 0,
                    Value = value.Value,
                    DataSource = value.DataSource,
                    AsOfDate = value.AsOfDate,
                    Note = value.Note,
                    RecordedBy = value.RecordedBy,
                    EnteredByName = value.RecordedBy is Guid id && names.TryGetValue(id, out var name) ? name : null,
                    CreatedDate = value.CreatedDate
                });
            }

            return new Response<GetActualValuesDto>
            {
                Success = true,
                Message = "Actual values retrieved successfully.",
                Data = result
            };
        }
    }
}
