using Application.CQRS.Scorecard.Requests.Commands;
using Application.DTOS.Scorecard;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Responses;
using MediatR;
using ScorecardEntity = Domain.Entities.Scorecard;

namespace Application.CQRS.Scorecard.Handlers.Commands;

public class CreateScorecardCommandHandler : IRequestHandler<CreateScorecardCommandRequest, Response<ScorecardDto>>
{
    private readonly IScorecardRepository scorecardRepository;
    private readonly IFiscalYearRepository fiscalYearRepository;
    private readonly IUnitOfWork unitOfWork;

    public CreateScorecardCommandHandler(
        IScorecardRepository _scorecardRepository,
        IFiscalYearRepository _fiscalYearRepository,
        IUnitOfWork _unitOfWork)
    {
        scorecardRepository = _scorecardRepository;
        fiscalYearRepository = _fiscalYearRepository;
        unitOfWork = _unitOfWork;
    }

    public async Task<Response<ScorecardDto>> Handle(CreateScorecardCommandRequest request, CancellationToken cancellationToken)
    {
        var fiscalYear = await fiscalYearRepository.GetById(request.FiscalYearId);
        if (fiscalYear == null)
            throw new NotFoundException(nameof(Domain.Entities.FiscalYear), request.FiscalYearId);

        if (await scorecardRepository.ExistsByNameInFiscalYearAsync(request.Name, request.FiscalYearId))
            throw new BadRequestException($"A scorecard named '{request.Name}' already exists in this fiscal year.");

        var scorecard = new ScorecardEntity
        {
            Id = Guid.NewGuid(),
            FiscalYearId = request.FiscalYearId,
            Name = request.Name.Trim(),
            Description = request.Description,
            Scope = request.Scope,
            ScorecardStatus = Domain.Enums.ScorecardStatus.Active
        };

        await scorecardRepository.Add(scorecard);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<ScorecardDto>
        {
            Success = true,
            Message = "Scorecard created successfully.",
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
