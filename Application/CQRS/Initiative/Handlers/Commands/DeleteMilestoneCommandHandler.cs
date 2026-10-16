using Application.CQRS.Initiative.Requests.Commands;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Responses;
using Domain.Entities;
using MediatR;

namespace Application.CQRS.Initiative.Handlers.Commands
{
    /// <summary>
    /// FR-402 milestone deletion. The milestone soft-delete is recorded in the initiative history
    /// because removing weight changes the weighted-progress denominator.
    /// </summary>
    public class DeleteMilestoneCommandHandler : IRequestHandler<DeleteMilestoneCommandRequest>
    {
        private readonly IInitiativeRepository initiativeRepository;
        private readonly IMilestoneRepository milestoneRepository;
        private readonly IInitiativeUpdateLogRepository updateLogRepository;
        private readonly IUnitOfWork unitOfWork;
        private readonly InitiativeGuard guard;
        private readonly InitiativeDtoFactory dtoFactory;

        public DeleteMilestoneCommandHandler(
            IInitiativeRepository _initiativeRepository,
            IMilestoneRepository _milestoneRepository,
            IInitiativeUpdateLogRepository _updateLogRepository,
            IUnitOfWork _unitOfWork,
            InitiativeGuard _guard,
            InitiativeDtoFactory _dtoFactory)
        {
            initiativeRepository = _initiativeRepository;
            milestoneRepository = _milestoneRepository;
            updateLogRepository = _updateLogRepository;
            unitOfWork = _unitOfWork;
            guard = _guard;
            dtoFactory = _dtoFactory;
        }

        public async Task Handle(DeleteMilestoneCommandRequest request, CancellationToken cancellationToken)
        {
            var initiative = await initiativeRepository.GetById(request.InitiativeId)
                ?? throw new NotFoundException("Initiative", request.InitiativeId);

            // FR-405: closed initiatives are read-only.
            guard.EnsureMutable(initiative);
            guard.EnsureCanManageMilestones(initiative);

            var milestone = await milestoneRepository.GetByIdForInitiativeAsync(request.Id, request.InitiativeId)
                ?? throw new NotFoundException("Milestone", request.Id);

            var name = milestone.Name;
            var weight = milestone.Weight;

            // System-wide convention: a delete never removes the row. It flips the shared
            // CommonEntity delete flag, which SpmsDbContext's global query filter (e => !e.IsDeleted)
            // already excludes from every read, aggregate and dropdown. The milestone stays in the
            // database with its history intact and can be brought back by clearing the flag.
            await milestoneRepository.Delete(milestone);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            // FR-403: removing weight changes progress and readiness, but never the recorded
            // execution state — only formal closure sets Completed (FRD lifecycle).

            await updateLogRepository.Add(new InitiativeUpdateLog
            {
                Id = Guid.NewGuid(),
                InitiativeId = initiative.Id,
                MilestoneId = milestone.Id,
                ChangeType = "MilestoneDeleted",
                Details = $"Milestone '{name}' deleted (weight {weight:N2}%). Remaining weights no longer total 100%.",
                PreviousValue = $"{weight:N2}%"
            });
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
