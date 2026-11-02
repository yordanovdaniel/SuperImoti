using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Claims;

namespace SuperImoti.Web.Infrastructure;

public static class EnumExtensions
{
    /// <summary>Returns the [Display(Name = …)] text of an enum value, e.g. DealKind.Sale → "Продажба".</summary>
    public static string GetDisplayName(this Enum value)
    {
        var member = value.GetType().GetMember(value.ToString()).FirstOrDefault();
        return member?.GetCustomAttribute<DisplayAttribute>()?.GetName() ?? value.ToString();
    }
}

public static class ClaimsPrincipalExtensions
{
    public static string? GetUserId(this ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier);

    public static bool IsAdmin(this ClaimsPrincipal user) => user.IsInRole(AppRoles.Administrator);

    public static string GetDisplayName(this ClaimsPrincipal user) =>
        user.FindFirstValue(AppClaimsPrincipalFactory.DisplayNameClaim) ?? user.Identity?.Name ?? string.Empty;
}
