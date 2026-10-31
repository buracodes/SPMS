using Application.CQRS.Division.Requests.Commands;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Domain.Entities;
using Domain.Enums;
using MediatR;

namespace Application.CQRS.Division.Handlers.Commands
{
    public class DeleteDivisionGoalCommandHandler : IRequestHandler<DeleteDivisionGoalCommandRequest, bool>
    {
        private readonly IDivisionGoalRepository divisionGoalRepository;
        private readonly IKpiRepository kpiRepository;
        private readonly IUnitOfWork unitOfWork;
        private readonly IOrganizationalScopeService organizationalScope;

        public DeleteDivisionGoalCommandHandler(
            IDivisionGoalRepository _divisionGoalRepository,
            IKpiRepository _kpiRepository,
            IUnitOfWork _unitOfWork,
            IOrganizationalScopeService _organizationalScope)
        {
            divisionGoalRepository = _divisionGoalRepository;
            kpiRepository = _kpiRepository;
            unitOfWork = _unitOfWork;
            organizationalScope = _organizationalScope;
        }

        public async Task<bool> Handle(DeleteDivisionGoalCommandRequest request, CancellationToken cancellationToken)
        {
            var goal = await divisionGoalRepository.GetByIdWithPlanAsync(request.DivisionGoalId)
                ?? throw new NotFoundException(nameof(DivisionGoal), request.DivisionGoalId);

            if (goal.DivisionPlan != null)
            {
                await organizationalScope.EnsureDivisionAsync(goal.DivisionPlan.DivisionId, cancellationToken);
            }

            if (goal.DivisionPlan == null || goal.DivisionPlan.PlanStatus != DivisionPlanStatus.Draft)
                throw new BadRequestException("Division goals cannot be deleted once the plan has been finalized or locked.");

            var kpiIds = kpiRepository.GetQueryable()
                .Where(k => k.DivisionGoalId == goal.Id)
                .Select(k => k.Id)
                .ToList();

            if (kpiIds.Count > 0 && await kpiRepository.HasAnyActualDataAsync(kpiIds))
                throw new BadRequestException("Cannot delete a division goal whose KPIs have recorded actual performance data. Historical actual values must be preserved.");

            // Soft-delete the goal and its KPIs in a single SaveChanges so a failure can
            // never leave a half-deleted goal (previously one save happened per KPI).
            var now = DateTime.UtcNow;
            foreach (var kpiId in kpiIds)
            {
                var kpi = await kpiRepository.GetById(kpiId);
                if (kpi != null)
                {
                    kpi.IsDeleted = true;
                    kpi.Status = EntityStatus.Inactive;
                    kpi.UpdatedDate = now;
                }
            }

            goal.IsDeleted = true;
            goal.Status = EntityStatus.Inactive;
            goal.UpdatedDate = now;

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }
    }
}
