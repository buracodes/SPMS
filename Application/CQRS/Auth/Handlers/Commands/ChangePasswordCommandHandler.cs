using Application.CQRS.Auth.Requests.Commands;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.CQRS.Auth.Handlers.Commands
{
    public class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommandRequest, Response<bool>>
    {
        private readonly IUserRepository userRepository;
        private readonly IRefreshTokenRepository refreshTokenRepository;
        private readonly IPasswordHasher passwordHasher;
        private readonly ICurrentUserService currentUserService;

        public ChangePasswordCommandHandler(
            IUserRepository _userRepository,
            IRefreshTokenRepository _refreshTokenRepository,
            IPasswordHasher _passwordHasher,
            ICurrentUserService _currentUserService)
        {
            userRepository = _userRepository;
            refreshTokenRepository = _refreshTokenRepository;
            passwordHasher = _passwordHasher;
            currentUserService = _currentUserService;
        }

        public async Task<Response<bool>> Handle(ChangePasswordCommandRequest request, CancellationToken cancellationToken)
        {
            if (request.Dto == null)
            {
                throw new BadRequestException("Password change data is required.");
            }

            var userId = currentUserService.UserId;
            if (!currentUserService.IsAuthenticated || userId == null)
            {
                throw new UnauthorizedException("User is not authenticated.");
            }

            var user = await userRepository.GetById(userId.Value);
            if (user == null || !user.IsActive)
            {
                throw new UnauthorizedException("User account is not active.");
            }

            if (string.IsNullOrEmpty(request.Dto.CurrentPassword)
                || !passwordHasher.VerifyPassword(user.PasswordHash, request.Dto.CurrentPassword))
            {
                throw new BadRequestException("Current password is incorrect.");
            }

            if (string.Equals(request.Dto.NewPassword, request.Dto.CurrentPassword, StringComparison.Ordinal))
            {
                throw new BadRequestException("New password must be different from the current password.");
            }

            if (string.Equals(request.Dto.NewPassword, user.Username, StringComparison.OrdinalIgnoreCase))
            {
                throw new BadRequestException("New password must not match the username.");
            }

            user.PasswordHash = passwordHasher.HashPassword(request.Dto.NewPassword);
            user.FailedLoginCount = 0;
            user.LockedUntilUtc = null;

            await userRepository.Update(user);

            await refreshTokenRepository.RevokeAllForUserAsync(user.Id, "Password changed by user", DateTime.UtcNow);

            return new Response<bool>
            {
                Success = true,
                Message = "Password changed successfully. Please sign in again.",
                Data = true
            };
        }
    }
}
