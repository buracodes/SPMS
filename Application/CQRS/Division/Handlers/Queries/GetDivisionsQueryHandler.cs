using Application.CQRS.Division.Requests.Queries;
using Application.DTOS.Division;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Division.Handlers.Queries
{
    public class GetDivisionsQueryHandler : IRequestHandler<GetDivisionsQueryRequest, Response<List<DivisionDto>>>
    {
        private readonly IDivisionRepository divisionRepository;
        private readonly IDivisionPlanRepository divisionPlanRepository;
        private readonly IOrganizationalScopeService organizationalScope;

        public GetDivisionsQueryHandler(
            IDivisionRepository _divisionRepository,
            IDivisionPlanRepository _divisionPlanRepository,
            IOrganizationalScopeService _organizationalScope)
        {
            divisionRepository = _divisionRepository;
            divisionPlanRepository = _divisionPlanRepository;
            organizationalScope = _organizationalScope;
        }

        public async Task<Response<List<DivisionDto>>> Handle(GetDivisionsQueryRequest request, CancellationToken cancellationToken)
        {
            var divisions = await divisionRepository.GetActiveAsync();

            // Division accounts only discover their own division. Corporate accounts keep the
            // full list, including an admin or power user whose HR record also carries a division
            // claim: they assign KPI owners from the corporate scorecard, so every active
            // division has to be selectable. Portfolio or corporate-wide visibility is resolved
            // once from the live authorization state rather than per division.
            var canBrowsePortfolio = await organizationalScope.HasDivisionPortfolioAccessAsync(cancellationToken)
                || await organizationalScope.HasCorporateAccessAsync(cancellationToken);
            if (!canBrowsePortfolio)
            {
                var authorizedDivisionIds = organizationalScope.DivisionIds;
                divisions = authorizedDivisionIds.Count > 0
                    ? divisions.Where(d => authorizedDivisionIds.Contains(d.Id)).ToList()
                    : new List<Domain.Entities.Division>();
            }
            var divisionIds = divisions.Select(d => d.Id).ToList();

            var plans = divisionIds.Count > 0
                ? await divisionPlanRepository.GetByDivisionIdsAsync(divisionIds)
                : new List<Domain.Entities.DivisionPlan>();

            var latestStatusByDivision = plans
                .GroupBy(p => p.DivisionId)
                .ToDictionary(g => g.Key, g => g.Last().PlanStatus);

            var data = divisions.Select(d => new DivisionDto
            {
                Id = d.Id,
                Name = d.Name,
                Code = d.Code,
                IsActive = d.IsActive,
                LatestPlanStatus = latestStatusByDivision.GetValueOrDefault(d.Id)
            }).ToList();

            return new Response<List<DivisionDto>>
            {
                Success = true,
                Message = "Divisions retrieved successfully.",
                Data = data
            };
        }
    }
}
