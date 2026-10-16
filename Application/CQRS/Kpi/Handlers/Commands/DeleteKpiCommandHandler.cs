using Application.CQRS.Kpi.Requests.Commands;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using MediatR;
using KpiEntity = Domain.Entities.Kpi;

namespace Application.CQRS.Kpi.Handlers.Commands;

public class DeleteKpiCommandHandler : IRequestHandler<DeleteKpiCommandRequest>
{
    private readonly IKpiRepository kpiRepository;
    private readonly IUnitOfWork unitOfWork;

    public DeleteKpiCommandHandler(IKpiRepository _kpiRepository, IUnitOfWork _unitOfWork)
    {
        kpiRepository = _kpiRepository;
        unitOfWork = _unitOfWork;
    }

    public async Task Handle(DeleteKpiCommandRequest request, CancellationToken cancellationToken)
    {
        var kpi = await kpiRepository.GetById(request.Id)
            ?? throw new NotFoundException(nameof(KpiEntity), request.Id);

        var hasChildKpis = kpiRepository.GetQueryable().Any(k => k.ParentKpiId == kpi.Id);
        if (hasChildKpis)
            throw new BadRequestException("Cannot delete a KPI that has child KPIs.");

        await kpiRepository.Delete(kpi);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
