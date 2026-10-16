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
    public class LogoutCommandHandler : IRequestHandler<LogoutCommandRequest, Response<bool>>
    {
        private readonly IRefreshTokenRepository refreshTokenRepository;
        private readonly ITokenService tokenService;

        public LogoutCommandHandler(
            IRefreshTokenRepository _refreshTokenRepository,
            ITokenService _tokenService)
        {
            refreshTokenRepository = _refreshTokenRepository;
            tokenService = _tokenService;
        }

        public async Task<Response<bool>> Handle(LogoutCommandRequest request, CancellationToken cancellationToken)
        {
            if (request.Dto == null)
            {
                throw new BadRequestException("Logout data is required.");
            }

            var hash = tokenService.HashRefreshToken(request.Dto.RefreshToken);
            var stored = await refreshTokenRepository.GetByHashAsync(hash);

            if (stored != null && !stored.RevokedAtUtc.HasValue)
            {
                stored.RevokedAtUtc = DateTime.UtcNow;
                stored.ReasonRevoked = "User logged out";
                await refreshTokenRepository.RevokeAsync(stored);
            }

            return new Response<bool>
            {
                Success = true,
                Message = "Logged out successfully.",
                Data = true
            };
        }
    }
}