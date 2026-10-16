using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Interface.IService
{
    public record AccessTokenResult(string Token, DateTime ExpiresAtUtc);

    public interface ITokenService
    {
        AccessTokenResult CreateAccessToken(
            Guid userId,
            string username,
            string fullName,
            IReadOnlyList<string> roles,
            IReadOnlyList<string> permissions,
            Guid? divisionId = null);

        string CreateRefreshTokenValue();
        string HashRefreshToken(string refreshToken);
    }
}