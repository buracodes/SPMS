using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Interface.IService
{
    public record AccessTokenResult(string Token, DateTime ExpiresAtUtc);

    public interface ITokenService
    {
        /// <summary>
        /// Mints the minimum stable identity (object GUID, username) and authorization (group names,
        /// effective rights) claims. Mutable HR details and organizational scope are not embedded;
        /// they are resolved from the database per request.
        /// </summary>
        AccessTokenResult CreateAccessToken(
            Guid userId,
            string username,
            IReadOnlyList<string> roles,
            IReadOnlyList<string> permissions);

        string CreateRefreshTokenValue();
        string HashRefreshToken(string refreshToken);
    }
}