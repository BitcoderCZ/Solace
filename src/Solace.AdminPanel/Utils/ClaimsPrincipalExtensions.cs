using System.Security.Claims;

namespace Solace.AdminPanel.Utils;

internal static class ClaimsPrincipalExtensions
{
    extension (ClaimsPrincipal principal)
    {
        public bool HasPermission(string permission)
            => principal?.HasClaim("Permission", permission) ?? false;
    }
}