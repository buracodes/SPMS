using Application.CQRS.AccessControl;
using Application.Responses;
using MediatR;
using System;
using System.Collections.Generic;

namespace Application.CQRS.AccessControl.Requests.Queries;

public class GetAccessSummaryQueryRequest : IRequest<Response<AccessSummaryDto>>
{
}

public class GetAccessGroupsQueryRequest : IRequest<Response<List<AccessGroupDto>>>
{
}

public class GetAccessRightsQueryRequest : IRequest<Response<List<AccessRightDto>>>
{
}

public class GetAccessDirectoryQueryRequest : IRequest<Response<List<AccessDirectoryUserDto>>>
{
}

public class GetAccessUserDetailQueryRequest : IRequest<Response<AccessUserDetailDto>>
{
    public Guid UserId { get; set; }
}

public class GetGroupMembershipQueryRequest : IRequest<Response<List<GroupMembershipRowDto>>>
{
    /// <summary>All memberships for the page when null; otherwise one group's members.</summary>
    public Guid? GroupId { get; set; }

    /// <summary>One employee's group memberships, for the member picker's right-hand card.</summary>
    public Guid? UserId { get; set; }
}

public class GetGroupRightsQueryRequest : IRequest<Response<List<GroupRightRowDto>>>
{
    public Guid GroupId { get; set; }
}

public class GetUserRightsQueryRequest : IRequest<Response<List<UserRightRowDto>>>
{
    public Guid UserId { get; set; }
}

public class GetEffectiveAccessQueryRequest : IRequest<Response<EffectiveAccessResultDto>>
{
    public Guid UserId { get; set; }
}

public class GetAuthorizationAuditQueryRequest : IRequest<Response<List<AuthorizationAuditEntryDto>>>
{
}
