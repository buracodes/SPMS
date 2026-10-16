using Application.DTOS.Division;
using Application.Interface.IService;
using Domain.Entities;

namespace Application.CQRS.Division
{
    public static class DivisionPlanDtoFactory
    {
        public static DivisionPlanDto Build(
            Domain.Entities.DivisionPlan plan,
            Domain.Entities.Scorecard scorecard,
            IReadOnlyList<Goal> corporateGoals,
            DivisionPeriodResult? performance = null,
            Domain.Entities.Scorecard? corporateScorecard = null,
            IReadOnlyDictionary<Guid, IReadOnlyList<KpiTarget>>? kpiTargets = null,
            IReadOnlyList<FiscalPeriod>? fiscalPeriods = null)
        {
            var contributedGoalIds = plan.Contributions.Select(c => c.GoalId).ToHashSet();
            var corporateGoalById = corporateGoals.ToDictionary(g => g.Id);

            return new DivisionPlanDto
            {
                Id = plan.Id,
                DivisionId = plan.DivisionId,
                DivisionName = plan.Division?.Name ?? string.Empty,
                ScorecardId = plan.ScorecardId,
                ScorecardName = scorecard.Name,
                FiscalYearId = scorecard.FiscalYearId,
                PlanStatus = plan.PlanStatus,
                FinalizedAtUtc = plan.FinalizedAtUtc,
                CorporateScorecardId = corporateScorecard?.Id,
                CorporateScorecardName = corporateScorecard?.Name,
                OverallScore = performance?.OverallScore,
                Contributions = plan.Contributions
                    .Where(c => corporateGoalById.ContainsKey(c.GoalId))
                    .Select(c => ToCorporateGoal(corporateGoalById[c.GoalId]))
                    .OrderBy(g => g.Name)
                    .ToList(),
                AvailableGoals = corporateGoals
                    .Where(g => !contributedGoalIds.Contains(g.Id))
                    .OrderBy(g => g.Name)
                    .Select(ToCorporateGoal)
                    .ToList(),
                Goals = plan.Goals
                    .OrderBy(g => g.Name)
                    .Select(g => BuildGoal(
                        g,
                        ResolveGoalScore(performance, g.Id),
                        performance?.Kpis,
                        kpiTargets))
                    .ToList(),
                FiscalPeriods = fiscalPeriods == null
                    ? new List<DivisionFiscalPeriodDto>()
                    : fiscalPeriods
                        .OrderBy(p => p.FiscalMonth)
                        .Select(p => new DivisionFiscalPeriodDto
                        {
                            Id = p.Id,
                            FiscalMonth = p.FiscalMonth,
                            Quarter = p.Quarter,
                            IsQuarterEnd = p.IsQuarterEnd,
                            IsYearEnd = p.IsYearEnd
                        })
                        .ToList()
            };
        }

        public static DivisionGoalDto BuildGoal(Domain.Entities.DivisionGoal goal)
            => BuildGoal(goal, score: null, kpiScores: null, kpiTargets: null);

        public static DivisionGoalDto BuildGoal(
            Domain.Entities.DivisionGoal goal,
            EntityScorePeriod? score,
            IReadOnlyDictionary<Guid, KpiScorePeriod>? kpiScores,
            IReadOnlyDictionary<Guid, IReadOnlyList<KpiTarget>>? kpiTargets = null)
        {
            return new DivisionGoalDto
            {
                Id = goal.Id,
                DivisionPlanId = goal.DivisionPlanId,
                ParentGoalId = goal.ParentGoalId,
                ParentGoalName = goal.ParentGoal?.Name ?? string.Empty,
                Name = goal.Name,
                Description = goal.Description,
                Weight = goal.Weight,
                ContributionDescription = goal.ContributionDescription,
                Score = score?.PeriodScore,
                Status = score?.PeriodScore is null ? null : RagFromScore(score.PeriodScore.Value),
                Kpis = goal.Kpis
                    .OrderBy(k => k.Name)
                    .Select(k => BuildKpi(
                        k,
                        ResolveKpiScore(kpiScores, k.Id),
                        ResolveKpiTargets(kpiTargets, k.Id)))
                    .ToList()
            };
        }

        public static DivisionKpiDto BuildKpi(Domain.Entities.Kpi kpi)
            => BuildKpi(kpi, kpiScore: null, targets: null);

        public static DivisionKpiDto BuildKpi(
            Domain.Entities.Kpi kpi,
            KpiScorePeriod? kpiScore,
            IReadOnlyList<KpiTarget>? targets = null)
        {
            return new DivisionKpiDto
            {
                Id = kpi.Id,
                DivisionGoalId = kpi.DivisionGoalId!.Value,
                Name = kpi.Name,
                Description = kpi.Description,
                Unit = kpi.Unit,
                UnitOfMeasureId = kpi.UnitOfMeasureId,
                OwnerId = kpi.OwnerId,
                DivisionOwnerId = kpi.DivisionOwnerId,
                DivisionOwnerName = kpi.DivisionOwner?.Name,
                Weight = kpi.Weight,
                ToleranceBandPercent = kpi.ToleranceBandPercent,
                OptimizationType = kpi.OptimizationType.ToString(),
                KpiType = kpi.KpiType.ToString(),
                DataSource = kpi.DataSource.ToString(),
                IsCumulative = kpi.IsCumulative,
                Baseline = kpi.Baseline,
                MinValue = kpi.MinValue,
                MaxValue = kpi.MaxValue,
                Formula = kpi.Formula,
                IsActive = kpi.IsActive,
                Score = kpiScore?.PeriodScore,
                Target = kpiScore?.PeriodTarget,
                Actual = kpiScore?.PeriodActual,
                Status = kpiScore?.PeriodScore is null ? null : RagFromScore(kpiScore.PeriodScore.Value),
                Targets = targets == null
                    ? new List<DivisionKpiTargetDto>()
                    : targets
                        .OrderBy(t => t.Granularity)
                        .ThenBy(t => t.FiscalPeriod?.FiscalMonth ?? 0)
                        .Select(t => new DivisionKpiTargetDto
                        {
                            FiscalPeriodId = t.FiscalPeriodId,
                            FiscalMonth = t.FiscalPeriod?.FiscalMonth ?? 0,
                            Quarter = t.FiscalPeriod?.Quarter ?? 0,
                            Granularity = t.Granularity,
                            TargetValue = t.TargetValue
                        })
                        .ToList()
            };
        }

        private static IReadOnlyList<KpiTarget>? ResolveKpiTargets(
            IReadOnlyDictionary<Guid, IReadOnlyList<KpiTarget>>? kpiTargets, Guid kpiId)
            => kpiTargets != null && kpiTargets.TryGetValue(kpiId, out var targets) ? targets : null;

        private static EntityScorePeriod? ResolveGoalScore(DivisionPeriodResult? performance, Guid goalId)
        {
            if (performance == null) return null;
            return performance.DivisionGoals.TryGetValue(goalId, out var score) ? score : null;
        }

        private static KpiScorePeriod? ResolveKpiScore(
            IReadOnlyDictionary<Guid, KpiScorePeriod>? kpiScores, Guid kpiId)
        {
            if (kpiScores == null) return null;
            return kpiScores.TryGetValue(kpiId, out var score) ? score : null;
        }

        private static CorporateGoalDto ToCorporateGoal(Goal goal) => new()
        {
            GoalId = goal.Id,
            Name = goal.Name,
            Description = goal.Description,
            Weight = goal.Weight
        };

        /// <summary>
        /// FR-607 / FR-908 RAG bands (Green >= 95, Amber >= 70, else Red). These now match
        /// <c>DashboardRag</c>; the division-plan page previously used 70/50 bands, so the
        /// same score could be coloured differently on the plan and the report.
        /// </summary>
        public static string RagFromScore(decimal score)
        {
            if (score >= 95m) return "Green";
            if (score >= 70m) return "Amber";
            return "Red";
        }
    }
}
