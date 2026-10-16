using Application.CQRS.Division.Requests.Commands;
using Application.Exceptions;
using Application.Interface.IRepository;
using Domain.Entities;
using Domain.Enums;
using MediatR;
using KpiEntity = Domain.Entities.Kpi;

namespace Application.CQRS.Division.Handlers.Commands
{
    public class DeleteDivisionKpiCommandHandler : IRequestHandler<DeleteDivisionKpiCommandRequest, bool>
    {
        private readonly IKpiRepository kpiRepository;
        private readonly IDivisionGoalRepository divisionGoalRepository;

        public DeleteDivisionKpiCommandHandler(
            IKpiRepository _kpiRepository,
            IDivisionGoalRepository _divisionGoalRepository)
        {
            kpiRepository = _kpiRepository;
            divisionGoalRepository = _divisionGoalRepository;
        }

        public async Task<bool> Handle(DeleteDivisionKpiCommandRequest request, CancellationToken cancellationToken)
        {
            var kpi = await kpiRepository.GetById(request.DivisionKpiId)
                ?? throw new NotFoundException(nameof(KpiEntity), request.DivisionKpiId);

            if (kpi.DivisionGoalId == null)
                throw new BadRequestException("The specified KPI is not a division KPI.");

            var goal = await divisionGoalRepository.GetByIdWithPlanAsync(kpi.DivisionGoalId.Value);
            if (goal?.DivisionPlan != null && goal.DivisionPlan.PlanStatus != DivisionPlanStatus.Draft)
                throw new BadRequestException("Division KPIs cannot be deleted once the plan has been finalized or locked.");

            await kpiRepository.Delete(kpi);
            return true;
        }
    }
}
