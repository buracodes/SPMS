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

        Domain.Entities.Division? divisionOwner = null;
        if (request.DivisionOwnerId.HasValue)
        {
            divisionOwner = await divisionRepository.GetById(request.DivisionOwnerId.Value);
            if (divisionOwner == null || !divisionOwner.IsActive)
                throw new NotFoundException(nameof(Domain.Entities.Division), request.DivisionOwnerId.Value);
        }

        if (request.Weight.HasValue && request.Weight.Value != kpi.Weight && kpi.KraId.HasValue)
        {
            var kraWithKpis = await kraRepository.GetByIdWithKpisAsync(kpi.KraId.Value);
            var otherWeight = kraWithKpis?.Kpis.Where(k => k.Id != kpi.Id).Sum(k => k.Weight) ?? 0m;
            if (otherWeight + request.Weight.Value > 100m)
                throw new BadRequestException($"Total KPI weight under this KRA cannot exceed 100%. Current total excluding this KPI is {otherWeight}%.");
        }

        if (request.KraId.HasValue)
            kpi.KraId = request.KraId.Value;

        if (request.DivisionGoalId.HasValue)
            kpi.DivisionGoalId = request.DivisionGoalId.Value;

        if (request.OwnerId.HasValue)
            kpi.OwnerId = request.OwnerId.Value;

        if (request.DivisionOwnerId.HasValue)
            kpi.DivisionOwnerId = request.DivisionOwnerId.Value;

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var updatedName = request.Name.Trim();
            if (kpi.KraId.HasValue)
            {
                var kraWithKpis = await kraRepository.GetByIdWithKpisAsync(kpi.KraId.Value);
                if (kraWithKpis?.Kpis.Any(k => !k.IsDeleted && k.Id != kpi.Id
                    && string.Equals(k.Name, updatedName, StringComparison.OrdinalIgnoreCase)) == true)
                    throw new BadRequestException($"A KPI named '{updatedName}' already exists under this KRA.");
            }
            kpi.Name = updatedName;
        }

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
    }
}
