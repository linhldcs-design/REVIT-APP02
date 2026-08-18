namespace RevitAPP.Licensing;

/// <summary>
/// Cau hinh license. Cac gia tri cong khai duoc khai bao trong source; credentials
/// phai duoc cung cap bang bien moi truong (xem docs/license-google-setup.md).
/// </summary>
public static class LicenseConfig
{
    /// <summary>Google OAuth Client ID (Desktop app, dung PKCE).</summary>
    public const string ClientId =
        "1057703492407-9854f0gv80pu5pni13jbe4l2osdn3b2u.apps.googleusercontent.com";

    /// <summary>URL Apps Script web app verify email (POST { email, secret }).</summary>
    public const string AppsScriptUrl =
        "https://script.google.com/macros/s/AKfycbwNiaP9ZN5MJWBybhBFXz9okSkFwUIYq6diyML2fjK0wDEf-arFtI4ZyBg9vFjp5QtLpg/exec";

    /// <summary>Shared secret sent with license verification requests.</summary>
    public static string SharedSecret =>
#if REVITAPP_EMBED_LICENSE_SECRET
        ReleaseLicenseSecrets.SharedSecret;
#else
        GetRequiredEnvironmentVariable("REVITAPP_LICENSE_SHARED_SECRET");
#endif

    /// <summary>So ngay cache verify con hieu luc khi offline. Qua han + offline = chan.</summary>
    public const int CacheGraceDays = 7;

    /// <summary>OAuth scope toi thieu de lay email da xac thuc.</summary>
    public const string OAuthScope = "openid email profile";

    public const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    /// <summary>Thu muc luu cache license: %AppData%\RevitAPP.</summary>
    public static string DataDir =>
        System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "RevitAPP");

    public static string CacheFile => System.IO.Path.Combine(DataDir, "license.json");

    private static string GetRequiredEnvironmentVariable(string name)
    {
        var value = System.Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new System.InvalidOperationException(
            $"Missing required environment variable '{name}'. " +
            "See docs/license-google-setup.md for configuration instructions.");
    }
}
