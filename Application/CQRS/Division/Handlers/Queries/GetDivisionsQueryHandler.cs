using Application.CQRS.Division.Requests.Queries;
using Application.DTOS.Division;
using Application.Interface.IRepository;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Division.Handlers.Queries
{
    public class GetDivisionsQueryHandler : IRequestHandler<GetDivisionsQueryRequest, Response<List<DivisionDto>>>
    {
        private readonly IDivisionRepository divisionRepository;
        private readonly IDivisionPlanRepository divisionPlanRepository;

        public GetDivisionsQueryHandler(IDivisionRepository _divisionRepository, IDivisionPlanRepository _divisionPlanRepository)
        {
            divisionRepository = _divisionRepository;
            divisionPlanRepository = _divisionPlanRepository;
        }

        public async Task<Response<List<DivisionDto>>> Handle(GetDivisionsQueryRequest request, CancellationToken cancellationToken)
        {
            var divisions = await divisionRepository.GetActiveAsync();
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
