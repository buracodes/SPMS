using Application.CQRS.Division.Requests.Commands;
using Application.DTOS.Division;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Responses;
using Domain.Entities;
using Domain.Enums;
using MediatR;
using KpiEntity = Domain.Entities.Kpi;

namespace Application.CQRS.Division.Handlers.Commands
{
    public class UpdateDivisionKpiCommandHandler
        : IRequestHandler<UpdateDivisionKpiCommandRequest, Response<DivisionKpiDto>>
    {
        private readonly IKpiRepository kpiRepository;
        private readonly IDivisionGoalRepository divisionGoalRepository;
        private readonly IScorecardRepository scorecardRepository;
        private readonly IFiscalPeriodRepository fiscalPeriodRepository;
        private readonly IKpiTargetRepository kpiTargetRepository;
        private readonly IUnitOfWork unitOfWork;

        public UpdateDivisionKpiCommandHandler(
            IKpiRepository _kpiRepository,
            IDivisionGoalRepository _divisionGoalRepository,
            IScorecardRepository _scorecardRepository,
            IFiscalPeriodRepository _fiscalPeriodRepository,
            IKpiTargetRepository _kpiTargetRepository,
            IUnitOfWork _unitOfWork)
        {
            kpiRepository = _kpiRepository;
            divisionGoalRepository = _divisionGoalRepository;
            scorecardRepository = _scorecardRepository;
            fiscalPeriodRepository = _fiscalPeriodRepository;
            kpiTargetRepository = _kpiTargetRepository;
            unitOfWork = _unitOfWork;
        }

        public async Task<Response<DivisionKpiDto>> Handle(
            UpdateDivisionKpiCommandRequest request, CancellationToken cancellationToken)
        {
            var kpi = await kpiRepository.GetById(request.DivisionKpiId)
                ?? throw new NotFoundException(nameof(KpiEntity), request.DivisionKpiId);

            if (kpi.DivisionGoalId == null)
                throw new BadRequestException("The specified KPI is not a division KPI.");

            var goal = await divisionGoalRepository.GetByIdWithPlanAsync(kpi.DivisionGoalId.Value)
                ?? throw new NotFoundException(nameof(DivisionGoal), kpi.DivisionGoalId.Value);

            if (goal.DivisionPlan == null || goal.DivisionPlan.PlanStatus != DivisionPlanStatus.Draft)
                throw new BadRequestException("Division KPIs cannot be edited once the plan has been finalized or locked.");

            if (request.Weight.HasValue && request.Weight.Value != kpi.Weight)
            {
                var otherWeight = await kpiRepository.GetDivisionKpiWeightsSumAsync(goal.Id, kpi.Id);
                if (otherWeight + request.Weight.Value > 100m)
                    throw new BadRequestException(
                        $"Total KPI weight under this goal cannot exceed 100%. Current total excluding this KPI is {otherWeight}%.");

                kpi.Weight = request.Weight.Value;
            }

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                var name = request.Name.Trim();
                if (name != kpi.Name && await kpiRepository.ExistsUnderDivisionGoalAsync(goal.Id, name))
                    throw new BadRequestException($"A division KPI named '{name}' already exists under this goal.");
                kpi.Name = name;
            }

            if (request.Description != null) kpi.Description = request.Description;
            if (request.Unit != null) kpi.Unit = request.Unit;
            if (request.ToleranceBandPercent.HasValue) kpi.ToleranceBandPercent = request.ToleranceBandPercent.Value;
            if (request.OptimizationType.HasValue) kpi.OptimizationType = request.OptimizationType.Value;
            if (request.DataSource.HasValue) kpi.DataSource = request.DataSource.Value;
            if (request.KpiType.HasValue) kpi.KpiType = request.KpiType.Value;
            if (request.IsCumulative.HasValue) kpi.IsCumulative = request.IsCumulative.Value;
            if (request.Baseline.HasValue) kpi.Baseline = request.Baseline.Value;
            if (request.MinValue.HasValue) kpi.MinValue = request.MinValue.Value;
            if (request.MaxValue.HasValue) kpi.MaxValue = request.MaxValue.Value;
            if (request.Formula != null) kpi.Formula = request.Formula;
            if (request.IsActive.HasValue) kpi.IsActive = request.IsActive.Value;
            if (request.OwnerId.HasValue) kpi.OwnerId = request.OwnerId.Value;
            if (request.DivisionOwnerId.HasValue) kpi.DivisionOwnerId = request.DivisionOwnerId.Value;
            if (request.UnitOfMeasureId.HasValue) kpi.UnitOfMeasureId = request.UnitOfMeasureId.Value;

            if (request.Targets != null)
            {
                var scorecard = await scorecardRepository.GetById(kpi.ScorecardId)
                    ?? throw new BadRequestException("The KPI's scorecard could not be found.");
                var periods = await fiscalPeriodRepository.GetByFiscalYearAsync(scorecard.FiscalYearId);
                var targets = DivisionKpiTargetBuilder.Build(
                    kpi.Id, kpi.IsCumulative, request.Targets, periods);
                await kpiTargetRepository.ReplaceForFiscalYearAsync(kpi.Id, scorecard.FiscalYearId, targets);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return new Response<DivisionKpiDto>
            {
                Success = true,
                Message = "Division KPI updated successfully.",
                Data = DivisionPlanDtoFactory.BuildKpi(kpi)
            };
        }
    }
}
