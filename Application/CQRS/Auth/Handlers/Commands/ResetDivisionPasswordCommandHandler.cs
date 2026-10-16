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
    public class ResetDivisionPasswordCommandHandler : IRequestHandler<ResetDivisionPasswordCommandRequest, Response<bool>>
    {
        private readonly IUserRepository userRepository;
        private readonly IDivisionRepository divisionRepository;
        private readonly IRefreshTokenRepository refreshTokenRepository;
        private readonly IPasswordHasher passwordHasher;

        public ResetDivisionPasswordCommandHandler(
            IUserRepository _userRepository,
            IDivisionRepository _divisionRepository,
            IRefreshTokenRepository _refreshTokenRepository,
            IPasswordHasher _passwordHasher)
        {
            userRepository = _userRepository;
            divisionRepository = _divisionRepository;
            refreshTokenRepository = _refreshTokenRepository;
            passwordHasher = _passwordHasher;
        }

        public async Task<Response<bool>> Handle(ResetDivisionPasswordCommandRequest request, CancellationToken cancellationToken)
        {
            if (request.Dto == null)
            {
                throw new BadRequestException("Password reset data is required.");
            }

            var division = await divisionRepository.GetById(request.Dto.DivisionId);
            if (division == null)
            {
                throw new NotFoundException("Division");
            }

            var account = await userRepository.GetByDivisionIdAsync(division.Id);
            if (account == null)
            {
                throw new NotFoundException($"Division account for '{division.Name}'");
            }

            if (string.IsNullOrEmpty(request.Dto.NewPassword))
            {
                throw new BadRequestException("New password is required.");
            }

            if (string.Equals(request.Dto.NewPassword, account.Username, StringComparison.OrdinalIgnoreCase))
            {
                throw new BadRequestException("New password must not match the username.");
            }

            account.PasswordHash = passwordHasher.HashPassword(request.Dto.NewPassword);
            account.FailedLoginCount = 0;
            account.LockedUntilUtc = null;

            await userRepository.Update(account);

            await refreshTokenRepository.RevokeAllForUserAsync(account.Id, "Password reset by administrator", DateTime.UtcNow);

            return new Response<bool>
            {
                Success = true,
                Message = $"Password reset for the '{division.Name}' division account.",
                Data = true
            };
        }
    }
}
