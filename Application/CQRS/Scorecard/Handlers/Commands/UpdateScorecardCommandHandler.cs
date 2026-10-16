using Application.CQRS.Scorecard.Requests.Commands;
using Application.DTOS.Scorecard;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Responses;
using MediatR;
using ScorecardEntity = Domain.Entities.Scorecard;

namespace Application.CQRS.Scorecard.Handlers.Commands;

public class UpdateScorecardCommandHandler : IRequestHandler<UpdateScorecardCommandRequest, Response<ScorecardDto>>
{
    private readonly IScorecardRepository scorecardRepository;
    private readonly IUnitOfWork unitOfWork;

    public UpdateScorecardCommandHandler(IScorecardRepository _scorecardRepository, IUnitOfWork _unitOfWork)
    {
        scorecardRepository = _scorecardRepository;
        unitOfWork = _unitOfWork;
    }

    public async Task<Response<ScorecardDto>> Handle(UpdateScorecardCommandRequest request, CancellationToken cancellationToken)
    {
        var scorecard = await scorecardRepository.GetById(request.Id)
            ?? throw new NotFoundException(nameof(ScorecardEntity), request.Id);

        if (!string.IsNullOrEmpty(request.Name) && request.Name != scorecard.Name)
        {
            if (await scorecardRepository.ExistsByNameInFiscalYearAsync(request.Name, scorecard.FiscalYearId))
                throw new BadRequestException($"A scorecard named '{request.Name}' already exists in this fiscal year.");
            scorecard.Name = request.Name.Trim();
        }

        if (request.Description != null) scorecard.Description = request.Description;
        if (request.Scope.HasValue) scorecard.Scope = request.Scope.Value;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<ScorecardDto>
        {
            Success = true,
            Message = "Scorecard updated successfully.",
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
