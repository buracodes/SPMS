using Application.CQRS.Auth.Requests.Queries;
using Application.DTOS.Auth;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.CQRS.Auth.Handlers.Queries
{
    public class GetCurrentUserQueryHandler : IRequestHandler<GetCurrentUserQueryRequest, Response<AuthUserDto>>
    {
        private readonly ICurrentUserService currentUserService;
        private readonly IUserRepository userRepository;

        public GetCurrentUserQueryHandler(
            ICurrentUserService _currentUserService,
            IUserRepository _userRepository)
        {
            currentUserService = _currentUserService;
            userRepository = _userRepository;
        }

        public async Task<Response<AuthUserDto>> Handle(GetCurrentUserQueryRequest request, CancellationToken cancellationToken)
        {
            var userId = currentUserService.UserId;
            if (!userId.HasValue)
            {
                throw new UnauthorizedException("User is not authenticated.");
            }

            var user = await userRepository.GetByIdWithRolesAndPermissionsAsync(userId.Value);
            if (user == null)
            {
                throw new NotFoundException("User");
            }

            var roles = user.UserRoles
                .Select(ur => ur.Role.Name)
                .Distinct()
                .ToList();

            var permissions = user.UserRoles
                .SelectMany(ur => ur.Role.RolePermissions)
                .Select(rp => rp.Permission.Name)
                .Distinct()
                .ToList();

            return new Response<AuthUserDto>
            {
                Success = true,
                Message = "Current user retrieved successfully.",
                Data = new AuthUserDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    FullName = user.FullName,
                    Email = user.Email,
                    DivisionId = user.DivisionId,
                    Roles = roles,
                    Permissions = permissions
                }
            };
        }
    }
}