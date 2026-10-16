using Application.CQRS.Kpi.Requests.Commands;
using Application.DTOS.Kpi;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Responses;
using MediatR;
using KpiEntity = Domain.Entities.Kpi;

namespace Application.CQRS.Kpi.Handlers.Commands;

public class CreateKpiCommandHandler : IRequestHandler<CreateKpiCommandRequest, Response<KpiDto>>
{
    private readonly IKpiRepository kpiRepository;
    private readonly IKraRepository kraRepository;
    private readonly IScorecardRepository scorecardRepository;
    private readonly IDivisionRepository divisionRepository;
    private readonly IUnitOfWork unitOfWork;

    public CreateKpiCommandHandler(
        IKpiRepository _kpiRepository,
        IKraRepository _kraRepository,
        IScorecardRepository _scorecardRepository,
        IDivisionRepository _divisionRepository,
        IUnitOfWork _unitOfWork)
    {
        kpiRepository = _kpiRepository;
        kraRepository = _kraRepository;
        scorecardRepository = _scorecardRepository;
        divisionRepository = _divisionRepository;
        unitOfWork = _unitOfWork;
    }

    public async Task<Response<KpiDto>> Handle(CreateKpiCommandRequest request, CancellationToken cancellationToken)
    {
        var scorecard = await scorecardRepository.GetById(request.ScorecardId);
        if (scorecard == null)
            throw new NotFoundException(nameof(Domain.Entities.Scorecard), request.ScorecardId);

        if (request.KraId.HasValue)
        {
            var kra = await kraRepository.GetById(request.KraId.Value);
            if (kra == null)
                throw new NotFoundException(nameof(Domain.Entities.Kra), request.KraId.Value);
        }

        if (request.ParentKpiId.HasValue)
        {
            var parentKpi = await kpiRepository.GetById(request.ParentKpiId.Value);
            if (parentKpi == null)
                throw new NotFoundException(nameof(KpiEntity), request.ParentKpiId.Value);
        }

        Domain.Entities.Division? divisionOwner = null;
        if (request.DivisionOwnerId.HasValue)
        {
            divisionOwner = await divisionRepository.GetById(request.DivisionOwnerId.Value);
            if (divisionOwner == null || !divisionOwner.IsActive)
                throw new NotFoundException(nameof(Domain.Entities.Division), request.DivisionOwnerId.Value);
        }

        if (request.KraId.HasValue)
        {
            var kraWithKpis = await kraRepository.GetByIdWithKpisAsync(request.KraId.Value);
            var existingWeight = kraWithKpis?.Kpis.Sum(k => k.Weight) ?? 0m;
            if (existingWeight + request.Weight > 100m)
                throw new BadRequestException($"Total KPI weight under this KRA cannot exceed 100%. Current total is {existingWeight}%.");

            var kpiName = request.Name.Trim();
            if (kraWithKpis?.Kpis.Any(k => !k.IsDeleted && string.Equals(k.Name, kpiName, StringComparison.OrdinalIgnoreCase)) == true)
                throw new BadRequestException($"A KPI named '{kpiName}' already exists under this KRA.");
        }

        var kpi = new KpiEntity
        {
            Id = Guid.NewGuid(),
            ScorecardId = request.ScorecardId,
            KraId = request.KraId,
            DivisionGoalId = request.DivisionGoalId,
            OwnerId = request.OwnerId,
            DivisionOwnerId = request.DivisionOwnerId,
            Name = request.Name.Trim(),
            Description = request.Description,
            Unit = request.Unit,
            Weight = request.Weight,
            OptimizationType = request.OptimizationType,
            DataSource = request.DataSource,
            KpiType = request.KpiType,
            IsCumulative = request.IsCumulative,
            ParentKpiId = request.ParentKpiId,
            Baseline = request.Baseline,
            MinValue = request.MinValue,
            MaxValue = request.MaxValue,
            Formula = request.Formula,
            IsActive = true
        };

        await kpiRepository.Add(kpi);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<KpiDto>
        {
            Success = true,
            Message = "KPI created successfully.",
            Data = new KpiDto
            {
                Id = kpi.Id,
                ScorecardId = kpi.ScorecardId,
                KraId = kpi.KraId,
                DivisionGoalId = kpi.DivisionGoalId,
                OwnerId = kpi.OwnerId,
                DivisionOwnerId = kpi.DivisionOwnerId,
                DivisionOwnerName = divisionOwner?.Name,
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
                ChildKpiCount = 0
            }
        };
    }
}
