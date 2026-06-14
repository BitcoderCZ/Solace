using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Solace.ApiServer.Models;
using Solace.ApiServer.Utils;

namespace Solace.ApiServer.Controllers.XboxLive.Auth;

[Route("title/authenticate")]
[Route("title.auth.xboxlive.com/title/authenticate")]
internal sealed class TitleController : SolaceControllerBase
{
    private readonly CryptoSecrets _cryptoSecrets;

    private readonly int _xboxLiveTokenValidityMinutes;

    public TitleController(CryptoSecrets cryptoSecrets, IConfiguration configuration)
    {
        _cryptoSecrets = cryptoSecrets;

        _xboxLiveTokenValidityMinutes = configuration.GetValue<int>("Authentication:XboxLive:TokenValidityMinutes");
    }

    internal sealed record AuthenticateRequest(
        AuthenticateRequest.PropertiesR Properties,
        string RelyingParty,
        string TokenType
    )
    {
        internal sealed record PropertiesR(
            string AuthMethod,
            string DeviceToken,
            string RpsTicket,
            string SiteName
        );
    }

    private sealed record AuthenticateResponse(
        string IssueInstant,
        string NotAfter,
        string Token,
        Dictionary<string, Dictionary<string, string>> DisplayClaims
    );

    [HttpPost]
#pragma warning disable IDE0060 // Remove unused parameter
    public ContentHttpResult Authenticate([FromBody] AuthenticateRequest request)
#pragma warning restore IDE0060 // Remove unused parameter
    {
        var tokenValidity = ValidityDatePair.Create(_xboxLiveTokenValidityMinutes);
        var token = new Tokens.Xbox.TitleToken()
        {
            Tid = "2037747551",
        };

        return JsonPascalCase(new AuthenticateResponse(
            tokenValidity.IssuedStr,
            tokenValidity.ExpiresStr,
            JwtUtils.Sign<Tokens.Xbox.AuthToken>(token, _cryptoSecrets.LiveAuthTokenSecret, tokenValidity),
            new()
            {
                ["xdi"] = new()
                {
                    ["tid"] = token.Tid,
                },
            }
        ));
    }
}
