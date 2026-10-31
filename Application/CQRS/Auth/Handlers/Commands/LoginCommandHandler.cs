using Application.CQRS.Auth.Requests.Commands;
using Application.DTOS.Auth;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Options;
using Application.Responses;
using Domain.Entities;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Application.CQRS.Auth.Handlers.Commands
{
    public class LoginCommandHandler : IRequestHandler<LoginCommandRequest, Response<TokenResponseDto>>
    {
        private readonly IUserRepository userRepository;
        private readonly ITokenService tokenService;
        private readonly IRefreshTokenRepository refreshTokenRepository;
        private readonly SecurityOptions securityOptions;
        private readonly IEffectivePermissionService effectivePermissionService;

        public LoginCommandHandler(IUserRepository _userRepository, ITokenService _tokenService, IRefreshTokenRepository _refreshTokenRepository, SecurityOptions _securityOptions, IEffectivePermissionService _effectivePermissionService)
        {
            userRepository = _userRepository;
            tokenService = _tokenService;
            refreshTokenRepository = _refreshTokenRepository;
            securityOptions = _securityOptions;
            effectivePermissionService = _effectivePermissionService;
        }

        public async Task<Response<TokenResponseDto>> Handle(LoginCommandRequest request, CancellationToken cancellationToken)
        {
            if (request.Dto == null)
            {
                throw new BadRequestException("Login data is required.");
            }

            // AD is the only authenticator: SPMS stores no password, failure counter or lockout
            // window, so a failed bind is the whole answer.
            var adAuthResult = userRepository.AuthenticateToAD(request.Dto.Username, request.Dto.Password);
            if (adAuthResult == null || !adAuthResult.IsAuthenticated)
            {
                throw new UnauthorizedException("Invalid username or password.");
            }

            // The AD object GUID is the identity key: stable across renames and equal to HR's
            // Employee.EmpId. The username lookup is the fallback for rows created before that.
            var user = await userRepository.GetByIdWithRolesAndPermissionsAsync(adAuthResult.ObjectId)
                ?? await userRepository.GetByUsernameWithRolesAndPermissionsAsync(request.Dto.Username);

            if (user == null)
            {
                if (!securityOptions.AutoProvisionAdUsers)
                {
                    throw new UnauthorizedException("Your account has not been provisioned in SPMS. Contact an administrator.");
                }

                user = await userRepository.ProvisionAdUserAsync(
                    adAuthResult.ObjectId,
                    request.Dto.Username,
                    adAuthResult.FullName,
                    adAuthResult.Email,
                    ResolveBootstrapRole(request.Dto.Username),
                    cancellationToken);
            }

            if (user == null || !user.IsActive || user.IsDeleted)
            {
                throw new UnauthorizedException("Invalid username or password.");
            }

            // A successful login is allowed with zero rights: protected endpoints stay default-deny
            // until an administrator assigns access.
            var roles = user.UserRoles
                .Where(ur => ur.Role != null && !ur.IsDeleted && ur.Status == Domain.Enums.EntityStatus.Active)
                .Select(ur => ur.Role.Name)
                .Distinct()
                .ToList();

            var effectivePermissions = await effectivePermissionService.GetEffectiveGrantNamesAsync(user.Id, cancellationToken);
            var permissions = effectivePermissions.ToList();

            var accessToken = tokenService.CreateAccessToken(user.Id, user.Username, roles, permissions);

            var rawRefresh = tokenService.CreateRefreshTokenValue();
            var refreshToken = new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = tokenService.HashRefreshToken(rawRefresh),
                ExpiresAtUtc = DateTime.UtcNow.AddDays(securityOptions.RefreshTokenExpiryDays),
                CreatedAtUtc = DateTime.UtcNow
            };
            await refreshTokenRepository.AddAsync(refreshToken);

            return new Response<TokenResponseDto>
            {
                Success = true,
                Message = "Login successful.",
                Data = new TokenResponseDto
                {
                    AccessToken = accessToken.Token,
                    RefreshToken = rawRefresh,
                    ExpiresAtUtc = accessToken.ExpiresAtUtc,
                    User = new AuthUserDto
                    {
                        Id = user.Id,
                        Username = user.Username,
                        FullName = user.FullName,
                        Email = user.Email,
                        DivisionId = user.DivisionId,
                        Roles = roles,
                        Permissions = permissions
                    }
                }
            };
        }

        /// <summary>
        /// AD-only deployments have no seeded local accounts, so the configured bootstrap
        /// administrators are the only way a new database gets its first administrator. Everyone
        /// else is provisioned with no role and therefore starts on default-deny access.
        /// </summary>
        private string? ResolveBootstrapRole(string username)
        {
            var isBootstrapAdmin = securityOptions.BootstrapAdminUsernames
                .Any(name => string.Equals(name, username, StringComparison.OrdinalIgnoreCase));

            return isBootstrapAdmin ? Domain.Constants.Roles.Admin : null;
        }
    }
}