using Application.CQRS.Scorecard.Requests.Commands;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using MediatR;
using ScorecardEntity = Domain.Entities.Scorecard;

namespace Application.CQRS.Scorecard.Handlers.Commands;

public class DeleteScorecardCommandHandler : IRequestHandler<DeleteScorecardCommandRequest>
{
    private readonly IScorecardRepository scorecardRepository;
    private readonly IUnitOfWork unitOfWork;

    public DeleteScorecardCommandHandler(IScorecardRepository _scorecardRepository, IUnitOfWork _unitOfWork)
    {
        scorecardRepository = _scorecardRepository;
        unitOfWork = _unitOfWork;
    }

    public async Task Handle(DeleteScorecardCommandRequest request, CancellationToken cancellationToken)
    {
        var scorecard = await scorecardRepository.GetByIdWithGoalsAsync(request.Id)
            ?? throw new NotFoundException(nameof(ScorecardEntity), request.Id);

        if (scorecard.Goals.Any())
            throw new BadRequestException("Cannot delete a scorecard that contains goals. Remove all goals first.");

        await scorecardRepository.Delete(scorecard);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
