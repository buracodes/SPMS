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
    public class LoginCommandHandler : IRequestHandler<LoginCommandRequest, Response<TokenResponseDto>>
    {
        private readonly IUserRepository userRepository;
        private readonly IPasswordHasher passwordHasher;
        private readonly ITokenService tokenService;
        private readonly IRefreshTokenRepository refreshTokenRepository;
        private readonly SecurityOptions securityOptions;

        public LoginCommandHandler(
            IUserRepository _userRepository,
            IPasswordHasher _passwordHasher,
            ITokenService _tokenService,
            IRefreshTokenRepository _refreshTokenRepository,
            SecurityOptions _securityOptions)
        {
            userRepository = _userRepository;
            passwordHasher = _passwordHasher;
            tokenService = _tokenService;
            refreshTokenRepository = _refreshTokenRepository;
            securityOptions = _securityOptions;
        }

        public async Task<Response<TokenResponseDto>> Handle(LoginCommandRequest request, CancellationToken cancellationToken)
        {
            if (request.Dto == null)
            {
                throw new BadRequestException("Login data is required.");
            }

            var user = await userRepository.GetByUsernameWithRolesAndPermissionsAsync(request.Dto.Username);
            if (user == null || !user.IsActive)
            {
                throw new UnauthorizedException("Invalid username or password.");
            }

            if (user.LockedUntilUtc.HasValue && user.LockedUntilUtc.Value > DateTime.UtcNow)
            {
                throw new UnauthorizedException("Account is temporarily locked due to multiple failed login attempts. Try again later.");
            }

            var verified = passwordHasher.VerifyPassword(user.PasswordHash, request.Dto.Password);
            if (!verified)
            {
                user.FailedLoginCount += 1;
                if (user.FailedLoginCount >= securityOptions.MaxFailedAttempts)
                {
                    user.LockedUntilUtc = DateTime.UtcNow.AddMinutes(securityOptions.LockoutMinutes);
                    user.FailedLoginCount = 0;
                }
                await userRepository.Update(user);
                 throw new UnauthorizedException("Invalid username or password.");
            }

            user.FailedLoginCount = 0;
            user.LockedUntilUtc = null;
            user.LastLoginAtUtc = DateTime.UtcNow;
            await userRepository.Update(user);

            var roles = user.UserRoles
                .Select(ur => ur.Role.Name)
                .Distinct()
                .ToList();

            var permissions = user.UserRoles
                .SelectMany(ur => ur.Role.RolePermissions)
                .Select(rp => rp.Permission.Name)
                .Distinct()
                .ToList();

            var accessToken = tokenService.CreateAccessToken(user.Id, user.Username, user.FullName, roles, permissions, user.DivisionId);

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
    }
}