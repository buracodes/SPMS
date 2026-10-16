using Application.CQRS.Kra.Requests.Queries;
using Application.DTOS.Kra;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Responses;
using MediatR;
using KraEntity = Domain.Entities.Kra;

namespace Application.CQRS.Kra.Handlers.Queries;

public class GetKraByIdQueryHandler : IRequestHandler<GetKraByIdQueryRequest, Response<KraDto>>
{
    private readonly IKraRepository kraRepository;

    public GetKraByIdQueryHandler(IKraRepository _kraRepository)
    {
        kraRepository = _kraRepository;
    }

    public async Task<Response<KraDto>> Handle(GetKraByIdQueryRequest request, CancellationToken cancellationToken)
    {
        var kra = await kraRepository.GetByIdWithKpisAsync(request.Id)
            ?? throw new NotFoundException(nameof(KraEntity), request.Id);

        return new Response<KraDto>
        {
            Success = true,
            Message = "KRA retrieved successfully.",
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
                KpiCount = kra.Kpis.Count,
                CreatedBy = kra.CreatedBy,
                CreatedDate = kra.CreatedDate,
                UpdatedBy = kra.UpdatedBy,
                UpdatedDate = kra.UpdatedDate,
                Status = kra.Status
            }
        };
    }
}
