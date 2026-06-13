using System.Security.Claims;

namespace Solace.AdminPanel.Utils;

public static class ClaimsPrincipalExtensions
{
    extension (ClaimsPrincipal principal)
    {
        public bool HasPermission(string permission)
            => principal?.HasClaim("Permission", permission) ?? false;
    }
}