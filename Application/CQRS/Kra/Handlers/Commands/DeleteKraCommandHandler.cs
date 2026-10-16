using Application.CQRS.Kra.Requests.Commands;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using MediatR;
using KraEntity = Domain.Entities.Kra;

namespace Application.CQRS.Kra.Handlers.Commands;

public class DeleteKraCommandHandler : IRequestHandler<DeleteKraCommandRequest>
{
    private readonly IKraRepository kraRepository;
    private readonly IUnitOfWork unitOfWork;

    public DeleteKraCommandHandler(IKraRepository _kraRepository, IUnitOfWork _unitOfWork)
    {
        kraRepository = _kraRepository;
        unitOfWork = _unitOfWork;
    }

    public async Task Handle(DeleteKraCommandRequest request, CancellationToken cancellationToken)
    {
        var kra = await kraRepository.GetByIdWithKpisAsync(request.Id)
            ?? throw new NotFoundException(nameof(KraEntity), request.Id);

        if (kra.Kpis.Any())
            throw new BadRequestException("Cannot delete a KRA that contains KPIs.");

        await kraRepository.Delete(kra);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
