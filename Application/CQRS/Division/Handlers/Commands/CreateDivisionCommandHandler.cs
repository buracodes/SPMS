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
        private readonly IUserRepository userRepository;
        private readonly IGenericRepository<Role> roleRepository;
        private readonly IPasswordHasher passwordHasher;
        private readonly IUnitOfWork unitOfWork;

        public CreateDivisionCommandHandler(
            IDivisionRepository _divisionRepository,
            IUserRepository _userRepository,
            IGenericRepository<Role> _roleRepository,
            IPasswordHasher _passwordHasher,
            IUnitOfWork _unitOfWork)
        {
            divisionRepository = _divisionRepository;
            userRepository = _userRepository;
            roleRepository = _roleRepository;
            passwordHasher = _passwordHasher;
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

            if (await userRepository.GetByUsernameAsync(code) != null)
            {
                throw new BadRequestException($"A login account with username '{code}' already exists.");
            }

            var dataInputRole = (await roleRepository.GetAll())
                .FirstOrDefault(r => r.Name == Roles.DataInputUser);
            if (dataInputRole == null)
            {
                throw new BadRequestException("The Data Input User role is not configured.");
            }

            var division = new DivisionEntity
            {
                Id = Guid.NewGuid(),
                Name = name,
                Code = code,
                IsActive = true
            };

            var accountId = Guid.NewGuid();
            var account = new User
            {
                Id = accountId,
                Username = code,
                FullName = $"{name} Division Account",
                PasswordHash = passwordHasher.HashPassword(code),
                IsActive = true,
                DivisionId = division.Id,
                UserRoles = new List<UserRole>
                {
                    new UserRole { UserId = accountId, RoleId = dataInputRole.Id }
                }
            };

            await unitOfWork.AddRangeAsync(new[] { division }, cancellationToken);
            await userRepository.Add(account);
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
