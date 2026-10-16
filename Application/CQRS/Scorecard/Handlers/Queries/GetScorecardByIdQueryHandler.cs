using Application.CQRS.Scorecard.Requests.Queries;
using Application.DTOS.Scorecard;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Responses;
using MediatR;
using ScorecardEntity = Domain.Entities.Scorecard;

namespace Application.CQRS.Scorecard.Handlers.Queries;

public class GetScorecardByIdQueryHandler : IRequestHandler<GetScorecardByIdQueryRequest, Response<ScorecardDto>>
{
    private readonly IScorecardRepository scorecardRepository;
    private readonly IGoalRepository goalRepository;
    private readonly IKraRepository kraRepository;
    private readonly IKpiRepository kpiRepository;

    public GetScorecardByIdQueryHandler(
        IScorecardRepository _scorecardRepository,
        IGoalRepository _goalRepository,
        IKraRepository _kraRepository,
        IKpiRepository _kpiRepository)
    {
        scorecardRepository = _scorecardRepository;
        goalRepository = _goalRepository;
        kraRepository = _kraRepository;
        kpiRepository = _kpiRepository;
    }

    public async Task<Response<ScorecardDto>> Handle(GetScorecardByIdQueryRequest request, CancellationToken cancellationToken)
    {
        var scorecard = await scorecardRepository.GetById(request.Id)
            ?? throw new NotFoundException(nameof(ScorecardEntity), request.Id);

        var goals = await goalRepository.GetAll();
        var kras = await kraRepository.GetAll();
        var kpis = await kpiRepository.GetAll();

        var goalCount = goals.Count(g => g.ScorecardId == scorecard.Id);
        var kraCount = kras.Count(k => k.ScorecardId == scorecard.Id);
        var kpiCount = kpis.Count(k => k.ScorecardId == scorecard.Id);

        return new Response<ScorecardDto>
        {
            Success = true,
            Message = "Scorecard retrieved successfully.",
            Data = new ScorecardDto
            {
                Id = scorecard.Id,
                FiscalYearId = scorecard.FiscalYearId,
                Name = scorecard.Name,
                Description = scorecard.Description,
                Scope = scorecard.Scope,
                ScorecardStatus = scorecard.ScorecardStatus,
                GoalCount = goalCount,
                KraCount = kraCount,
                KpiCount = kpiCount,
                CreatedDate = scorecard.CreatedDate
            }
        };
    }
}
