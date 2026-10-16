using Application.CQRS.Kra.Requests.Commands;
using Application.DTOS.Kra;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Responses;
using MediatR;
using KraEntity = Domain.Entities.Kra;

namespace Application.CQRS.Kra.Handlers.Commands;

public class CreateKraCommandHandler : IRequestHandler<CreateKraCommandRequest, Response<KraDto>>
{
    private readonly IKraRepository kraRepository;
    private readonly IGoalRepository goalRepository;
    private readonly IScorecardRepository scorecardRepository;
    private readonly IUnitOfWork unitOfWork;

    public CreateKraCommandHandler(
        IKraRepository _kraRepository,
        IGoalRepository _goalRepository,
        IScorecardRepository _scorecardRepository,
        IUnitOfWork _unitOfWork)
    {
        kraRepository = _kraRepository;
        goalRepository = _goalRepository;
        scorecardRepository = _scorecardRepository;
        unitOfWork = _unitOfWork;
    }

    public async Task<Response<KraDto>> Handle(CreateKraCommandRequest request, CancellationToken cancellationToken)
    {
        var goal = await goalRepository.GetById(request.GoalId);
        if (goal == null)
            throw new NotFoundException(nameof(Domain.Entities.Goal), request.GoalId);

        var scorecard = await scorecardRepository.GetById(request.ScorecardId);
        if (scorecard == null)
            throw new NotFoundException(nameof(Domain.Entities.Scorecard), request.ScorecardId);

        var goalWithKras = await goalRepository.GetByIdWithKrasAsync(request.GoalId);
        var existingWeight = goalWithKras?.Kras.Sum(k => k.Weight) ?? 0m;
        if (existingWeight + request.Weight > 100m)
            throw new BadRequestException($"Total KRA weight under this goal cannot exceed 100%. Current total is {existingWeight}%.");

        var kraName = request.Name.Trim();
        if (goalWithKras?.Kras.Any(k => !k.IsDeleted && string.Equals(k.Name, kraName, StringComparison.OrdinalIgnoreCase)) == true)
            throw new BadRequestException($"A KRA named '{kraName}' already exists under this goal.");

        var kra = new KraEntity
        {
            Id = Guid.NewGuid(),
            GoalId = request.GoalId,
            ScorecardId = request.ScorecardId,
            Name = kraName,
            Description = request.Description,
            Icon = request.Icon,
            StrategicThemeId = request.StrategicThemeId,
            UnitMeasure = request.UnitMeasure,
            Weight = request.Weight,
            IsActive = true
        };

        await kraRepository.Add(kra);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<KraDto>
        {
            Success = true,
            Message = "KRA created successfully.",
            Data = new KraDto
            {
                Id = kra.Id,
                GoalId = kra.GoalId,
                ScorecardId = kra.ScorecardId,
                Name = kra.Name,
                Description = kra.Description,
                Icon = kra.Icon,
                StrategicThemeId = kra.StrategicThemeId,
                UnitMeasure = kra.UnitMeasure,
                Weight = kra.Weight,
                IsActive = kra.IsActive,
                KpiCount = 0
            }
        };
    }
}
