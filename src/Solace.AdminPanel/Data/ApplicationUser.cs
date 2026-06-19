using Microsoft.AspNetCore.Identity;

namespace Solace.AdminPanel.Data;

internal sealed class ApplicationUser : IdentityUser
{
    public List<Guid> LinkedInGameAccounts { get; set; } = [];
}
