using Application.CQRS.AccessControl;
using Application.CQRS.AccessControl.Requests.Queries;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using Domain.Entities;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Application.CQRS.AccessControl.Handlers.Queries;

/// <summary>
/// Read side of the Access Control Management page (dynamic RBAC, ADR-011). Every count and
/// snapshot is derived from the same tables the runtime policy evaluates, so the page can
/// never show a picture the authorization policy disagrees with.
/// </summary>
public class AccessControlQueryHandlers :
    IRequestHandler<GetAccessSummaryQueryRequest, Response<AccessSummaryDto>>,
    IRequestHandler<GetAccessGroupsQueryRequest, Response<List<AccessGroupDto>>>,
    IRequestHandler<GetAccessRightsQueryRequest, Response<List<AccessRightDto>>>,
    IRequestHandler<GetAccessDirectoryQueryRequest, Response<List<AccessDirectoryUserDto>>>,
    IRequestHandler<GetAccessUserDetailQueryRequest, Response<AccessUserDetailDto>>,
    IRequestHandler<GetGroupMembershipQueryRequest, Response<List<GroupMembershipRowDto>>>,
    IRequestHandler<GetGroupRightsQueryRequest, Response<List<GroupRightRowDto>>>,
    IRequestHandler<GetUserRightsQueryRequest, Response<List<UserRightRowDto>>>,
    IRequestHandler<GetEffectiveAccessQueryRequest, Response<EffectiveAccessResultDto>>,
    IRequestHandler<GetAuthorizationAuditQueryRequest, Response<List<AuthorizationAuditEntryDto>>>
{
    private static readonly string[] RbacEntityTypes =
    {
        nameof(Role), nameof(UserRole), nameof(RolePermission), nameof(Permission), nameof(UserRight)
    };

    private const int AuditEntryLimit = 200;

    private readonly IRbacRepository rbacRepository;
    private readonly IGenericRepository<Permission> permissionRepository;
    private readonly IGenericRepository<UserRight> userRightRepository;
    private readonly IEffectivePermissionService effectivePermissions;
    private readonly IAuditLogEntryRepository auditLogEntryRepository;

    public AccessControlQueryHandlers(
        IRbacRepository _rbacRepository,
        IGenericRepository<Permission> _permissionRepository,
        IGenericRepository<UserRight> _userRightRepository,
        IEffectivePermissionService _effectivePermissions,
        IAuditLogEntryRepository _auditLogEntryRepository)
    {
        rbacRepository = _rbacRepository;
        permissionRepository = _permissionRepository;
        userRightRepository = _userRightRepository;
        effectivePermissions = _effectivePermissions;
        auditLogEntryRepository = _auditLogEntryRepository;
    }

    public async Task<Response<AccessSummaryDto>> Handle(GetAccessSummaryQueryRequest request, CancellationToken cancellationToken)
    {
        var roles = await rbacRepository.GetAllRolesWithRightsAsync();
        var permissions = await permissionRepository.GetAll();
        var userRights = await userRightRepository.GetAll();

        var summary = new AccessSummaryDto(
            TotalGroups: roles.Count,
            TotalRights: permissions.Count,
            GroupMembershipAssignments: roles.Sum(r => r.UserRoles.Count),
            DirectGrantAssignments: userRights.Count(ur => ur.Effect == Domain.Enums.PermissionEffect.Grant),
            DirectDenyAssignments: userRights.Count(ur => ur.Effect == Domain.Enums.PermissionEffect.Deny));

        return new Response<AccessSummaryDto>
        {
            Success = true,
            Message = "Access control summary retrieved successfully.",
            Data = summary
        };
    }

    public async Task<Response<List<AccessGroupDto>>> Handle(GetAccessGroupsQueryRequest request, CancellationToken cancellationToken)
    {
        var roles = await rbacRepository.GetAllRolesWithRightsAsync();

        var dtos = roles
            .OrderBy(r => r.Name)
            .Select(r => new AccessGroupDto(
                Id: r.Id,
                Name: r.Name,
                Description: r.Description,
                MemberCount: r.UserRoles.Count,
                RightCount: r.RolePermissions.Count(rp => rp.Effect == Domain.Enums.PermissionEffect.Grant),
                DenyCount: r.RolePermissions.Count(rp => rp.Effect == Domain.Enums.PermissionEffect.Deny),
                IsActive: r.Status == Domain.Enums.EntityStatus.Active && !r.IsDeleted,
                Status: r.Status.ToString(),
                LastUpdated: r.UpdatedDate ?? r.CreatedDate))
            .ToList();

        return new Response<List<AccessGroupDto>>
        {
            Success = true,
            Message = "Access groups retrieved successfully.",
            Data = dtos
        };
    }

    public async Task<Response<List<AccessRightDto>>> Handle(GetAccessRightsQueryRequest request, CancellationToken cancellationToken)
    {
        var permissions = await permissionRepository.GetAll();
        var roles = await rbacRepository.GetAllRolesWithRightsAsync();
        var userRights = await userRightRepository.GetAll();

        var groupAssignments = roles
            .SelectMany(r => r.RolePermissions)
            .GroupBy(rp => rp.PermissionId)
            .ToDictionary(g => g.Key, g => g.Count());
        var directAssignments = userRights
            .GroupBy(ur => ur.PermissionId)
            .ToDictionary(g => g.Key, g => g.Count());

        var dtos = permissions
            .OrderBy(p => p.Module)
            .ThenBy(p => p.Name)
            .Select(p =>
            {
                var groupCount = groupAssignments.TryGetValue(p.Id, out var gc) ? gc : 0;
                var directCount = directAssignments.TryGetValue(p.Id, out var dc) ? dc : 0;
                return new AccessRightDto(
                    p.Id,
                    p.Name,
                    p.Module,
                    p.Description,
                    groupCount,
                    directCount,
                    groupCount + directCount > 0,
                    p.Status.ToString(),
                    p.UpdatedDate ?? p.CreatedDate);
            })
            .ToList();

        return new Response<List<AccessRightDto>>
        {
            Success = true,
            Message = "Rights catalog retrieved successfully.",
            Data = dtos
        };
    }

    public async Task<Response<List<AccessDirectoryUserDto>>> Handle(GetAccessDirectoryQueryRequest request, CancellationToken cancellationToken)
    {
        var users = await rbacRepository.GetDirectoryUsersAsync();

        var dtos = users.Select(u => new AccessDirectoryUserDto(
            u.Id,
            u.Username,
            u.Email,
            u.FullName,
            u.IsActive,
            u.LastLoginAtUtc,
            u.DivisionId,
            u.Division?.Name,
            u.UserRoles
                .Where(ur => ur.Role != null && !ur.IsDeleted && ur.Status == Domain.Enums.EntityStatus.Active)
                .Select(ur => ur.Role.Name)
                .OrderBy(n => n)
                .ToArray()))
            .ToList();

        return new Response<List<AccessDirectoryUserDto>>
        {
            Success = true,
            Message = "Employee directory retrieved successfully.",
            Data = dtos
        };
    }

    public async Task<Response<AccessUserDetailDto>> Handle(GetAccessUserDetailQueryRequest request, CancellationToken cancellationToken)
    {
        var user = await rbacRepository.GetUserAccessGraphAsync(request.UserId);
        if (user == null)
        {
            throw new Application.Exceptions.NotFoundException(nameof(User), request.UserId);
        }

        var permissionCount = (await permissionRepository.GetAll()).Count;
        var assignedGroupIds = user.UserRoles
            .Where(ur => ur.Role != null && !ur.IsDeleted && ur.Status == Domain.Enums.EntityStatus.Active)
            .Select(ur => ur.Role!.Id)
            .ToHashSet();

        var dto = new AccessUserDetailDto(
            user.Id,
            user.Username,
            user.Email,
            user.FullName,
            user.IsActive,
            user.DivisionId,
            user.Division?.Name,
            user.LastLoginAtUtc,
            user.UserRoles
                .Where(ur => ur.Role != null && !ur.IsDeleted && ur.Status == Domain.Enums.EntityStatus.Active)
                .Select(ur => ur.Role.Name)
                .OrderBy(n => n)
                .ToArray(),
            permissionCount - assignedGroupIds.Count);

        return new Response<AccessUserDetailDto>
        {
            Success = true,
            Message = "User access detail retrieved successfully.",
            Data = dto
        };
    }

    public async Task<Response<List<GroupMembershipRowDto>>> Handle(GetGroupMembershipQueryRequest request, CancellationToken cancellationToken)
    {
        var users = await rbacRepository.GetDirectoryUsersAsync();
        var nameByUser = users.ToDictionary(u => u.Id, u => u.FullName);

        var rows = users
            .SelectMany(u => u.UserRoles
                .Where(ur => ur.Role != null && !ur.IsDeleted && ur.Status == Domain.Enums.EntityStatus.Active)
                .Select(ur => new GroupMembershipRowDto(
                    u.Id,
                    ur.RoleId,
                    u.Username,
                    u.FullName,
                    ur.Role.Name,
                    ur.CreatedDate,
                    nameByUser.TryGetValue(ur.CreatedBy, out var by) ? by : null)))
            .Where(m => request.GroupId == null || m.RoleId == request.GroupId)
            .OrderBy(m => m.RoleName)
            .ThenBy(m => m.FullName)
            .ToList();

        return new Response<List<GroupMembershipRowDto>>
        {
            Success = true,
            Message = "Group memberships retrieved successfully.",
            Data = rows
        };
    }

    public async Task<Response<List<GroupRightRowDto>>> Handle(GetGroupRightsQueryRequest request, CancellationToken cancellationToken)
    {
        var role = await rbacRepository.GetRoleWithRightsAsync(request.GroupId);
        if (role == null)
        {
            throw new Application.Exceptions.NotFoundException(nameof(Role), request.GroupId);
        }

        var users = await rbacRepository.GetDirectoryUsersAsync();
        var nameByUser = users.ToDictionary(u => u.Id, u => u.FullName);

        var rows = role.RolePermissions
            .Where(rp => rp.Permission != null)
            .Select(rp => new GroupRightRowDto(
                rp.RoleId,
                rp.PermissionId,
                role.Name,
                rp.Permission.Name,
                rp.Permission.Module,
                rp.Effect == Domain.Enums.PermissionEffect.Grant ? "Grant" : "Deny",
                rp.CreatedDate,
                nameByUser.TryGetValue(rp.CreatedBy, out var by) ? by : null))
            .OrderBy(r => r.Module)
            .ThenBy(r => r.PermissionName)
            .ToList();

        return new Response<List<GroupRightRowDto>>
        {
            Success = true,
            Message = "Group rights retrieved successfully.",
            Data = rows
        };
    }

    public async Task<Response<List<UserRightRowDto>>> Handle(GetUserRightsQueryRequest request, CancellationToken cancellationToken)
    {
        var userRights = await rbacRepository.GetUserRightsAsync(request.UserId);

        var rows = userRights
            .Where(ur => ur.Permission != null && ur.User != null)
            .Select(ur => new UserRightRowDto(
                ur.UserId,
                ur.PermissionId,
                ur.User.Username,
                ur.User.FullName,
                ur.Permission.Name,
                ur.Permission.Module,
                ur.Effect == Domain.Enums.PermissionEffect.Grant ? "Grant" : "Deny",
                ur.Reason,
                ur.CreatedDate,
                null))
            .OrderBy(r => r.Module)
            .ThenBy(r => r.PermissionName)
            .ToList();

        return new Response<List<UserRightRowDto>>
        {
            Success = true,
            Message = "User rights retrieved successfully.",
            Data = rows
        };
    }

    public async Task<Response<EffectiveAccessResultDto>> Handle(GetEffectiveAccessQueryRequest request, CancellationToken cancellationToken)
    {
        var result = await effectivePermissions.EvaluateDetailedAsync(request.UserId);
        return new Response<EffectiveAccessResultDto>
        {
            Success = true,
            Message = "Effective access retrieved successfully.",
            Data = result
        };
    }

    public async Task<Response<List<AuthorizationAuditEntryDto>>> Handle(GetAuthorizationAuditQueryRequest request, CancellationToken cancellationToken)
    {
        var entries = await auditLogEntryRepository.GetRecentAsync(AuditEntryLimit);

        var rows = entries
            .Where(e => RbacEntityTypes.Contains(e.EntityType, StringComparer.Ordinal))
            .Select(e => new AuthorizationAuditEntryDto(
                e.Id,
                e.EntityType,
                e.EntityId,
                e.Action.ToString(),
                e.PerformedByName,
                e.PerformedAt,
                e.Reason,
                e.OldValues,
                e.NewValues))
            .ToList();

        return new Response<List<AuthorizationAuditEntryDto>>
        {
            Success = true,
            Message = "Authorization audit retrieved successfully.",
            Data = rows
        };
    }
}
