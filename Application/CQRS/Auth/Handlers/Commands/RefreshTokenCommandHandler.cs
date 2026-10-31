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
using System.Text;

namespace Application.CQRS.Auth.Handlers.Commands
{
    public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommandRequest, Response<TokenResponseDto>>
    {
        private readonly IRefreshTokenRepository refreshTokenRepository;
        private readonly IUserRepository userRepository;
        private readonly ITokenService tokenService;
        private readonly SecurityOptions securityOptions;
        private readonly IEffectivePermissionService effectivePermissionService;

        public RefreshTokenCommandHandler(
            IRefreshTokenRepository _refreshTokenRepository,
            IUserRepository _userRepository,
            ITokenService _tokenService,
            SecurityOptions _securityOptions,
            IEffectivePermissionService _effectivePermissionService)
        {
            refreshTokenRepository = _refreshTokenRepository;
            userRepository = _userRepository;
            tokenService = _tokenService;
            securityOptions = _securityOptions;
            effectivePermissionService = _effectivePermissionService;
        }

        public async Task<Response<TokenResponseDto>> Handle(RefreshTokenCommandRequest request, CancellationToken cancellationToken)
        {
            if (request.Dto == null)
            {
                throw new BadRequestException("Refresh token data is required.");
            }

            var hash = tokenService.HashRefreshToken(request.Dto.RefreshToken);
            var stored = await refreshTokenRepository.GetByHashAsync(hash);

            if (stored == null || stored.RevokedAtUtc.HasValue || stored.ExpiresAtUtc <= DateTime.UtcNow)
            {
                throw new UnauthorizedException("Refresh token is invalid or has expired.");
            }

            var user = await userRepository.GetByIdWithRolesAndPermissionsAsync(stored.UserId);
            if (user == null || !user.IsActive)
            {
                throw new UnauthorizedException("User account is not active.");
            }

            var newRefreshId = Guid.NewGuid();
            var rawRefresh = tokenService.CreateRefreshTokenValue();
            var newRefresh = new RefreshToken
            {
                Id = newRefreshId,
                UserId = user.Id,
                TokenHash = tokenService.HashRefreshToken(rawRefresh),
                ExpiresAtUtc = DateTime.UtcNow.AddDays(securityOptions.RefreshTokenExpiryDays),
                CreatedAtUtc = DateTime.UtcNow
            };

            stored.RevokedAtUtc = DateTime.UtcNow;
            stored.ReasonRevoked = "Replaced by rotated token";
            stored.ReplacedByTokenId = newRefreshId;

            await refreshTokenRepository.RevokeAsync(stored);
            await refreshTokenRepository.AddAsync(newRefresh);

            var roles = user.UserRoles
                .Where(ur => ur.Role != null && !ur.IsDeleted && ur.Status == Domain.Enums.EntityStatus.Active)
                .Select(ur => ur.Role.Name)
                .Distinct()
                .ToList();

            var effectivePermissions = await effectivePermissionService.GetEffectiveGrantNamesAsync(user.Id, cancellationToken);
            var permissions = effectivePermissions.ToList();

            var accessToken = tokenService.CreateAccessToken(user.Id, user.Username, roles, permissions);

            return new Response<TokenResponseDto>
            {
                Success = true,
                Message = "Token refreshed successfully.",
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
    }
}