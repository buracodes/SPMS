using Application.CQRS.Division.Requests.Queries;
using Application.DTOS.Division;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Responses;
using MediatR;

namespace Application.CQRS.Division.Handlers.Queries
{
    public class GetDivisionPlansQueryHandler : IRequestHandler<GetDivisionPlansQueryRequest, Response<List<DivisionPlanSummaryDto>>>
    {
        private readonly IDivisionRepository divisionRepository;
        private readonly IDivisionPlanRepository divisionPlanRepository;
        private readonly IDivisionGoalRepository divisionGoalRepository;
        private readonly IKpiRepository kpiRepository;

        public GetDivisionPlansQueryHandler(
            IDivisionRepository _divisionRepository,
            IDivisionPlanRepository _divisionPlanRepository,
            IDivisionGoalRepository _divisionGoalRepository,
            IKpiRepository _kpiRepository)
        {
            divisionRepository = _divisionRepository;
            divisionPlanRepository = _divisionPlanRepository;
            divisionGoalRepository = _divisionGoalRepository;
            kpiRepository = _kpiRepository;
        }

        public async Task<Response<List<DivisionPlanSummaryDto>>> Handle(GetDivisionPlansQueryRequest request, CancellationToken cancellationToken)
        {
            var division = await divisionRepository.GetById(request.DivisionId);
            if (division == null)
            {
                throw new NotFoundException(nameof(Domain.Entities.Division), request.DivisionId);
            }

            var plans = await divisionPlanRepository.GetByDivisionAsync(request.DivisionId);
            var planIds = plans.Select(p => p.Id).ToList();

            var goalCounts = new Dictionary<Guid, int>();
            var kpiCounts = new Dictionary<Guid, int>();

            if (planIds.Count > 0)
            {
                var goals = await divisionGoalRepository.GetByPlanIdsAsync(planIds);

                goalCounts = goals.GroupBy(g => g.DivisionPlanId).ToDictionary(g => g.Key, g => g.Count());

                var goalIds = goals.Select(g => g.Id).ToList();
                var kpiByGoal = await kpiRepository.GetDivisionKpiCountsByGoalAsync(goalIds);

                kpiCounts = goals
                    .GroupBy(g => g.DivisionPlanId)
                    .ToDictionary(g => g.Key, g => g.Sum(x => kpiByGoal.GetValueOrDefault(x.Id)));
            }

            var data = plans.Select(p => new DivisionPlanSummaryDto
            {
                Id = p.Id,
                DivisionId = p.DivisionId,
                ScorecardId = p.ScorecardId,
                ScorecardName = p.Scorecard?.Name ?? string.Empty,
                FiscalYearId = p.Scorecard?.FiscalYearId ?? Guid.Empty,
                PlanStatus = p.PlanStatus,
                GoalCount = goalCounts.GetValueOrDefault(p.Id),
                KpiCount = kpiCounts.GetValueOrDefault(p.Id)
            }).ToList();

            return new Response<List<DivisionPlanSummaryDto>>
            {
                Success = true,
                Message = "Division plans retrieved successfully.",
                Data = data
            };
        }
    }
}
