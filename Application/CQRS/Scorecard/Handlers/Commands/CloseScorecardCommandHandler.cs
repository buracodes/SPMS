using Application.CQRS.Scorecard.Requests.Commands;
using Application.DTOS.Scorecard;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Responses;
using Domain.Enums;
using MediatR;
using ScorecardEntity = Domain.Entities.Scorecard;

namespace Application.CQRS.Scorecard.Handlers.Commands;

public class CloseScorecardCommandHandler : IRequestHandler<CloseScorecardCommandRequest, Response<ScorecardDto>>
{
    private readonly IScorecardRepository scorecardRepository;
    private readonly IUnitOfWork unitOfWork;

    public CloseScorecardCommandHandler(IScorecardRepository _scorecardRepository, IUnitOfWork _unitOfWork)
    {
        scorecardRepository = _scorecardRepository;
        unitOfWork = _unitOfWork;
    }

    public async Task<Response<ScorecardDto>> Handle(CloseScorecardCommandRequest request, CancellationToken cancellationToken)
    {
        var scorecard = await scorecardRepository.GetById(request.Id)
            ?? throw new NotFoundException(nameof(ScorecardEntity), request.Id);

        if (scorecard.ScorecardStatus == ScorecardStatus.Closed)
            throw new BadRequestException("Scorecard is already closed.");

        scorecard.ScorecardStatus = ScorecardStatus.Closed;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<ScorecardDto>
        {
            Success = true,
            Message = "Scorecard closed successfully.",
            Data = new ScorecardDto
            {
                Id = scorecard.Id,
                FiscalYearId = scorecard.FiscalYearId,
                Name = scorecard.Name,
                Description = scorecard.Description,
                Scope = scorecard.Scope,
                ScorecardStatus = scorecard.ScorecardStatus
            }
        };
    }
}
