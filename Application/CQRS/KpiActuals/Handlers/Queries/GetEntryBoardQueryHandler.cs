using Application.CQRS.Kpi;
using Application.CQRS.KpiActuals.Requests.Queries;
using Application.DTOS.KpiActual;
using Application.DTOS.KpiTarget;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using Application.Services;
using Domain.Entities;
using MediatR;

namespace Application.CQRS.KpiActuals.Handlers.Queries
{
    /// <summary>
    /// Reads the Data Entry board: the KPIs the signed-in principal reports on in one fiscal
    /// year, each with its monthly targets and the actual values already recorded. The entry
    /// scope is resolved server side from the live authorization state and the HR division
    /// claims, so the page never receives — and never has to filter out — another division's KPIs.
    /// </summary>
    public class GetEntryBoardQueryHandler
        : IRequestHandler<GetEntryBoardQueryRequest, Response<EntryBoardDto>>
    {
        private readonly IKpiRepository kpiRepository;
        private readonly IScorecardRepository scorecardRepository;
        private readonly IFiscalYearRepository fiscalYearRepository;
        private readonly IFiscalPeriodRepository fiscalPeriodRepository;
        private readonly IGoalRepository goalRepository;
        private readonly IKraRepository kraRepository;
        private readonly IKpiTargetRepository kpiTargetRepository;
        private readonly IKpiActualValueRepository kpiActualValueRepository;
        private readonly IUserRepository userRepository;
        private readonly IOrganizationalScopeService organizationalScope;

        public GetEntryBoardQueryHandler(
            IKpiRepository _kpiRepository,
            IScorecardRepository _scorecardRepository,
            IFiscalYearRepository _fiscalYearRepository,
            IFiscalPeriodRepository _fiscalPeriodRepository,
            IGoalRepository _goalRepository,
            IKraRepository _kraRepository,
            IKpiTargetRepository _kpiTargetRepository,
            IKpiActualValueRepository _kpiActualValueRepository,
            IUserRepository _userRepository,
            IOrganizationalScopeService _organizationalScope)
        {
            kpiRepository = _kpiRepository;
            scorecardRepository = _scorecardRepository;
            fiscalYearRepository = _fiscalYearRepository;
            fiscalPeriodRepository = _fiscalPeriodRepository;
            goalRepository = _goalRepository;
            kraRepository = _kraRepository;
            kpiTargetRepository = _kpiTargetRepository;
            kpiActualValueRepository = _kpiActualValueRepository;
            userRepository = _userRepository;
            organizationalScope = _organizationalScope;
        }

        public async Task<Response<EntryBoardDto>> Handle(
            GetEntryBoardQueryRequest request, CancellationToken cancellationToken)
        {
            var fiscalYear = await fiscalYearRepository.GetById(request.FiscalYearId);
            if (fiscalYear == null)
            {
                throw new NotFoundException(nameof(FiscalYear), request.FiscalYearId);
            }

            var board = new EntryBoardDto { FiscalYearId = request.FiscalYearId };

            var scorecardById = (await scorecardRepository.GetAll())
                .Where(s => s.FiscalYearId == request.FiscalYearId)
                .ToDictionary(s => s.Id);
            if (scorecardById.Count == 0)
            {
                return Ok(board);
            }

            var allKpis = await kpiRepository.GetAllKpisWithDetailsAsync();
            var scopedKpis = (await KeepEntryScopeAsync(
                    allKpis.Where(k => scorecardById.ContainsKey(k.ScorecardId)),
                    cancellationToken))
                .OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (scopedKpis.Count == 0)
            {
                return Ok(board);
            }

            var kpiIds = scopedKpis.Select(k => k.Id).ToHashSet();
            var scorecardsWithKpis = scopedKpis
                .Select(k => k.ScorecardId)
                .Distinct()
                .Where(scorecardById.ContainsKey)
                .Select(id => scorecardById[id])
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var scorecard in scorecardsWithKpis)
            {
                board.Scorecards.Add(new EntryScorecardDto
                {
                    Id = scorecard.Id,
                    FiscalYearId = scorecard.FiscalYearId,
                    Name = scorecard.Name,
                    ScorecardStatus = scorecard.ScorecardStatus
                });
            }

            var kraById = (await kraRepository.GetAll()).ToDictionary(kra => kra.Id);
            var goalNameById = (await goalRepository.GetAll()).ToDictionary(g => g.Id, g => g.Name);

            foreach (var kpi in scopedKpis)
            {
                board.Kpis.Add(KpiRowMapper.Map(kpi, scorecardById, kraById, goalNameById));
            }

            var monthByPeriodId = (await fiscalPeriodRepository.GetByFiscalYearAsync(request.FiscalYearId))
                .ToDictionary(p => p.Id, p => p.FiscalMonth);

            foreach (var scorecard in scorecardsWithKpis)
            {
                await AddTargetsAsync(board, scorecard.Id, request.FiscalYearId, kpiIds, monthByPeriodId);
                await AddActualsAsync(board, scorecard.Id, request.FiscalYearId, kpiIds, monthByPeriodId, cancellationToken);
            }

            return Ok(board);
        }

        /// <summary>
        /// KPIs the principal reports on: a corporate-visibility principal reads the whole
        /// board, otherwise only a KPI owned by one of their divisions or by themselves.
        /// Personal ownership alone opens the board, so a corporate KPI assignment does not
        /// depend on a division claim. Corporate data is never a fallback: a principal without
        /// corporate visibility, a division assignment or an owned KPI receives no rows. KPIs
        /// of a division plan are excluded — they are reported on the division board.
        /// </summary>
        private async Task<IReadOnlyList<Domain.Entities.Kpi>> KeepEntryScopeAsync(
            IEnumerable<Domain.Entities.Kpi> kpis,
            CancellationToken cancellationToken)
        {
            var corporateKpis = kpis.Where(k => !k.DivisionGoalId.HasValue);
            if (await organizationalScope.HasCorporateAccessAsync(cancellationToken))
            {
                return corporateKpis.ToList();
            }

            var divisionIds = organizationalScope.DivisionIds;
            var userId = organizationalScope.UserId;
            return corporateKpis
                .Where(k => (userId.HasValue && k.OwnerId.HasValue && k.OwnerId.Value == userId.Value) ||
                            (divisionIds.Count > 0 &&
                             k.DivisionOwnerId.HasValue &&
                             divisionIds.Contains(k.DivisionOwnerId.Value)))
                .ToList();
        }

        private async Task AddTargetsAsync(
            EntryBoardDto board,
            Guid scorecardId,
            Guid fiscalYearId,
            HashSet<Guid> kpiIds,
            IReadOnlyDictionary<Guid, int> monthByPeriodId)
        {
            var targets = await kpiTargetRepository.GetForScorecardFiscalYearAsync(scorecardId, fiscalYearId);

            foreach (var group in targets.Where(t => kpiIds.Contains(t.KpiId)).GroupBy(t => t.KpiId))
            {
                var bundle = new KpiTargetsBundleDto { KpiId = group.Key, FiscalYearId = fiscalYearId };
                foreach (var target in group
                             .OrderBy(t => t.Granularity)
                             .ThenBy(t => monthByPeriodId.GetValueOrDefault(t.FiscalPeriodId)))
                {
                    var month = monthByPeriodId.GetValueOrDefault(target.FiscalPeriodId);
                    bundle.Targets.Add(new KpiTargetValueDto
                    {
                        FiscalMonth = month,
                        Quarter = (month - 1) / 3 + 1,
                        Granularity = target.Granularity,
                        TargetValue = target.TargetValue,
                        IsDerivedFromMonthly = target.IsDerivedFromMonthly
                    });
                }

                board.Targets.Add(bundle);
            }
        }

        private async Task AddActualsAsync(
            EntryBoardDto board,
            Guid scorecardId,
            Guid fiscalYearId,
            HashSet<Guid> kpiIds,
            IReadOnlyDictionary<Guid, int> monthByPeriodId,
            CancellationToken cancellationToken)
        {
            // The repository returns only current revisions: one effective value per
            // KPI + fiscal period, regardless of source. The grid must agree with scores.
            var values = await kpiActualValueRepository.GetForScorecardFiscalYearAsync(scorecardId, fiscalYearId);
            var names = ActualAttributionNames.Resolve(
                userRepository, values.Select(v => v.RecordedBy));

            var latest = values
                .Where(v => kpiIds.Contains(v.KpiId))
                .GroupBy(v => v.KpiId);

            foreach (var group in latest)
            {
                var bundle = new GetActualValuesDto { KpiId = group.Key, FiscalYearId = fiscalYearId };
                foreach (var value in group
                             .OrderBy(v => monthByPeriodId.GetValueOrDefault(v.FiscalPeriodId)))
                {
                    var month = monthByPeriodId.GetValueOrDefault(value.FiscalPeriodId);
                    bundle.Values.Add(new ActualValueDto
                    {
                        Id = value.Id,
                        KpiId = value.KpiId,
                        FiscalPeriodId = value.FiscalPeriodId,
                        FiscalMonth = month,
                        Quarter = (month - 1) / 3 + 1,
                        Value = value.Value,
                        DataSource = value.DataSource,
                        AsOfDate = value.AsOfDate,
                        Note = value.Note,
                        RecordedBy = value.RecordedBy,
                        EnteredByName = value.RecordedBy is Guid id && names.TryGetValue(id, out var name) ? name : null,
                        CreatedDate = value.CreatedDate
                    });
                }

                board.Actuals.Add(bundle);
            }
        }

        private static Response<EntryBoardDto> Ok(EntryBoardDto board) => new()
        {
            Success = true,
            Message = "Data entry board retrieved successfully.",
            Data = board
        };
    }
}
