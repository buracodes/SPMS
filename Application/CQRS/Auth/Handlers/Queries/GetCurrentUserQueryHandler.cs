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
        private readonly IEffectivePermissionService effectivePermissionService;

        public GetCurrentUserQueryHandler(
            ICurrentUserService _currentUserService,
            IUserRepository _userRepository,
            IEffectivePermissionService _effectivePermissionService)
        {
            currentUserService = _currentUserService;
            userRepository = _userRepository;
            effectivePermissionService = _effectivePermissionService;
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
                .Where(ur => ur.Role != null && !ur.IsDeleted && ur.Status == Domain.Enums.EntityStatus.Active)
                .Select(ur => ur.Role.Name)
                .Distinct()
                .ToList();

            // Effective rights, not the union of every role right: denies, direct grants and
            // position grants must be reflected here exactly as they are at login.
            var effectivePermissions = await effectivePermissionService.GetEffectiveGrantNamesAsync(user.Id, cancellationToken);
            var permissions = effectivePermissions.ToList();

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