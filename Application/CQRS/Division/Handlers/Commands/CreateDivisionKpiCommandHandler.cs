using Application.CQRS.Division.Requests.Commands;
using Application.DTOS.Division;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using Domain.Enums;
using MediatR;

namespace Application.CQRS.Division.Handlers.Commands
{
    public class CreateDivisionKpiCommandHandler : IRequestHandler<CreateDivisionKpiCommandRequest, Response<DivisionKpiDto>>
    {
        private readonly IDivisionRepository divisionRepository;
        private readonly IScorecardRepository scorecardRepository;
        private readonly IDivisionPlanRepository divisionPlanRepository;
        private readonly IDivisionGoalRepository divisionGoalRepository;
        private readonly IKpiRepository kpiRepository;
        private readonly IFiscalPeriodRepository fiscalPeriodRepository;
        private readonly IUnitOfWork unitOfWork;
        private readonly IOrganizationalScopeService organizationalScope;

        public CreateDivisionKpiCommandHandler(
            IDivisionRepository _divisionRepository,
            IScorecardRepository _scorecardRepository,
            IDivisionPlanRepository _divisionPlanRepository,
            IDivisionGoalRepository _divisionGoalRepository,
            IKpiRepository _kpiRepository,
            IFiscalPeriodRepository _fiscalPeriodRepository,
            IUnitOfWork _unitOfWork,
            IOrganizationalScopeService _organizationalScope)
        {
            organizationalScope = _organizationalScope;
            divisionRepository = _divisionRepository;
            scorecardRepository = _scorecardRepository;
            divisionPlanRepository = _divisionPlanRepository;
            divisionGoalRepository = _divisionGoalRepository;
            kpiRepository = _kpiRepository;
            fiscalPeriodRepository = _fiscalPeriodRepository;
            unitOfWork = _unitOfWork;
        }

        public async Task<Response<DivisionKpiDto>> Handle(CreateDivisionKpiCommandRequest request, CancellationToken cancellationToken)
        {
            await organizationalScope.EnsureDivisionAsync(request.DivisionId, cancellationToken);

            var division = await divisionRepository.GetById(request.DivisionId);
            if (division == null)
            {
                throw new NotFoundException(nameof(Domain.Entities.Division), request.DivisionId);
            }

            var scorecard = await scorecardRepository.GetById(request.ScorecardId);
            if (scorecard == null)
            {
                throw new NotFoundException(nameof(Domain.Entities.Scorecard), request.ScorecardId);
            }

            var plan = await divisionPlanRepository.GetByDivisionAndScorecardAsync(division.Id, scorecard.Id);
            if (plan == null)
            {
                throw new BadRequestException("Create the division plan before adding division KPIs.");
            }

            if (plan.PlanStatus != DivisionPlanStatus.Draft)
            {
                throw new BadRequestException("Division KPIs cannot be added once the plan has been finalized or locked.");
            }

            var divisionGoal = await divisionGoalRepository.GetById(request.DivisionGoalId);
            if (divisionGoal == null || divisionGoal.DivisionPlanId != plan.Id)
            {
                throw new BadRequestException("The division goal does not belong to this division plan.");
            }

            var nameTaken = await kpiRepository.ExistsUnderDivisionGoalAsync(divisionGoal.Id, request.Name.Trim());
            if (nameTaken)
            {
                throw new BadRequestException($"A division KPI named '{request.Name.Trim()}' already exists under this goal.");
            }

            var validPeriods = await fiscalPeriodRepository.GetByFiscalYearAsync(scorecard.FiscalYearId);

            // ADR-009: the KPI weight budget is checked with the insert in one serializable
            // transaction, so two concurrent creates under the same goal cannot jointly
            // exceed 100% (mirrors the corporate KPI create handler).
            return await unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var currentWeightSum = await kpiRepository.GetDivisionKpiWeightsSumAsync(divisionGoal.Id);
                if (currentWeightSum + request.Weight > 100m)
                {
                    throw new BadRequestException(
                        $"Adding this KPI would bring the total KPI weight under the goal to {currentWeightSum + request.Weight:F2}% which exceeds 100%. Adjust the weight or existing KPIs.");
                }

                var kpi = new Domain.Entities.Kpi
                {
                    Id = Guid.NewGuid(),
                    ScorecardId = scorecard.Id,
                    KraId = null,
                    DivisionGoalId = divisionGoal.Id,
                    // A division KPI is owned by the plan's division; there is no separate owner picker.
                    OwnerId = null,
                    DivisionOwnerId = division.Id,
                    UnitOfMeasureId = request.UnitOfMeasureId,
                    Name = request.Name.Trim(),
                    Description = request.Description,
                    Unit = request.Unit,
                    Weight = request.Weight,
                    ToleranceBandPercent = request.ToleranceBandPercent,
                    OptimizationType = request.OptimizationType,
                    DataSource = request.DataSource,
                    KpiType = request.KpiType,
                    IsCumulative = request.IsCumulative,
                    Baseline = request.Baseline ?? request.MinValue,
                    MinValue = request.MinValue,
                    MaxValue = request.MaxValue,
                    Formula = request.Formula,
                    IsActive = true
                };

                await unitOfWork.AddRangeAsync(new[] { kpi }, cancellationToken);

                var targets = DivisionKpiTargetBuilder.Build(
                    kpi.Id, kpi.IsCumulative, request.Targets, validPeriods);
                if (targets.Count > 0)
                {
                    await unitOfWork.AddRangeAsync(targets, cancellationToken);
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);

                return new Response<DivisionKpiDto>
                {
                    Success = true,
                    Message = "Division KPI created successfully.",
                    Data = DivisionPlanDtoFactory.BuildKpi(kpi)
                };
            }, cancellationToken);
        }
    }
}
