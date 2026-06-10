namespace Solace.Common.Constants;

public static class AccountConstants
{
    // Str - for some reason string interpolation with string constants works, but it does not work with int, so need to have string variants for usage in attributes
    public const int MinUsernameLength = 3;
    public const string MinUsernameLengthStr = "3";
    public const int MaxUsernameLength = 16; // keep in sync with Solace.ApiServer.Controllers.Live.LoginController.GenerateUserId()
    public const string MaxUsernameLengthStr = "16"; // keep in sync with Solace.ApiServer.Controllers.Live.LoginController.GenerateUserId()

    public const int MinPasswordLength = 4;
    public const string MinPasswordLengthStr = "4";
    public const int MaxPasswordLength = 64;
    public const string MaxPasswordLengthStr = "64";

    public const int MinNameLength = 2;
    public const int MaxNameLength = 100;
}