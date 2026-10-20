using System.Security.Claims;
using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

// Identity of the requesting user, passed to every service method that touches protected data.
public sealed record UserContext(int UserId, UserRole Role, string? Department)
{
    // UserRole enum order is the hierarchy: Employee < TeamLead < ProjectManager < Administrator
    public bool IsAtLeast(UserRole role) => Role >= role;

    public bool IsAdministrator => Role == UserRole.Administrator;
}

public static class ClaimsPrincipalExtensions
{
    public const string DepartmentClaimType = "Department";

    public static UserContext ToUserContext(this ClaimsPrincipal principal)
    {
        var idValue = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(idValue, out var userId))
            throw new InvalidOperationException("Authenticated user id claim is missing.");

        var role = Enum.TryParse<UserRole>(principal.FindFirst(ClaimTypes.Role)?.Value, out var parsed)
            ? parsed
            : UserRole.Employee;

        var department = principal.FindFirst(DepartmentClaimType)?.Value;
        return new UserContext(userId, role, string.IsNullOrWhiteSpace(department) ? null : department);
    }
}
