using Application.CQRS.Initiative.Requests.Commands;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Responses;
using Domain.Entities;
using MediatR;

namespace Application.CQRS.Initiative.Handlers.Commands;

/// <summary>FR-401 initiative deletion.</summary>
public class DeleteInitiativeCommandHandler : IRequestHandler<DeleteInitiativeCommandRequest, Response<object>>
{
    private readonly IInitiativeRepository initiativeRepository;
    private readonly IInitiativeUpdateLogRepository updateLogRepository;
    private readonly IUnitOfWork unitOfWork;

    public DeleteInitiativeCommandHandler(
        IInitiativeRepository _initiativeRepository,
        IInitiativeUpdateLogRepository _updateLogRepository,
        IUnitOfWork _unitOfWork)
    {
        initiativeRepository = _initiativeRepository;
        updateLogRepository = _updateLogRepository;
        unitOfWork = _unitOfWork;
    }

    public async Task<Response<object>> Handle(DeleteInitiativeCommandRequest request, CancellationToken cancellationToken)
    {
        var initiative = await initiativeRepository.GetById(request.Id)
            ?? throw new NotFoundException("Initiative", request.Id);

        if (initiative.IsClosed)
        {
            throw new BadRequestException(
                "A closed initiative cannot be deleted. Reopen it first if the closure was recorded in error.");
        }

        // System-wide convention: a delete never removes the row. It flips the shared CommonEntity
        // delete flag, which SpmsDbContext's global query filter (e => !e.IsDeleted) already excludes
        // from every read, so the initiative disappears from the grid, the detail route, and every
        // aggregate while its milestones, dependencies and history stay intact in the database.
        // Because the row survives, the deletion can be audited in the very history it is hiding from.
        var name = initiative.Name;

        await updateLogRepository.AddWithoutSave(new InitiativeUpdateLog
        {
            Id = Guid.NewGuid(),
            InitiativeId = initiative.Id,
            ChangeType = "InitiativeDeleted",
            Details = $"Initiative '{name}' deleted."
        });

        initiative.IsDeleted = true;
        initiative.UpdatedDate = DateTime.UtcNow;

        // Single SaveChanges so the audit row and the deactivation commit together or not at all.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<object>
        {
            Success = true,
            Message = "Initiative deleted successfully."
        };
    }
}