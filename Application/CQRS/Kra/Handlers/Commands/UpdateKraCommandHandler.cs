using Application.CQRS.Kra.Requests.Commands;
using Application.DTOS.Kra;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Responses;
using MediatR;
using KraEntity = Domain.Entities.Kra;

namespace Application.CQRS.Kra.Handlers.Commands;

public class UpdateKraCommandHandler : IRequestHandler<UpdateKraCommandRequest, Response<KraDto>>
{
    private readonly IKraRepository kraRepository;
    private readonly IGoalRepository goalRepository;
    private readonly IUnitOfWork unitOfWork;

    public UpdateKraCommandHandler(
        IKraRepository _kraRepository,
        IGoalRepository _goalRepository,
        IUnitOfWork _unitOfWork)
    {
       kraRepository = _kraRepository;
        goalRepository = _goalRepository;
        unitOfWork = _unitOfWork;
    }

    public async Task<Response<KraDto>> Handle(UpdateKraCommandRequest request, CancellationToken cancellationToken)
    {
        var kra = await kraRepository.GetByIdWithKpisAsync(request.Id)
            ?? throw new NotFoundException(nameof(KraEntity), request.Id);

        // FR-302 defense in depth: holds even if FluentValidation is bypassed.
        if (request.Weight.HasValue && (request.Weight.Value <= 0m || request.Weight.Value > 100m))
        {
            throw new BadRequestException("KRA weight must be greater than 0 and 100 or less.");
        }

        // Whole mutation - name-dup check, weight budget, assignment, save - runs in one
        // serializable transaction (ADR-009), mirroring the goal update handler.
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (request.Weight.HasValue && request.Weight.Value != kra.Weight)
            {
                // Total of the OTHER KRAs under the same goal: SQL-side SUM that
                // excludes this KRA (and soft-deleted rows via the global filter).
                var otherWeight = await kraRepository.GetWeightsSumAsync(
                    kra.GoalId, kra.Id, cancellationToken: cancellationToken);
                if (otherWeight + request.Weight.Value > 100m)
                {
                    throw new BadRequestException($"Total KRA weight under this goal cannot exceed 100%. Current total excluding this KRA is {otherWeight:F2}%.");
                }
            }

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                var updatedName = request.Name.Trim();
                var goalWithKras = await goalRepository.GetByIdWithKrasAsync(kra.GoalId);
                if (goalWithKras?.Kras.Any(k => k.Id != kra.Id
                    && string.Equals(k.Name, updatedName, StringComparison.OrdinalIgnoreCase)) == true)
                {
                    throw new BadRequestException($"A KRA named '{updatedName}' already exists under this goal.");
                }
                kra.Name = updatedName;
            }

            if (request.Description != null)
                kra.Description = request.Description;

            if (request.Icon != null)
                kra.Icon = request.Icon;

            if (request.StrategicThemeId.HasValue)
                kra.StrategicThemeId = request.StrategicThemeId.Value;

            if (request.UnitMeasure != null)
                kra.UnitMeasure = request.UnitMeasure;

            if (request.Weight.HasValue)
                kra.Weight = request.Weight.Value;

            if (request.IsActive.HasValue)
                kra.IsActive = request.IsActive.Value;

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return kra;
        });

        return new Response<KraDto>
        {
            Success = true,
            Message = "KRA updated successfully.",
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
                KpiCount = kra.Kpis.Count
            }
        };
    }
}
