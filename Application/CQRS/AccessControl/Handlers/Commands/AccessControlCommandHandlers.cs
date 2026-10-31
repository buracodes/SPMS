using Application.CQRS.AccessControl;
using Application.CQRS.AccessControl.Requests.Commands;
using Application.DTOS.AccessControl;
using Application.Exceptions;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Responses;
using Application.Services;
using Domain.Constants;
using Domain.Entities;
using Domain.Enums;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Application.CQRS.AccessControl.Handlers.Commands;

/// <summary>
/// Write side of the Access Control Management page (dynamic RBAC, ADR-011). Every command
/// enforces the three safeguard rules before persisting anything:
///   1. self-modification — an administrator can never change their own effective access
///      (memberships, direct rights, or the rights of a group they belong to);
///   2. protected system groups — the seeded groups (e.g. the Admin group) cannot be
///      renamed, de-activated or have their rights changed;
///   3. last-administrator safeguard — a change is refused when it would leave the policy
///      with no active administrator holding both <c>admin:users.manage</c> and
///      <c>admin:roles.manage</c>.
/// </summary>
public class AccessControlCommandHandlers :
    IRequestHandler<CreateAccessGroupCommandRequest, Response<AccessGroupDto>>,
    IRequestHandler<UpdateAccessGroupCommandRequest, Response<AccessGroupDto>>,
    IRequestHandler<SetAccessGroupStatusCommandRequest, Response<AccessGroupDto>>,
    IRequestHandler<AssignGroupMembershipCommandRequest, Response<GroupMembershipRowDto>>,
    IRequestHandler<RemoveGroupMembershipCommandRequest, Response<bool>>,
    IRequestHandler<SetGroupRightCommandRequest, Response<GroupRightRowDto>>,
    IRequestHandler<RemoveGroupRightCommandRequest, Response<bool>>,
    IRequestHandler<SetUserRightCommandRequest, Response<UserRightRowDto>>,
    IRequestHandler<RemoveUserRightCommandRequest, Response<bool>>,
    IRequestHandler<CreateAccessRightCommandRequest, Response<AccessRightDto>>,
    IRequestHandler<UpdateAccessRightCommandRequest, Response<AccessRightDto>>,
    IRequestHandler<DeleteAccessRightCommandRequest, Response<bool>>
{
    private static readonly string[] ManagementKeys =
    {
        Permissions.AdminUsersManage,
        Permissions.AdminRolesManage
    };

    private readonly IRbacRepository rbacRepository;
    private readonly IGenericRepository<Role> roleRepository;
    private readonly IGenericRepository<RolePermission> roleRightRepository;
    private readonly IGenericRepository<UserRole> membershipRepository;
    private readonly IGenericRepository<Permission> permissionRepository;
    private readonly IGenericRepository<UserRight> userRightRepository;
    private readonly IGenericRepository<User> userRepository;
    private readonly IUserRepository employeeAccounts;
    private readonly IHRInfoRepository hrInfoRepository;
    private readonly ICurrentUserService currentUserService;
    private readonly IAuditContext auditContext;

    public AccessControlCommandHandlers(
        IRbacRepository _rbacRepository,
        IGenericRepository<Role> _roleRepository,
        IGenericRepository<RolePermission> _roleRightRepository,
        IGenericRepository<UserRole> _membershipRepository,
        IGenericRepository<Permission> _permissionRepository,
        IGenericRepository<UserRight> _userRightRepository,
        IGenericRepository<User> _userRepository,
        IUserRepository _employeeAccounts,
        IHRInfoRepository _hrInfoRepository,
        ICurrentUserService _currentUserService,
        IAuditContext _auditContext)
    {
        rbacRepository = _rbacRepository;
        roleRepository = _roleRepository;
        roleRightRepository = _roleRightRepository;
        membershipRepository = _membershipRepository;
        permissionRepository = _permissionRepository;
        userRightRepository = _userRightRepository;
        userRepository = _userRepository;
        employeeAccounts = _employeeAccounts;
        hrInfoRepository = _hrInfoRepository;
        currentUserService = _currentUserService;
        auditContext = _auditContext;
    }

    // ── Groups ──────────────────────────────────────────────────────────────

    public async Task<Response<AccessGroupDto>> Handle(CreateAccessGroupCommandRequest request, CancellationToken cancellationToken)
    {
        var dto = request.Dto!;
        var actor = RequireActor();
        var now = DateTime.UtcNow;

        var roles = await rbacRepository.GetAllRolesWithRightsAsync();
        if (roles.Any(r => string.Equals(r.Name, dto.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BadRequestException($"An access group named '{dto.Name}' already exists.");
        }

        if (Roles.All.Contains(dto.Name, StringComparer.OrdinalIgnoreCase))
        {
            throw new BadRequestException("This name is reserved for a protected system group.");
        }

        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            Status = EntityStatus.Active,
            CreatedBy = actor,
            CreatedDate = now
        };
        await roleRepository.Add(role);

        return new Response<AccessGroupDto>
        {
            Success = true,
            Message = "Access group created successfully.",
            Data = new AccessGroupDto(
                role.Id, role.Name, role.Description, 0, 0, 0, true, role.Status.ToString(), role.CreatedDate)
        };
    }

    public async Task<Response<AccessGroupDto>> Handle(UpdateAccessGroupCommandRequest request, CancellationToken cancellationToken)
    {
        var role = await roleRepository.GetById(request.Id);
        if (role == null)
        {
            throw new NotFoundException(nameof(Role), request.Id);
        }

        var actor = RequireActor();
        var dto = request.Dto!;

        var newName = dto.Name?.Trim();
        if (!string.IsNullOrEmpty(newName) && !string.Equals(newName, role.Name, StringComparison.Ordinal))
        {
            if (IsProtectedGroup(role.Name))
            {
                throw new BadRequestException("The protected system group cannot be renamed.");
            }

            if (Roles.All.Contains(newName, StringComparer.OrdinalIgnoreCase))
            {
                throw new BadRequestException("This name is reserved for a protected system group.");
            }

            var roles = await rbacRepository.GetAllRolesWithRightsAsync();
            if (roles.Any(r => r.Id != role.Id && string.Equals(r.Name, newName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new BadRequestException($"An access group named '{newName}' already exists.");
            }

            role.Name = newName;
        }

        role.Description = dto.Description?.Trim();
        role.UpdatedBy = actor;
        role.UpdatedDate = DateTime.UtcNow;
        await roleRepository.Update(role);

        var reloaded = await rbacRepository.GetRoleWithRightsAsync(role.Id) ?? role;
        return new Response<AccessGroupDto>
        {
            Success = true,
            Message = "Access group updated successfully.",
            Data = ToGroupDto(reloaded, rolesCache: null)
        };
    }

    public async Task<Response<AccessGroupDto>> Handle(SetAccessGroupStatusCommandRequest request, CancellationToken cancellationToken)
    {
        var role = await rbacRepository.GetRoleWithRightsAsync(request.Id);
        if (role == null)
        {
            throw new NotFoundException(nameof(Role), request.Id);
        }

        var actor = RequireActor();

        if (request.Dto!.IsActive)
        {
            role.Status = EntityStatus.Active;
        }
        else
        {
            if (IsProtectedGroup(role.Name))
            {
                throw new BadRequestException("The protected system group cannot be deactivated.");
            }

            var (_, members) = SplitMembers(role);
            if (members.Contains(actor))
            {
                throw new ForbiddenException("You cannot deactivate an access group that grants you access.");
            }

            var administratorsLeft = await rbacRepository.CountActiveAdministratorsExcludingRoleAsync(role.Id);
            if (administratorsLeft == 0)
            {
                throw new BadRequestException(
                    "This change would leave no active administrators. Assign another administrator first.");
            }

            role.Status = EntityStatus.Inactive;
        }

        role.UpdatedBy = actor;
        role.UpdatedDate = DateTime.UtcNow;
        await roleRepository.Update(role);

        return new Response<AccessGroupDto>
        {
            Success = true,
            Message = "Access group status updated successfully.",
            Data = ToGroupDto(role, rolesCache: null)
        };
    }

    // ── Membership ─────────────────────────────────────────────────────────

    public async Task<Response<GroupMembershipRowDto>> Handle(AssignGroupMembershipCommandRequest request, CancellationToken cancellationToken)
    {
        var actor = RequireActor();
        var now = DateTime.UtcNow;

        var user = await ResolveAssignableEmployeeAsync(request.UserId, actor, cancellationToken);

        var role = await rbacRepository.GetRoleWithRightsAsync(request.GroupId);
        if (role == null || role.Status != EntityStatus.Active)
        {
            throw new NotFoundException(nameof(Role), request.GroupId);
        }

        if (request.UserId == actor)
        {
            throw new ForbiddenException("You cannot change your own access group memberships.");
        }

        var membership = role.UserRoles.FirstOrDefault(ur => ur.UserId == request.UserId);
        if (membership != null && !membership.IsDeleted && membership.Status == EntityStatus.Active)
        {
            throw new BadRequestException("The employee is already a member of this access group.");
        }

        if (membership == null)
        {
            membership = new UserRole
            {
                Id = Guid.NewGuid(),
                UserId = request.UserId,
                RoleId = role.Id,
                Status = EntityStatus.Active,
                CreatedBy = actor,
                CreatedDate = now
            };
            await membershipRepository.Add(membership);
        }
        else
        {
            // (UserId, RoleId) is the key, so a removed membership must be revived, not re-inserted.
            membership.IsDeleted = false;
            membership.Status = EntityStatus.Active;
            membership.UpdatedBy = actor;
            membership.UpdatedDate = now;
            await membershipRepository.Update(membership);
        }

        return new Response<GroupMembershipRowDto>
        {
            Success = true,
            Message = "Member assigned to access group successfully.",
            Data = new GroupMembershipRowDto(
                membership.UserId, membership.RoleId, user.Username, user.FullName, role.Name, now, currentUserService.UserName)
        };
    }

    public async Task<Response<bool>> Handle(RemoveGroupMembershipCommandRequest request, CancellationToken cancellationToken)
    {
        var actor = RequireActor();

        var role = await rbacRepository.GetRoleWithRightsAsync(request.GroupId);
        if (role == null)
        {
            throw new NotFoundException(nameof(Role), request.GroupId);
        }

        var membership = role.UserRoles.FirstOrDefault(ur =>
            ur.UserId == request.UserId && !ur.IsDeleted && ur.Status == EntityStatus.Active);
        if (membership == null)
        {
            throw new NotFoundException("Membership");
        }

        if (request.UserId == actor)
        {
            throw new ForbiddenException("You cannot remove yourself from an access group.");
        }

        await RequireOtherAdministratorAsync(request.UserId);

        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            auditContext.Reason = request.Reason!.Trim();
        }

        membership.IsDeleted = true;
        membership.UpdatedBy = actor;
        membership.UpdatedDate = DateTime.UtcNow;
        await membershipRepository.Update(membership);

        return new Response<bool>
        {
            Success = true,
            Message = "Member removed from access group successfully.",
            Data = true
        };
    }

    // ── Group rights ───────────────────────────────────────────────────────

    public async Task<Response<GroupRightRowDto>> Handle(SetGroupRightCommandRequest request, CancellationToken cancellationToken)
    {
        var actor = RequireActor();
        var now = DateTime.UtcNow;

        var role = await rbacRepository.GetRoleWithRightsAsync(request.GroupId);
        if (role == null || role.Status != EntityStatus.Active)
        {
            throw new NotFoundException(nameof(Role), request.GroupId);
        }

        var permission = await permissionRepository.GetById(request.RightId);
        if (permission == null)
        {
            throw new NotFoundException(nameof(Permission), request.RightId);
        }

        if (IsProtectedGroup(role.Name))
        {
            throw new BadRequestException("The rights of a protected system group cannot be changed.");
        }

        var (_, members) = SplitMembers(role);
        if (members.Contains(actor))
        {
            throw new ForbiddenException("You cannot change the rights of an access group that grants you access.");
        }

        var row = role.RolePermissions.FirstOrDefault(rp => rp.PermissionId == request.RightId);

        if (row == null)
        {
            row = new RolePermission
            {
                Id = Guid.NewGuid(),
                RoleId = role.Id,
                PermissionId = permission.Id,
                Effect = request.Effect,
                Status = EntityStatus.Active,
                CreatedBy = actor,
                CreatedDate = now
            };
            await roleRightRepository.Add(row);
        }
        else
        {
            var wasLiveGrant = !row.IsDeleted && row.Status == EntityStatus.Active && row.Effect == PermissionEffect.Grant;
            if (wasLiveGrant && request.Effect != PermissionEffect.Grant)
            {
                await RequireRoleLeftAfterRightChangeAsync(role.Id, permission.Name);
            }

            // (RoleId, PermissionId) is the key, so a removed right must be revived, not re-inserted.
            row.Effect = request.Effect;
            row.IsDeleted = false;
            row.Status = EntityStatus.Active;
            row.UpdatedBy = actor;
            row.UpdatedDate = now;
            await roleRightRepository.Update(row);
        }

        return new Response<GroupRightRowDto>
        {
            Success = true,
            Message = "Group right saved successfully.",
            Data = new GroupRightRowDto(
                role.Id, permission.Id, role.Name, permission.Name, permission.Module,
                request.Effect == PermissionEffect.Grant ? "Grant" : "Deny", now, currentUserService.UserName)
        };
    }

    public async Task<Response<bool>> Handle(RemoveGroupRightCommandRequest request, CancellationToken cancellationToken)
    {
        var actor = RequireActor();

        var role = await rbacRepository.GetRoleWithRightsAsync(request.GroupId);
        if (role == null)
        {
            throw new NotFoundException(nameof(Role), request.GroupId);
        }

        var row = role.RolePermissions.FirstOrDefault(rp =>
            rp.PermissionId == request.RightId && !rp.IsDeleted && rp.Status == EntityStatus.Active);
        if (row == null)
        {
            throw new NotFoundException("Group right");
        }

        if (IsProtectedGroup(role.Name))
        {
            throw new BadRequestException("The rights of a protected system group cannot be changed.");
        }

        var (_, members) = SplitMembers(role);
        if (members.Contains(actor))
        {
            throw new ForbiddenException("You cannot change the rights of an access group that grants you access.");
        }

        if (row.Effect == PermissionEffect.Grant)
        {
            await RequireRoleLeftAfterRightChangeAsync(role.Id, row.Permission.Name);
        }

        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            auditContext.Reason = request.Reason!.Trim();
        }

        row.IsDeleted = true;
        row.UpdatedBy = actor;
        row.UpdatedDate = DateTime.UtcNow;
        await roleRightRepository.Update(row);

        return new Response<bool>
        {
            Success = true,
            Message = "Group right removed successfully.",
            Data = true
        };
    }

    // ── Direct user rights ─────────────────────────────────────────────────

    public async Task<Response<UserRightRowDto>> Handle(SetUserRightCommandRequest request, CancellationToken cancellationToken)
    {
        var actor = RequireActor();
        var now = DateTime.UtcNow;

        var user = await ResolveAssignableEmployeeAsync(request.UserId, actor, cancellationToken);

        var permission = await permissionRepository.GetById(request.RightId);
        if (permission == null)
        {
            throw new NotFoundException(nameof(Permission), request.RightId);
        }

        if (request.UserId == actor)
        {
            throw new ForbiddenException("You cannot grant or deny a right to yourself.");
        }

        await RequireOtherAdministratorAsync(request.UserId);

        var reviewRows = await userRightRepository.GetAll();
        var row = reviewRows.FirstOrDefault(ur => ur.UserId == request.UserId && ur.PermissionId == request.RightId);

        auditContext.Reason = request.Reason!.Trim();

        if (row == null)
        {
            row = new UserRight
            {
                Id = Guid.NewGuid(),
                UserId = request.UserId,
                PermissionId = permission.Id,
                Effect = request.Effect,
                Reason = request.Reason!.Trim(),
                Status = EntityStatus.Active,
                CreatedBy = actor,
                CreatedDate = now
            };
            await userRightRepository.Add(row);
        }
        else
        {
            row.Effect = request.Effect;
            row.Reason = request.Reason!.Trim();
            row.UpdatedBy = actor;
            row.UpdatedDate = now;
            await userRightRepository.Update(row);
        }

        return new Response<UserRightRowDto>
        {
            Success = true,
            Message = "User right saved successfully.",
            Data = new UserRightRowDto(
                row.UserId, permission.Id, user.Username, user.FullName, permission.Name, permission.Module,
                request.Effect == PermissionEffect.Grant ? "Grant" : "Deny", row.Reason, now, currentUserService.UserName)
        };
    }

    public async Task<Response<bool>> Handle(RemoveUserRightCommandRequest request, CancellationToken cancellationToken)
    {
        var actor = RequireActor();

        var rows = await userRightRepository.GetAll();
        var row = rows.FirstOrDefault(ur => ur.UserId == request.UserId && ur.PermissionId == request.RightId);
        if (row == null)
        {
            throw new NotFoundException("User right");
        }

        var user = await userRepository.GetById(request.UserId);
        if (user == null)
        {
            throw new NotFoundException(nameof(User), request.UserId);
        }

        if (request.UserId == actor)
        {
            throw new ForbiddenException("You cannot remove your own right assignment.");
        }

        await RequireOtherAdministratorAsync(request.UserId);

        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            auditContext.Reason = request.Reason!.Trim();
        }

        row.IsDeleted = true;
        row.UpdatedBy = actor;
        row.UpdatedDate = DateTime.UtcNow;
        await userRightRepository.Update(row);

        return new Response<bool>
        {
            Success = true,
            Message = "User right removed successfully.",
            Data = true
        };
    }

    // ── Rights catalog ─────────────────────────────────────────────────────

    public async Task<Response<AccessRightDto>> Handle(CreateAccessRightCommandRequest request, CancellationToken cancellationToken)
    {
        var dto = request.Dto!;
        var actor = RequireActor();
        var now = DateTime.UtcNow;

        var existing = await permissionRepository.GetAll();
        if (existing.Any(p => string.Equals(p.Name, dto.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BadRequestException($"A right with key '{dto.Name}' already exists.");
        }

        var permission = new Permission
        {
            Id = Guid.NewGuid(),
            Name = dto.Name.Trim(),
            Module = dto.Module.Trim(),
            Description = dto.Description?.Trim(),
            Status = EntityStatus.Active,
            CreatedBy = actor,
            CreatedDate = now
        };
        await permissionRepository.Add(permission);

        return new Response<AccessRightDto>
        {
            Success = true,
            Message = "Right created successfully.",
            Data = new AccessRightDto(
                permission.Id, permission.Name, permission.Module, permission.Description,
                0, 0, false, permission.Status.ToString(), permission.CreatedDate)
        };
    }

    public async Task<Response<AccessRightDto>> Handle(UpdateAccessRightCommandRequest request, CancellationToken cancellationToken)
    {
        var permission = await permissionRepository.GetById(request.Id);
        if (permission == null)
        {
            throw new NotFoundException(nameof(Permission), request.Id);
        }

        var actor = RequireActor();

        permission.Module = request.Dto!.Module?.Trim() ?? permission.Module;
        permission.Description = request.Dto.Description?.Trim();
        permission.UpdatedBy = actor;
        permission.UpdatedDate = DateTime.UtcNow;
        await permissionRepository.Update(permission);

        return new Response<AccessRightDto>
        {
            Success = true,
            Message = "Right updated successfully.",
            Data = new AccessRightDto(
                permission.Id, permission.Name, permission.Module, permission.Description,
                0, 0, false, permission.Status.ToString(), permission.UpdatedDate!.Value)
        };
    }

    public async Task<Response<bool>> Handle(DeleteAccessRightCommandRequest request, CancellationToken cancellationToken)
    {
        var permission = await permissionRepository.GetById(request.Id);
        if (permission == null)
        {
            throw new NotFoundException(nameof(Permission), request.Id);
        }

        if (Permissions.All.Any(d => d.Name == permission.Name))
        {
            throw new BadRequestException("Seeded system rights cannot be deleted.");
        }

        var roles = await rbacRepository.GetAllRolesWithRightsAsync();
        if (roles.Any(r => r.RolePermissions.Any(rp => rp.PermissionId == permission.Id)))
        {
            throw new BadRequestException("The right is still assigned to at least one access group. Remove those assignments first.");
        }

        var userRights = await userRightRepository.GetAll();
        if (userRights.Any(ur => ur.PermissionId == permission.Id))
        {
            throw new BadRequestException("The right is still assigned directly to at least one employee. Remove those assignments first.");
        }

        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            auditContext.Reason = request.Reason!.Trim();
        }

        var actor = RequireActor();
        permission.IsDeleted = true;
        permission.UpdatedBy = actor;
        permission.UpdatedDate = DateTime.UtcNow;
        await permissionRepository.Update(permission);

        return new Response<bool>
        {
            Success = true,
            Message = "Right deleted successfully.",
            Data = true
        };
    }

    // ── Safeguard helpers ──────────────────────────────────────────────────

    private Guid RequireActor()
    {
        if (!currentUserService.IsAuthenticated || currentUserService.UserId is not { } actor || actor == Guid.Empty)
        {
            throw new ForbiddenException("Authentication is required to manage access control.");
        }

        return actor;
    }

    /// <summary>
    /// Resolves the employee a grant targets. The pickers only offer active HR employees; an
    /// employee who has never signed in has no local row yet, so one is created from the verified
    /// HR/AD identity mapping and linked to their AD account by object GUID, which is what first
    /// sign-in matches on.
    /// </summary>
    private async Task<User> ResolveAssignableEmployeeAsync(Guid userId, Guid actor, CancellationToken cancellationToken)
    {
        var existing = await userRepository.GetById(userId);
        if (existing != null)
        {
            return existing;
        }

        var employee = await hrInfoRepository.FindActiveEmployeeByIdAsync(userId, cancellationToken);
        if (employee == null)
        {
            throw new NotFoundException("Active HR employee", userId);
        }

        return await employeeAccounts.EnsureEmployeeAccountAsync(
            userId,
            EmployeeNaming.DisplayName(employee),
            EmployeeNaming.UsernameHint(employee),
            actor,
            cancellationToken);
    }

    private async Task RequireOtherAdministratorAsync(Guid affectedUserId)
    {
        var administratorIds = await rbacRepository.GetActiveAdministratorUserIdsAsync();
        if (administratorIds.Contains(affectedUserId) &&
            administratorIds.All(id => id == affectedUserId))
        {
            throw new BadRequestException(
                "This employee is the only active administrator. Assign another administrator first.");
        }
    }

    private async Task RequireRoleLeftAfterRightChangeAsync(Guid roleId, string permissionName)
    {
        if (!ManagementKeys.Contains(permissionName, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var administratorsLeft = await rbacRepository.CountActiveAdministratorsExcludingRoleAsync(roleId);
        var directAdmins = await rbacRepository.GetActiveAdministratorUserIdsAsync();
        if (administratorsLeft == 0 && directAdmins.Count <= 1)
        {
            throw new BadRequestException(
                "This change would leave no active administrators. Assign another administrator first.");
        }
    }

    private (HashSet<Guid>, HashSet<Guid>) SplitMembers(Role role)
    {
        var ids = role.UserRoles
            .Where(ur => !ur.IsDeleted && ur.Status == EntityStatus.Active)
            .Select(ur => ur.UserId)
            .ToHashSet();
        return (ids, ids);
    }

    private bool IsProtectedGroup(string name) =>
        Roles.All.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static AccessGroupDto ToGroupDto(Role role, object? rolesCache) => new(
        role.Id,
        role.Name,
        role.Description,
        role.UserRoles.Count(ur => !ur.IsDeleted && ur.Status == EntityStatus.Active),
        role.RolePermissions.Count(rp => !rp.IsDeleted && rp.Status == EntityStatus.Active && rp.Effect == PermissionEffect.Grant),
        role.RolePermissions.Count(rp => !rp.IsDeleted && rp.Status == EntityStatus.Active && rp.Effect == PermissionEffect.Deny),
        role.Status == EntityStatus.Active && !role.IsDeleted,
        role.Status.ToString(),
        role.UpdatedDate ?? role.CreatedDate);
}
