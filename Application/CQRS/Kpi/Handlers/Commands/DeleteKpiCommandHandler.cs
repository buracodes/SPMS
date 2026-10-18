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

        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            // The guard covers the whole descendant tree, including soft-deleted
            // children the filtered read path no longer shows (FR-902 parent-child
            // lineage): deleting a parent must never orphan history whose actual
            // values would silently vanish from roll-ups.
            var subtree = await kpiRepository.GetSubtreeIncludingInactiveAsync(kpi.Id);
            if (await kpiRepository.HasAnyActualDataAsync(subtree.Select(k => k.Id).ToList()))
                throw new BadRequestException("Cannot delete a KPI (or any of its child KPIs) that has recorded actual performance data. Historical actual values must be preserved.");

            // Closed subtree soft-delete happens atomically: either every member of
            // the hierarchy is deactivated (shared IsDeleted + EntityStatus lifecycle
            // convention, see GenericRepository.Delete) or none is, so a concurrent
            // actual entry cannot attach data to a half-deleted hierarchy.
            foreach (var member in subtree)
            {
                if (!member.IsDeleted)
                {
                    await kpiRepository.Delete(member);
                }
            }

            return true;
        });
    }
}
