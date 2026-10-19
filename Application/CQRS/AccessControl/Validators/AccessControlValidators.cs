using Application.CQRS.AccessControl.Requests.Commands;
using Domain.Constants;
using FluentValidation;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Application.CQRS.AccessControl.Validators;

public class CreateAccessGroupValidator : AbstractValidator<CreateAccessGroupCommandRequest>
{
    private static readonly Regex NamePattern = new("^[A-Za-z][A-Za-z0-9 ._-]{0,63}$", RegexOptions.Compiled);

    public CreateAccessGroupValidator()
    {
        RuleFor(x => x.Dto)
            .NotNull().WithMessage("Access group details are required.");
        When(x => x.Dto != null, () =>
        {
            RuleFor(x => x.Dto!.Name)
                .NotEmpty().WithMessage("Group name is required.")
                .Matches(NamePattern)
                    .WithMessage("Group name may use letters, digits, spaces, dots, underscores or hyphens (max 64).")
                .Must(name => !Roles.All.Any(r => string.Equals(r, name, StringComparison.OrdinalIgnoreCase)))
                    .WithMessage("This name is reserved for a protected system group.");

            RuleFor(x => x.Dto!.Description)
                .MaximumLength(500).WithMessage("Description must be 500 characters or fewer.");
        });
    }
}

public class UpdateAccessGroupValidator : AbstractValidator<UpdateAccessGroupCommandRequest>
{
    public UpdateAccessGroupValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Access group is required.");
        RuleFor(x => x.Dto)
            .NotNull().WithMessage("Access group details are required.");
        When(x => x.Dto != null, () =>
        {
            RuleFor(x => x.Dto!.Description)
                .MaximumLength(500).WithMessage("Description must be 500 characters or fewer.");
        });
    }
}

public class SetAccessGroupStatusValidator : AbstractValidator<SetAccessGroupStatusCommandRequest>
{
    public SetAccessGroupStatusValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Access group is required.");
        RuleFor(x => x.Dto).NotNull().WithMessage("Status is required.");
    }
}

public class AssignGroupMembershipValidator : AbstractValidator<AssignGroupMembershipCommandRequest>
{
    public AssignGroupMembershipValidator()
    {
        RuleFor(x => x.GroupId).NotEmpty().WithMessage("Access group is required.");
        RuleFor(x => x.UserId).NotEmpty().WithMessage("Employee is required.");
    }
}

public class RemoveGroupMembershipValidator : AbstractValidator<RemoveGroupMembershipCommandRequest>
{
    public RemoveGroupMembershipValidator()
    {
        RuleFor(x => x.GroupId).NotEmpty().WithMessage("Access group is required.");
        RuleFor(x => x.UserId).NotEmpty().WithMessage("Employee is required.");
    }
}

public class SetGroupRightValidator : AbstractValidator<SetGroupRightCommandRequest>
{
    public SetGroupRightValidator()
    {
        RuleFor(x => x.GroupId).NotEmpty().WithMessage("Access group is required.");
        RuleFor(x => x.RightId).NotEmpty().WithMessage("Right is required.");
        RuleFor(x => x.Effect)
            .IsInEnum().WithMessage("Effect must be Grant or Deny.");
    }
}

public class RemoveGroupRightValidator : AbstractValidator<RemoveGroupRightCommandRequest>
{
    public RemoveGroupRightValidator()
    {
        RuleFor(x => x.GroupId).NotEmpty().WithMessage("Access group is required.");
        RuleFor(x => x.RightId).NotEmpty().WithMessage("Right is required.");
    }
}

public class SetUserRightValidator : AbstractValidator<SetUserRightCommandRequest>
{
    public SetUserRightValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("Employee is required.");
        RuleFor(x => x.RightId).NotEmpty().WithMessage("Right is required.");
        RuleFor(x => x.Effect)
            .IsInEnum().WithMessage("Effect must be Grant or Deny.");
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A justification reason is required for every direct user right.")
            .MaximumLength(500).WithMessage("Reason must be 500 characters or fewer.");
    }
}

public class RemoveUserRightValidator : AbstractValidator<RemoveUserRightCommandRequest>
{
    public RemoveUserRightValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("Employee is required.");
        RuleFor(x => x.RightId).NotEmpty().WithMessage("Right is required.");
    }
}

public class CreateAccessRightValidator : AbstractValidator<CreateAccessRightCommandRequest>
{
    private static readonly Regex KeyPattern =
        new(Domain.Constants.Permissions.PermissionKeyPattern, RegexOptions.Compiled);

    public CreateAccessRightValidator()
    {
        RuleFor(x => x.Dto)
            .NotNull().WithMessage("Right details are required.");
        When(x => x.Dto != null, () =>
        {
            RuleFor(x => x.Dto!.Name)
                .NotEmpty().WithMessage("Right key is required.")
                .Matches(KeyPattern)
                    .WithMessage("Right key must look like 'module:resource[.action]' — lowercase and dotted.");

            RuleFor(x => x.Dto.Module)
                .NotEmpty().WithMessage("Module is required.")
                .MaximumLength(100).WithMessage("Module must be 100 characters or fewer.")
                .Must((request, module) => module.Length > 0 &&
                    request.Dto!.Name.StartsWith(module + ":", StringComparison.OrdinalIgnoreCase))
                    .WithMessage("Module must match the module segment of the right key.");

            RuleFor(x => x.Dto.Description)
                .MaximumLength(500).WithMessage("Description must be 500 characters or fewer.");
        });
    }
}

public class UpdateAccessRightValidator : AbstractValidator<UpdateAccessRightCommandRequest>
{
    public UpdateAccessRightValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Right is required.");
        RuleFor(x => x.Dto).NotNull().WithMessage("Right details are required.");
        When(x => x.Dto != null, () =>
        {
            RuleFor(x => x.Dto!.Module)
                .MaximumLength(100).WithMessage("Module must be 100 characters or fewer.");

            RuleFor(x => x.Dto!.Description)
                .MaximumLength(500).WithMessage("Description must be 500 characters or fewer.");
        });
    }
}

public class DeleteAccessRightValidator : AbstractValidator<DeleteAccessRightCommandRequest>
{
    public DeleteAccessRightValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Right is required.");
    }
}
