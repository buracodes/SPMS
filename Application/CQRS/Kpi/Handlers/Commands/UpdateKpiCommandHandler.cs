using Application.CQRS.Kpi.Requests.Commands;
using Application.DTOS.Kpi;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Responses;
using MediatR;
using KpiEntity = Domain.Entities.Kpi;

namespace Application.CQRS.Kpi.Handlers.Commands;

public class UpdateKpiCommandHandler : IRequestHandler<UpdateKpiCommandRequest, Response<KpiDto>>
{
    private readonly IKpiRepository kpiRepository;
    private readonly IKraRepository kraRepository;
    private readonly IDivisionRepository divisionRepository;
    private readonly IUnitOfWork unitOfWork;

    public UpdateKpiCommandHandler(
        IKpiRepository _kpiRepository,
        IKraRepository _kraRepository,
        IDivisionRepository _divisionRepository,
        IUnitOfWork _unitOfWork)
    {
        kpiRepository = _kpiRepository;
        kraRepository = _kraRepository;
        divisionRepository = _divisionRepository;
        unitOfWork = _unitOfWork;
    }

    public async Task<Response<KpiDto>> Handle(UpdateKpiCommandRequest request, CancellationToken cancellationToken)
    {
        var kpi = await kpiRepository.GetById(request.Id)
            ?? throw new NotFoundException(nameof(KpiEntity), request.Id);

        if (request.KraId.HasValue && request.DivisionGoalId.HasValue)
            throw new BadRequestException("A KPI must belong to exactly one parent: a KRA or a division goal, not both.");

        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var originalWeight = kpi.Weight;
            Domain.Entities.Division? divisionOwner = null;
            if (request.DivisionOwnerId.HasValue)
            {
                divisionOwner = await divisionRepository.GetById(request.DivisionOwnerId.Value);
                if (divisionOwner == null || !divisionOwner.IsActive)
                    throw new NotFoundException(nameof(Domain.Entities.Division), request.DivisionOwnerId.Value);
            }

            if (request.KraId.HasValue)
                kpi.KraId = request.KraId.Value;

            if (request.DivisionGoalId.HasValue)
                kpi.DivisionGoalId = request.DivisionGoalId.Value;

            if (kpi.KraId.HasValue == kpi.DivisionGoalId.HasValue)
                throw new BadRequestException("A KPI must belong to exactly one parent: a KRA or a division goal, not both or neither.");

            // Reduce the parent change first so budget/duplicate checks always
            // validate the KPI's final parent, then apply the editable fields.
            if (request.OwnerId.HasValue)
                kpi.OwnerId = request.OwnerId.Value;

            if (request.DivisionOwnerId.HasValue)
                kpi.DivisionOwnerId = request.DivisionOwnerId.Value;

            if (!string.IsNullOrWhiteSpace(request.Name))
                kpi.Name = request.Name.Trim();

            if (request.Description != null)
                kpi.Description = request.Description;

            if (request.Unit != null)
                kpi.Unit = request.Unit;

            if (request.Weight.HasValue)
                kpi.Weight = request.Weight.Value;

            if (request.ToleranceBandPercent.HasValue)
                kpi.ToleranceBandPercent = request.ToleranceBandPercent.Value;

            if (request.OptimizationType.HasValue)
                kpi.OptimizationType = request.OptimizationType.Value;

            if (request.DataSource.HasValue)
                kpi.DataSource = request.DataSource.Value;

            if (request.KpiType.HasValue)
                kpi.KpiType = request.KpiType.Value;

            if (request.IsCumulative.HasValue)
                kpi.IsCumulative = request.IsCumulative.Value;

            if (request.ParentKpiId.HasValue)
                kpi.ParentKpiId = request.ParentKpiId.Value;

            if (request.Baseline.HasValue)
                kpi.Baseline = request.Baseline.Value;

            if (request.MinValue.HasValue)
                kpi.MinValue = request.MinValue.Value;

            if (request.MaxValue.HasValue)
                kpi.MaxValue = request.MaxValue.Value;

            if (request.Formula != null)
                kpi.Formula = request.Formula;

            if (request.IsActive.HasValue)
                kpi.IsActive = request.IsActive.Value;

            var parentChanged = request.KraId.HasValue || request.DivisionGoalId.HasValue;
            var weightChanged = request.Weight.HasValue && request.Weight.Value != originalWeight;

            // Weight budget is re-validated against the SQL SUM with the write
            // inside one serializable transaction (ADR-009): concurrent edits under
            // the same parent cannot race past the 100% cap. Running on the final
            // parent also covers KPI moves between parents; exactly 100% is allowed.
            if (weightChanged || parentChanged)
            {
                var otherWeight = kpi.KraId.HasValue
                    ? await kpiRepository.GetKraKpiWeightsSumAsync(kpi.KraId.Value, kpi.Id)
                    : await kpiRepository.GetDivisionKpiWeightsSumAsync(kpi.DivisionGoalId!.Value, kpi.Id);
                if (otherWeight + kpi.Weight > 100m)
                    throw new BadRequestException($"Total KPI weight under this parent cannot exceed 100%. Current total excluding this KPI is {otherWeight}%.");
            }

            var nameChanged = !string.IsNullOrWhiteSpace(request.Name);
            if (nameChanged || parentChanged)
            {
                var duplicateExists = kpi.KraId.HasValue
                    ? await kpiRepository.ExistsUnderKraAsync(kpi.KraId.Value, kpi.Name, kpi.Id)
                    : await kpiRepository.ExistsUnderDivisionGoalAsync(kpi.DivisionGoalId!.Value, kpi.Name, kpi.Id);
                if (duplicateExists)
                    throw new BadRequestException($"A KPI named '{kpi.Name}' already exists under this parent.");
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);

            var childKpiCount = kpiRepository.GetQueryable().Count(k => k.ParentKpiId == kpi.Id);

            return new Response<KpiDto>
            {
                Success = true,
                Message = "KPI updated successfully.",
                Data = new KpiDto
                {
                    Id = kpi.Id,
                    ScorecardId = kpi.ScorecardId,
                    KraId = kpi.KraId,
                    DivisionGoalId = kpi.DivisionGoalId,
                    OwnerId = kpi.OwnerId,
                    DivisionOwnerId = kpi.DivisionOwnerId,
                    DivisionOwnerName = divisionOwner?.Name ?? kpi.DivisionOwner?.Name,
                    Name = kpi.Name,
                    Description = kpi.Description,
                    Unit = kpi.Unit,
                    Weight = kpi.Weight,
                    ToleranceBandPercent = kpi.ToleranceBandPercent,
                    OptimizationType = kpi.OptimizationType,
                    DataSource = kpi.DataSource,
                    KpiType = kpi.KpiType,
                    IsCumulative = kpi.IsCumulative,
                    ParentKpiId = kpi.ParentKpiId,
                    Baseline = kpi.Baseline,
                    MinValue = kpi.MinValue,
                    MaxValue = kpi.MaxValue,
                    Formula = kpi.Formula,
                    IsActive = kpi.IsActive,
                    ChildKpiCount = childKpiCount,
                    CreatedBy = kpi.CreatedBy,
                    CreatedDate = kpi.CreatedDate,
                    UpdatedBy = kpi.UpdatedBy,
                    UpdatedDate = kpi.UpdatedDate,
                    Status = kpi.Status
                }
            };
        });
    }
}
