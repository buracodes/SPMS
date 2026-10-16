using Application.CQRS.Scorecard.Requests.Commands;
using Application.DTOS.Scorecard;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Responses;
using Domain.Entities;
using Domain.Enums;
using MediatR;
using ScorecardEntity = Domain.Entities.Scorecard;
using KraEntity = Domain.Entities.Kra;
using KpiEntity = Domain.Entities.Kpi;
using System.Text;
using System.Text.RegularExpressions;

namespace Application.CQRS.Scorecard.Handlers.Commands
{
    public class CloneScorecardCommandHandler : IRequestHandler<CloneScorecardCommandRequest, Response<CloneScorecardResultDto>>
    {
        private readonly IScorecardRepository scorecardRepository;
        private readonly IGoalRepository goalRepository;
        private readonly IKraRepository kraRepository;
        private readonly IKpiTargetRepository kpiTargetRepository;
        private readonly IFiscalYearRepository fiscalYearRepository;
        private readonly IFiscalPeriodRepository fiscalPeriodRepository;
        private readonly IUnitOfWork unitOfWork;

        public CloneScorecardCommandHandler(
            IScorecardRepository _scorecardRepository,
            IGoalRepository _goalRepository,
            IKraRepository _kraRepository,
            IKpiTargetRepository _kpiTargetRepository,
            IFiscalYearRepository _fiscalYearRepository,
            IFiscalPeriodRepository _fiscalPeriodRepository,
            IUnitOfWork _unitOfWork)
        {
            scorecardRepository = _scorecardRepository;
            goalRepository = _goalRepository;
            kraRepository = _kraRepository;
            kpiTargetRepository = _kpiTargetRepository;
            fiscalYearRepository = _fiscalYearRepository;
            fiscalPeriodRepository = _fiscalPeriodRepository;
            unitOfWork = _unitOfWork;
        }

        public async Task<Response<CloneScorecardResultDto>> Handle(CloneScorecardCommandRequest request, CancellationToken cancellationToken)
        {
            var source = await scorecardRepository.GetById(request.SourceScorecardId);
            if (source == null)
            {
                throw new NotFoundException(nameof(ScorecardEntity), request.SourceScorecardId);
            }

            var targetYear = await fiscalYearRepository.GetById(request.TargetFiscalYearId);
            if (targetYear == null)
            {
                throw new NotFoundException(nameof(FiscalYear), request.TargetFiscalYearId);
            }

            var name = string.IsNullOrWhiteSpace(request.NewName) ? source.Name : request.NewName.Trim();

            var nameTaken = await scorecardRepository.ExistsByNameInFiscalYearAsync(name, request.TargetFiscalYearId);
            if (nameTaken)
            {
                throw new BadRequestException($"A scorecard named '{name}' already exists in the target fiscal year. Provide a different name.");
            }

            var targetPeriods = await fiscalPeriodRepository.GetByFiscalYearAsync(request.TargetFiscalYearId);
            if (targetPeriods.Count != 12)
            {
                throw new BadRequestException("The target fiscal year has no period calendar. Create the fiscal year periods before cloning.");
            }

            var periodByMonth = targetPeriods.ToDictionary(p => p.FiscalMonth);

            var newScorecard = new ScorecardEntity
            {
                Id = Guid.NewGuid(),
                FiscalYearId = request.TargetFiscalYearId,
                Name = name,
                Description = source.Description,
                Scope = source.Scope,
                ScorecardStatus = ScorecardStatus.Active
            };

            var sourceGoals = await LoadSourceGoalsAsync(request.SourceScorecardId, cancellationToken);

            var newGoals = new List<Goal>();
            var newKras = new List<KraEntity>();
            var newKpis = new List<KpiEntity>();
            var newTargets = new List<KpiTarget>();
            var newGoalNames = new HashSet<string>();
            var newKraNames = new HashSet<string>();

            var kpiIdMap = new Dictionary<Guid, Guid>();
            var sourceKpiByNewId = new Dictionary<Guid, KpiEntity>();

            foreach (var goal in sourceGoals)
            {
                var normalizedGoalName = NormalizeName(goal.Name);
                if (!newGoalNames.Add(normalizedGoalName))
                {
                    continue;
                }

                var newGoal = new Goal
                {
                    Id = Guid.NewGuid(),
                    ScorecardId = newScorecard.Id,
                    Name = normalizedGoalName,
                    Description = goal.Description,
                    Weight = goal.Weight,
                    IsActive = goal.IsActive
                };
                newGoals.Add(newGoal);
                var goalIdMap = new Dictionary<Guid, Guid> { [goal.Id] = newGoal.Id };
                newKraNames.Clear();

                foreach (var kra in goal.Kras)
                {
                    var normalizedKraName = NormalizeName(kra.Name);
                    if (!newKraNames.Add(normalizedKraName))
                    {
                        continue;
                    }

                    var newKra = new KraEntity
                    {
                        Id = Guid.NewGuid(),
                        GoalId = newGoal.Id,
                        ScorecardId = newScorecard.Id,
                        Name = normalizedKraName,
                        Description = kra.Description,
                        Icon = kra.Icon,
                        StrategicThemeId = kra.StrategicThemeId,
                        UnitMeasure = kra.UnitMeasure,
                        Weight = kra.Weight,
                        IsActive = kra.IsActive
                    };
                    newKras.Add(newKra);
                    var kraIdMap = new Dictionary<Guid, Guid> { [kra.Id] = newKra.Id };

                    var newKpiNames = new HashSet<string>();

                    foreach (var kpi in kra.Kpis)
                    {
                        var normalizedKpiName = NormalizeName(kpi.Name);
                        if (!newKpiNames.Add(normalizedKpiName))
                        {
                            continue;
                        }

                        var newKpi = new KpiEntity
                        {
                            Id = Guid.NewGuid(),
                            ScorecardId = newScorecard.Id,
                            KraId = newKra.Id,
                            OwnerId = kpi.OwnerId,
                            UnitOfMeasureId = kpi.UnitOfMeasureId,
                            Name = normalizedKpiName,
                            Description = kpi.Description,
                            Unit = kpi.Unit,
                            Weight = kpi.Weight,
                            ToleranceBandPercent = kpi.ToleranceBandPercent,
                            OptimizationType = kpi.OptimizationType,
                            DataSource = kpi.DataSource,
                            KpiType = kpi.KpiType,
                            IsCumulative = kpi.IsCumulative,
                            Baseline = kpi.Baseline,
                            MinValue = kpi.MinValue,
                            MaxValue = kpi.MaxValue,
                            Formula = kpi.Formula,
                            IsActive = kpi.IsActive
                        };
                        newKpis.Add(newKpi);
                        kpiIdMap[kpi.Id] = newKpi.Id;
                        sourceKpiByNewId[newKpi.Id] = kpi;
                    }
                }
            }

            foreach (var newKpi in newKpis)
            {
                var sourceKpi = sourceKpiByNewId[newKpi.Id];
                if (sourceKpi.ParentKpiId.HasValue && kpiIdMap.TryGetValue(sourceKpi.ParentKpiId.Value, out var newParentId))
                {
                    newKpi.ParentKpiId = newParentId;
                }
            }

            foreach (var goal in sourceGoals)
            {
                foreach (var kra in goal.Kras)
                {
                    foreach (var kpi in kra.Kpis)
                    {
                        var targets = await kpiTargetRepository.GetByKpiAndFiscalYearAsync(kpi.Id, source.FiscalYearId);
                        foreach (var target in targets)
                        {
                            newTargets.Add(new KpiTarget
                            {
                                Id = Guid.NewGuid(),
                                KpiId = kpiIdMap[kpi.Id],
                                FiscalPeriodId = periodByMonth[target.FiscalPeriod!.FiscalMonth].Id,
                                TargetValue = target.TargetValue,
                                Granularity = target.Granularity
                            });
                        }
                    }
                }
            }

            await unitOfWork.AddRangeAsync(new[] { newScorecard }, cancellationToken);
            await unitOfWork.AddRangeAsync(newGoals, cancellationToken);
            await unitOfWork.AddRangeAsync(newKras, cancellationToken);
            await unitOfWork.AddRangeAsync(newKpis, cancellationToken);
            await unitOfWork.AddRangeAsync(newTargets, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            var result = new CloneScorecardResultDto
            {
                SourceScorecardId = source.Id,
                TargetScorecardId = newScorecard.Id,
                TargetFiscalYearId = request.TargetFiscalYearId,
                Name = name,
                GoalCount = newGoals.Count,
                KraCount = newKras.Count,
                KpiCount = newKpis.Count,
                TargetCount = newTargets.Count
            };

            return new Response<CloneScorecardResultDto>
            {
                Success = true,
                Message = "Scorecard cloned successfully.",
                Data = result
            };
        }

        private async Task<IReadOnlyList<Goal>> LoadSourceGoalsAsync(Guid scorecardId, CancellationToken cancellationToken)
        {
            var scorecard = await scorecardRepository.GetByIdWithGoalsAsync(scorecardId);
            if (scorecard == null)
            {
                return Array.Empty<Goal>();
            }

            var goals = new List<Goal>();
            foreach (var goal in scorecard.Goals.OrderBy(g => g.Name))
            {
                var goalWithKras = await goalRepository.GetByIdWithKrasAsync(goal.Id);
                if (goalWithKras == null)
                {
                    continue;
                }

                var kras = goalWithKras.Kras.OrderBy(k => k.Name).ToList();
                goalWithKras.Kras = new List<KraEntity>();
                foreach (var kra in kras)
                {
                    var kraWithKpis = await kraRepository.GetByIdWithKpisAsync(kra.Id);
                    if (kraWithKpis == null)
                    {
                        continue;
                    }

                    kraWithKpis.Kpis = kraWithKpis.Kpis.OrderBy(k => k.Name).ToList();
                    goalWithKras.Kras.Add(kraWithKpis);
                }

                goals.Add(goalWithKras);
            }

            return goals;
        }

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var normalized = name.Normalize(NormalizationForm.FormC);
            return Regex.Replace(normalized, @"[^\w\s]", "").Trim();
        }
    }
}
