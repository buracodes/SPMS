using Application.CQRS.Kra.Requests.Commands;
using Application.Exceptions;
using Application.Interface.IRepository;
using MediatR;
using KraEntity = Domain.Entities.Kra;

namespace Application.CQRS.Kra.Handlers.Commands;

public class DeleteKraCommandHandler : IRequestHandler<DeleteKraCommandRequest>
{
    private readonly IKraRepository kraRepository;

    public DeleteKraCommandHandler(IKraRepository _kraRepository)
    {
        kraRepository = _kraRepository;
    }

    public async Task Handle(DeleteKraCommandRequest request, CancellationToken cancellationToken)
    {
        var kra = await kraRepository.GetByIdWithKpisAsync(request.Id)
            ?? throw new NotFoundException(nameof(KraEntity), request.Id);

        // Deletion remains blocked while dependent records exist (restricted FKs, ADR:
        // no cascading deletes). The second check deliberately ignores the global query
        // filter: a soft-deleted KPI still holds its FK into this KRA and its history,
        // so deletion must stay blocked even when the KPI is hidden from normal reads.
        if (kra.Kpis.Any() || await kraRepository.HasKpisIncludingDeletedAsync(request.Id, cancellationToken))
        {
            throw new BadRequestException("Cannot delete a KRA that has KPIs (including soft-deleted ones). Remove its KPIs first.");
        }

        // Convention: delete never removes the row - soft-delete keeps the record,
        // its relationships and its audit history, and queries filter it out.
        await kraRepository.Delete(kra);
    }
}
