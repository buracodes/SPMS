using Application.DTOS.AccessControl;
using System;

namespace Application.Services;

/// <summary>
/// Shared HR naming conventions for every employee picker and for account provisioning.
/// HR remains the authoritative employee directory.
/// </summary>
public static class EmployeeNaming
{
    /// <summary>HR's LastName is the grandfather's name, so the corporate convention is first name + father's name.</summary>
    public static string DisplayName(EmployeeHRInfoDto employee)
    {
        var first = employee.FirstName?.Trim() ?? string.Empty;
        var middle = employee.MiddleName?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(middle))
        {
            return $"{first} {middle}";
        }

        var full = $"{first} {employee.LastName?.Trim()}".Trim();
        return string.IsNullOrEmpty(full) ? employee.Email?.Trim() ?? string.Empty : full;
    }

    public static string UsernameHint(EmployeeHRInfoDto employee)
    {
        const string corporateDomain = "@ecx.com.et";
        var email = employee.Email?.Trim() ?? string.Empty;
        if (email.EndsWith(corporateDomain, StringComparison.OrdinalIgnoreCase))
        {
            return email[..^corporateDomain.Length];
        }

        return string.IsNullOrWhiteSpace(employee.IdCode) ? string.Empty : $"emp-{employee.IdCode.Trim()}";
    }
}
