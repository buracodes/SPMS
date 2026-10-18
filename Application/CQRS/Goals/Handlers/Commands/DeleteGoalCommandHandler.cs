using Application.CQRS.Goals.Requests.Commands;
using Application.Exceptions;
using Application.Interface.IRepository;
using MediatR;

namespace Application.CQRS.Goals.Handlers.Commands;

public class DeleteGoalCommandHandler : IRequestHandler<DeleteGoalCommandRequest>
{
    private readonly IGoalRepository goalRepository;

    public DeleteGoalCommandHandler(IGoalRepository _goalRepository)
    {
        goalRepository = _goalRepository;
    }

    public async Task Handle(DeleteGoalCommandRequest request, CancellationToken cancellationToken)
    {
        var goal = await goalRepository.GetByIdWithKrasAsync(request.Id)
            ?? throw new NotFoundException(nameof(Domain.Entities.Goal), request.Id);

        // Deletion stays blocked while dependent records exist (restricted FKs, no
        // cascading deletes). The second check ignores the global query filter on
        // purpose: a soft-deleted KRA still references this goal and must not let
        // the goal slip out from under it just because normal reads hide it.
        if (goal.Kras.Any() || await goalRepository.HasAnyKrasIncludingDeletedAsync(request.Id, cancellationToken))
        {
            throw new BadRequestException("Cannot delete a goal that has KRAs (including soft-deleted ones). Remove its KRAs first.");
        }

        // Convention: delete never removes the row - soft-delete keeps the record,
        // its relationships and its audit history, and queries filter it out.
        await goalRepository.Delete(goal);
    }
}
