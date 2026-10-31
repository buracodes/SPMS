using Application.Interface.IService;
using Domain.Constants;
using Infrastructure.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Infrastructure.Service
{
    public class JwtTokenService : ITokenService
    {
        private readonly JwtOptions jwtOptions;

        public JwtTokenService(IOptions<JwtOptions> _jwtOptions)
        {
            jwtOptions = _jwtOptions.Value;
        }

        public AccessTokenResult CreateAccessToken(
            Guid userId,
            string username,
            IReadOnlyList<string> roles,
            IReadOnlyList<string> permissions)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, username)
            };

            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            foreach (var permission in permissions)
            {
                claims.Add(new Claim(Permissions.PermissionClaimType, permission));
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var now = DateTime.UtcNow;
            var expires = now.AddMinutes(jwtOptions.AccessTokenExpiryMinutes);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Issuer = jwtOptions.Issuer,
                Audience = jwtOptions.Audience,
                NotBefore = now,
                Expires = expires,
                SigningCredentials = credentials
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);

            return new AccessTokenResult(tokenHandler.WriteToken(token), expires);
        }

        public string CreateRefreshTokenValue()
        {
            var bytes = RandomNumberGenerator.GetBytes(64);
            return Convert.ToBase64String(bytes);
        }

        public string HashRefreshToken(string refreshToken)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
            return Convert.ToHexString(bytes);
        }
    }
}
