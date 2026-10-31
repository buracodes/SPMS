using Application.CQRS.Division.Requests.Commands;
using Application.DTOS.Division;
using Application.Exceptions;
using Application.Interface;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using Domain.Constants;
using Domain.Entities;
using MediatR;
using DivisionEntity = Domain.Entities.Division;

namespace Application.CQRS.Division.Handlers.Commands
{
    public class CreateDivisionCommandHandler : IRequestHandler<CreateDivisionCommandRequest, Response<DivisionDto>>
    {
        private readonly IDivisionRepository divisionRepository;
        private readonly IUnitOfWork unitOfWork;

        public CreateDivisionCommandHandler(
            IDivisionRepository _divisionRepository,
            IUnitOfWork _unitOfWork)
        {
            divisionRepository = _divisionRepository;
            unitOfWork = _unitOfWork;
        }

        public async Task<Response<DivisionDto>> Handle(CreateDivisionCommandRequest request, CancellationToken cancellationToken)
        {
            var name = request.Name.Trim();
            var code = request.Code.Trim();

            if (await divisionRepository.ExistsByNameAsync(name))
            {
                throw new BadRequestException($"A division named '{name}' already exists.");
            }

            if (await divisionRepository.ExistsByCodeAsync(code))
            {
                throw new BadRequestException($"A division with code '{code}' already exists.");
            }

            // Divisions no longer get a local login account: authentication is AD-only, and a
            // division's members reach it through their own AD identity.
            var division = new DivisionEntity
            {
                Id = Guid.NewGuid(),
                Name = name,
                Code = code,
                IsActive = true
            };

            await unitOfWork.AddRangeAsync(new[] { division }, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return new Response<DivisionDto>
            {
                Success = true,
                Message = "Division created successfully.",
                Data = new DivisionDto
                {
                    Id = division.Id,
                    Name = division.Name,
                    Code = division.Code,
                    IsActive = division.IsActive
                }
            };
        }
    }
}
