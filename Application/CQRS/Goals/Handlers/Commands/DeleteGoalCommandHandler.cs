using Application.CQRS.Goals.Requests.Commands;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using MediatR;

namespace Application.CQRS.Goals.Handlers.Commands;

public class DeleteGoalCommandHandler : IRequestHandler<DeleteGoalCommandRequest>
{
    private readonly IGoalRepository goalRepository;
    private readonly IUnitOfWork unitOfWork;

    public DeleteGoalCommandHandler(IGoalRepository _goalRepository, IUnitOfWork _unitOfWork)
    {
        goalRepository = _goalRepository;
        unitOfWork = _unitOfWork;
    }

    public async Task Handle(DeleteGoalCommandRequest request, CancellationToken cancellationToken)
    {
        var goal = await goalRepository.GetByIdWithKrasAsync(request.Id)
            ?? throw new NotFoundException(nameof(Domain.Entities.Goal), request.Id);

        if (goal.Kras.Any())
            throw new BadRequestException("Cannot delete a goal that contains KRAs.");

        await goalRepository.Delete(goal);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
