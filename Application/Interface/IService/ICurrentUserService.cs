using System;
using System.Collections.Generic;

namespace Application.Interface.IService
{
    public interface ICurrentUserService
    {
        Guid? UserId { get; }
        string? UserName { get; }
        bool IsAuthenticated { get; }
        IReadOnlyList<string> Permissions { get; }

        /// <summary>Division carried by the authenticated principal's claims, if it is a division account.</summary>
        Guid? DivisionId { get; }

        /// <summary>Roles carried by the authenticated principal's claims.</summary>
        IReadOnlyList<string> Roles { get; }
    }
}
