using Application.CQRS.Kpi.Requests.Queries;
using Application.DTOS.Kpi;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using MediatR;
using KpiEntity = Domain.Entities.Kpi;

namespace Application.CQRS.Kpi.Handlers.Queries;

public class GetKpiByIdQueryHandler : IRequestHandler<GetKpiByIdQueryRequest, Response<KpiDto>>
{
    private readonly IKpiRepository kpiRepository;
    private readonly IOrganizationalScopeService organizationalScope;

    public GetKpiByIdQueryHandler(
        IKpiRepository _kpiRepository,
        IOrganizationalScopeService _organizationalScope)
    {
        kpiRepository = _kpiRepository;
        organizationalScope = _organizationalScope;
    }

    public async Task<Response<KpiDto>> Handle(GetKpiByIdQueryRequest request, CancellationToken cancellationToken)
    {
        var kpi = await kpiRepository.GetByIdWithScorecardAsync(request.Id)
            ?? throw new NotFoundException(nameof(KpiEntity), request.Id);

        // Same precedence as the KPI list: live corporate visibility opens any KPI, so the
        // corporate scorecard's owners can be viewed and changed by an admin or power user even
        // when their HR record also carries a division claim. Without it a division account
        // stays limited to the KPIs it owns directly or through its division.
        if (!await organizationalScope.HasCorporateAccessAsync(cancellationToken) &&
            !await organizationalScope.IsAssignedKpiAsync(kpi.OwnerId, kpi.DivisionOwnerId, cancellationToken))
        {
            throw new ForbiddenException("You do not have access to this KPI.");
        }

        var childKpiCount = kpiRepository.GetQueryable().Count(k => k.ParentKpiId == kpi.Id);

        return new Response<KpiDto>
        {
            Success = true,
            Message = "KPI retrieved successfully.",
            Data = new KpiDto
            {
                Id = kpi.Id,
                ScorecardId = kpi.ScorecardId,
                KraId = kpi.KraId,
                DivisionGoalId = kpi.DivisionGoalId,
                OwnerId = kpi.OwnerId,
                OwnerName = kpi.Owner?.FullName,
                DivisionOwnerId = kpi.DivisionOwnerId,
                DivisionOwnerName = kpi.DivisionOwner?.Name,
                Name = kpi.Name,
                Description = kpi.Description,
                Unit = kpi.Unit,
                Weight = kpi.Weight,
                ToleranceBandPercent = kpi.ToleranceBandPercent,
                OptimizationType = kpi.OptimizationType,
                DataSource = kpi.DataSource,
                KpiType = kpi.KpiType,
                IsCumulative = kpi.IsCumulative,
                ParentKpiId = kpi.ParentKpiId,
                Baseline = kpi.Baseline,
                MinValue = kpi.MinValue,
                MaxValue = kpi.MaxValue,
                Formula = kpi.Formula,
                IsActive = kpi.IsActive,
                ChildKpiCount = childKpiCount,
                CreatedBy = kpi.CreatedBy,
                CreatedDate = kpi.CreatedDate,
                UpdatedBy = kpi.UpdatedBy,
                UpdatedDate = kpi.UpdatedDate,
                Status = kpi.Status
            }
        };
    }
}
