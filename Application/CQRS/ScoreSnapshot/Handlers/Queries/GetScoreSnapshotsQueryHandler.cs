using Application.CQRS.ScoreSnapshot.Requests.Queries;
using Application.DTOS.ScoreSnapshot;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using Domain.Enums;
using MediatR;

namespace Application.CQRS.ScoreSnapshot.Handlers.Queries
{
    public class GetScoreSnapshotsQueryHandler
        : IRequestHandler<GetScoreSnapshotsQueryRequest, Response<GetScoreSnapshotsDto>>
    {
        private readonly IScorecardRepository _scorecardRepository;
        private readonly IScorePeriodCalculationService _scorePeriodCalculationService;

        public GetScoreSnapshotsQueryHandler(
            IScorecardRepository scorecardRepository,
            IScorePeriodCalculationService scorePeriodCalculationService)
        {
            _scorecardRepository = scorecardRepository;
            _scorePeriodCalculationService = scorePeriodCalculationService;
        }

        public async Task<Response<GetScoreSnapshotsDto>> Handle(
            GetScoreSnapshotsQueryRequest request, CancellationToken cancellationToken)
        {
            var scorecard = await _scorecardRepository.GetById(request.ScorecardId);
            if (scorecard == null)
                throw new NotFoundException(nameof(Scorecard), request.ScorecardId);

            if (scorecard.FiscalYearId != request.FiscalYearId)
                throw new BadRequestException(
                    "The requested fiscal year does not match the scorecard's fiscal year.");

            var result = await _scorePeriodCalculationService.CalculateAsync(
                request.ScorecardId,
                request.FiscalYearId,
                request.PeriodType,
                request.Quarter,
                request.FromMonth,
                request.ToMonth,
                request.Year,
                cancellationToken);

            var now = DateTime.UtcNow;
            var kpiSnapshots = BuildKpiSnapshots(result, now);
            var kraSnapshots = BuildEntitySnapshots(result.Kras, result, now);
            var goalSnapshots = BuildEntitySnapshots(result.Goals, result, now);
            var scorecardSnapshots = BuildScorecardSnapshots(result, now);

            return new Response<GetScoreSnapshotsDto>
            {
                Success = true,
                Message = "Score snapshots retrieved successfully.",
                Data = new GetScoreSnapshotsDto
                {
                    ScorecardId = request.ScorecardId,
                    FiscalYearId = request.FiscalYearId,
                    KpiSnapshots = kpiSnapshots,
                    KraSnapshots = kraSnapshots,
                    GoalSnapshots = goalSnapshots,
                    ScorecardSnapshots = scorecardSnapshots
                }
            };
        }

        private List<ScoreSnapshotDto> BuildKpiSnapshots(ScorePeriodResult result, DateTime now)
        {
            var list = new List<ScoreSnapshotDto>();
            foreach (var kv in result.Kpis)
            {
                var kpiId = kv.Key;
                var per = kv.Value;

                // Aggregated period entry
                if (result.FiscalPeriodByMonth.TryGetValue(result.Scope.ToMonth, out var fpId))
                {
                    list.Add(new ScoreSnapshotDto
                    {
                        EntityId = kpiId,
                        FiscalPeriodId = fpId,
                        FiscalMonth = result.Scope.ToMonth,
                        Quarter = ((result.Scope.ToMonth - 1) / 3) + 1,
                        Granularity = result.Scope.Granularity,
                        IsPeriodAggregate = true,
                        Score = per.PeriodScore,
                        ActualValue = per.PeriodActual,
                        TargetValue = per.PeriodTarget,
                        CalculatedAt = now
                    });
                }

                // Monthly entries within scope
                for (int m = result.Scope.FromMonth; m <= result.Scope.ToMonth; m++)
                {
                    per.MonthlyScores.TryGetValue(m, out var monthScore);
                    per.MonthlyTargets.TryGetValue(m, out var monthTarget);
                    per.MonthlyActuals.TryGetValue(m, out var monthActual);

                    if (monthScore == null && monthTarget == null && monthActual == null)
                        continue;

                    result.FiscalPeriodByMonth.TryGetValue(m, out var monthFpId);

                    list.Add(new ScoreSnapshotDto
                    {
                        EntityId = kpiId,
                        FiscalPeriodId = monthFpId,
                        FiscalMonth = m,
                        Quarter = ((m - 1) / 3) + 1,
                        Granularity = SnapshotGranularity.Month,
                        IsPeriodAggregate = false,
                        Score = monthScore,
                        ActualValue = monthActual,
                        TargetValue = monthTarget,
                        CalculatedAt = now
                    });
                }
            }
            return list;
        }

        private List<ScoreSnapshotDto> BuildEntitySnapshots(
            IReadOnlyDictionary<Guid, EntityScorePeriod> entities,
            ScorePeriodResult result,
            DateTime now)
        {
            var list = new List<ScoreSnapshotDto>();
            foreach (var kv in entities)
            {
                var entityId = kv.Key;
                var per = kv.Value;

                // Aggregated period entry
                if (result.FiscalPeriodByMonth.TryGetValue(result.Scope.ToMonth, out var fpId))
                {
                    list.Add(new ScoreSnapshotDto
                    {
                        EntityId = entityId,
                        FiscalPeriodId = fpId,
                        FiscalMonth = result.Scope.ToMonth,
                        Quarter = ((result.Scope.ToMonth - 1) / 3) + 1,
                        Granularity = result.Scope.Granularity,
                        IsPeriodAggregate = true,
                        Score = per.PeriodScore,
                        CalculatedAt = now
                    });
                }

                // Monthly entries
                for (int m = result.Scope.FromMonth; m <= result.Scope.ToMonth; m++)
                {
                    per.MonthlyScores.TryGetValue(m, out var monthScore);
                    if (monthScore == null) continue;

                    result.FiscalPeriodByMonth.TryGetValue(m, out var monthFpId);

                    list.Add(new ScoreSnapshotDto
                    {
                        EntityId = entityId,
                        FiscalPeriodId = monthFpId,
                        FiscalMonth = m,
                        Quarter = ((m - 1) / 3) + 1,
                        Granularity = SnapshotGranularity.Month,
                        IsPeriodAggregate = false,
                        Score = monthScore,
                        CalculatedAt = now
                    });
                }
            }
            return list;
        }

        private List<ScoreSnapshotDto> BuildScorecardSnapshots(ScorePeriodResult result, DateTime now)
        {
            var list = new List<ScoreSnapshotDto>();
            foreach (var kv in result.Scorecards)
            {
                var scorecardId = kv.Key;
                var per = kv.Value;

                if (result.FiscalPeriodByMonth.TryGetValue(result.Scope.ToMonth, out var fpId))
                {
                    list.Add(new ScoreSnapshotDto
                    {
                        EntityId = scorecardId,
                        FiscalPeriodId = fpId,
                        FiscalMonth = result.Scope.ToMonth,
                        Quarter = ((result.Scope.ToMonth - 1) / 3) + 1,
                        Granularity = result.Scope.Granularity,
                        IsPeriodAggregate = true,
                        Score = per.PeriodScore,
                        CalculatedAt = now
                    });
                }

                for (int m = result.Scope.FromMonth; m <= result.Scope.ToMonth; m++)
                {
                    per.MonthlyScores.TryGetValue(m, out var monthScore);
                    if (monthScore == null) continue;

                    result.FiscalPeriodByMonth.TryGetValue(m, out var monthFpId);

                    list.Add(new ScoreSnapshotDto
                    {
                        EntityId = scorecardId,
                        FiscalPeriodId = monthFpId,
                        FiscalMonth = m,
                        Quarter = ((m - 1) / 3) + 1,
                        Granularity = SnapshotGranularity.Month,
                        IsPeriodAggregate = false,
                        Score = monthScore,
                        CalculatedAt = now
                    });
                }
            }
            return list;
        }
    }
}