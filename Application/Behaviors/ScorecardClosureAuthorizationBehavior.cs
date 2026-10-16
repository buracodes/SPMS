using Application.CQRS.Division.Requests.Commands;
using Application.CQRS.Goals.Requests.Commands;
using Application.CQRS.Kpi.Requests.Commands;
using Application.CQRS.KpiActuals.Requests.Commands;
using Application.CQRS.KpiTargets.Requests.Commands;
using Application.CQRS.Kra.Requests.Commands;
using Application.CQRS.PeriodLock.Requests.Commands;
using Application.CQRS.Scorecard.Requests.Commands;
using Application.DTOS.KpiTarget;
using Application.Interface.IRepository;
using Application.Services;
using MediatR;

namespace Application.Behaviors
{
    /// <summary>
    /// Centralized FR-703 closure enforcement. Every scorecard-owned write funnels through
    /// this pipeline and is rejected when the affected scorecard (or its fiscal year) is
    /// closed. Actual-value entry is deliberately excluded here because it already flows
    /// through <see cref="LockAuthorizationBehavior"/>, which additionally permits an
    /// audited Admin/Power User override.
    /// </summary>
    public class ScorecardClosureAuthorizationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IBaseRequest
    {
        private readonly ScorecardClosureGuard closureGuard;
        private readonly IGoalRepository goalRepository;
        private readonly IKraRepository kraRepository;
        private readonly IKpiRepository kpiRepository;
        private readonly IDivisionGoalRepository divisionGoalRepository;

        public ScorecardClosureAuthorizationBehavior(
            ScorecardClosureGuard _closureGuard,
            IGoalRepository _goalRepository,
            IKraRepository _kraRepository,
            IKpiRepository _kpiRepository,
            IDivisionGoalRepository _divisionGoalRepository)
        {
            closureGuard = _closureGuard;
            goalRepository = _goalRepository;
            kraRepository = _kraRepository;
            kpiRepository = _kpiRepository;
            divisionGoalRepository = _divisionGoalRepository;
        }

        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            await EnforceAsync(request, cancellationToken);
            return await next();
        }

        private async Task EnforceAsync(TRequest request, CancellationToken cancellationToken)
        {
            var fiscalYearId = ResolveFiscalYearId(request);
            if (fiscalYearId.HasValue)
            {
                await closureGuard.EnsureFiscalYearOpenAsync(fiscalYearId.Value, cancellationToken);
            }

            var scorecardId = await ResolveScorecardIdAsync(request, cancellationToken);
            if (scorecardId.HasValue)
            {
                await closureGuard.EnsureScorecardOpenAsync(scorecardId.Value, cancellationToken);
            }
        }

        private static Guid? ResolveFiscalYearId(TRequest request)
        {
            return request switch
            {
                CreateScorecardCommandRequest r => r.FiscalYearId,
                CloneScorecardCommandRequest r => r.TargetFiscalYearId,
                _ => null
            };
        }

        private async Task<Guid?> ResolveScorecardIdAsync(TRequest request, CancellationToken cancellationToken)
        {
            switch (request)
            {
                case UpdateScorecardCommandRequest r:
                    return r.Id;

                case DeleteScorecardCommandRequest r:
                    return r.Id;

                case CreateGoalCommandRequest r when r.Dto is not null:
                    return r.Dto.ScorecardId;

                case UpdateGoalCommandRequest r:
                    return await GetScorecardIdByGoalAsync(r.Id, cancellationToken);

                case DeleteGoalCommandRequest r:
                    return await GetScorecardIdByGoalAsync(r.Id, cancellationToken);

                case CreateKraCommandRequest r:
                    return r.ScorecardId;

                case UpdateKraCommandRequest r:
                    return await GetScorecardIdByKraAsync(r.Id, cancellationToken);

                case DeleteKraCommandRequest r:
                    return await GetScorecardIdByKraAsync(r.Id, cancellationToken);

                case CreateKpiCommandRequest r:
                    return r.ScorecardId;

                case UpdateKpiCommandRequest r:
                    return await GetScorecardIdByKpiAsync(r.Id, cancellationToken);

                case DeleteKpiCommandRequest r:
                    return await GetScorecardIdByKpiAsync(r.Id, cancellationToken);

                case SetKpiTargetsCommandRequest r when r.Dto is not null:
                    return await GetScorecardIdByKpiAsync(r.Dto.KpiId, cancellationToken);

                case SetKpiTargetsBulkCommandRequest r when r.Dto is not null:
                    return await ResolveBulkTargetsScorecardAsync(r.Dto.Targets, cancellationToken);

                case SetPeriodLockConfigCommandRequest r when r.Dto is not null:
                    return r.Dto.ScorecardId;

                case CreateDivisionGoalCommandRequest r:
                    return r.ScorecardId;

                case UpdateDivisionGoalCommandRequest r:
                    return await GetScorecardIdByDivisionGoalAsync(r.DivisionGoalId, cancellationToken);

                case DeleteDivisionGoalCommandRequest r:
                    return await GetScorecardIdByDivisionGoalAsync(r.DivisionGoalId, cancellationToken);

                case CreateDivisionKpiCommandRequest r:
                    return r.ScorecardId;

                case UpdateDivisionKpiCommandRequest r:
                    return await GetScorecardIdByKpiAsync(r.DivisionKpiId, cancellationToken);

                case DeleteDivisionKpiCommandRequest r:
                    return await GetScorecardIdByKpiAsync(r.DivisionKpiId, cancellationToken);

                case SetDivisionContributionsCommandRequest r:
                    return r.ScorecardId;

                case FinalizeDivisionPlanCommandRequest r:
                    return r.ScorecardId;

                default:
                    return null;
            }
        }

        private async Task<Guid?> ResolveBulkTargetsScorecardAsync(
            IReadOnlyCollection<SetKpiTargetsItemDto> targets,
            CancellationToken cancellationToken)
        {
            if (targets.Count == 0)
            {
                return null;
            }

            // Bulk saves are normally scoped to one fiscal year, so resolve each KPI and
            // bail on the first closed scorecard. Distinct ids avoid duplicate lookups.
            foreach (var item in targets.Where(t => t.KpiId != Guid.Empty))
            {
                var scorecardId = await GetScorecardIdByKpiAsync(item.KpiId, cancellationToken);
                if (scorecardId.HasValue)
                {
                    await closureGuard.EnsureScorecardOpenAsync(scorecardId.Value, cancellationToken);
                }
            }

            return null;
        }

        private async Task<Guid?> GetScorecardIdByGoalAsync(Guid goalId, CancellationToken cancellationToken)
        {
            var goal = await goalRepository.GetById(goalId);
            return goal?.ScorecardId;
        }

        private async Task<Guid?> GetScorecardIdByKraAsync(Guid kraId, CancellationToken cancellationToken)
        {
            var kra = await kraRepository.GetById(kraId);
            return kra?.ScorecardId;
        }

        private async Task<Guid?> GetScorecardIdByKpiAsync(Guid kpiId, CancellationToken cancellationToken)
        {
            var kpi = await kpiRepository.GetById(kpiId);
            return kpi?.ScorecardId;
        }

        private async Task<Guid?> GetScorecardIdByDivisionGoalAsync(Guid divisionGoalId, CancellationToken cancellationToken)
        {
            var divisionGoal = await divisionGoalRepository.GetById(divisionGoalId);
            if (divisionGoal is null)
            {
                return null;
            }

            return await GetScorecardIdByGoalAsync(divisionGoal.ParentGoalId, cancellationToken);
        }
    }
}