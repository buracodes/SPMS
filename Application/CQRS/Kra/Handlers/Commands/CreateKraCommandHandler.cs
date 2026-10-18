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
        // FR-302 defense in depth: these hold even if FluentValidation is bypassed.
        if (request.Weight <= 0m || request.Weight > 100m)
        {
            throw new BadRequestException("KRA weight must be greater than 0 and 100 or less.");
        }

        var kraName = request.Name.Trim();

        var goal = await goalRepository.GetById(request.GoalId)
            ?? throw new NotFoundException(nameof(Domain.Entities.Goal), request.GoalId);

        var scorecard = await scorecardRepository.GetById(request.ScorecardId)
            ?? throw new NotFoundException(nameof(Domain.Entities.Scorecard), request.ScorecardId);

        // The KRA must be created under a goal that actually lives on the selected scorecard;
        // otherwise the weight budget would be summed against a goal from another container.
        if (goal.ScorecardId != request.ScorecardId)
        {
            throw new BadRequestException("The selected goal does not belong to the selected scorecard.");
        }

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

        // The weight budget is checked with the insert inside one serializable
        // transaction (ADR-009): the SQL SUM and the insert can no longer race, so two
        // concurrent creates under the same goal cannot jointly break the 100% cap.
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            // Duplicate-name check first (soft-deleted rows are excluded by the global
            // query filter on the navigation).
            var goalWithKras = await goalRepository.GetByIdWithKrasAsync(request.GoalId);
            if (goalWithKras?.Kras.Any(k => string.Equals(k.Name, kraName, StringComparison.OrdinalIgnoreCase)) == true)
            {
                throw new BadRequestException($"A KRA named '{kraName}' already exists under this goal.");
            }

            var existingWeight = await kraRepository.GetWeightsSumAsync(request.GoalId, cancellationToken: cancellationToken);
            if (existingWeight + request.Weight > 100m)
            {
                throw new BadRequestException($"Adding this KRA would bring the total KRA weight under the goal to {existingWeight + request.Weight:F2}% which exceeds 100%. Adjust the weight or existing KRAs.");
            }

            await kraRepository.AddWithoutSave(kra);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return kra;
        });

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
