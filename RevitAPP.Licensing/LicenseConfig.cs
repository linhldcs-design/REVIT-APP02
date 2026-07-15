namespace RevitAPP.Licensing;

/// <summary>
/// Cau hinh license. Cac gia tri ClientId/AppsScriptUrl/SharedSecret duoc deploy tren Google
/// (xem docs/license-google-setup.md). Client secret cua Desktop app KHONG phai bi mat that su
/// (Google coi la public) nen chap nhan nhung trong DLL.
/// </summary>
public static class LicenseConfig
{
    /// <summary>Google OAuth Client ID (Desktop app, dung PKCE).</summary>
    public const string ClientId =
        "1057703492407-9854f0gv80pu5pni13jbe4l2osdn3b2u.apps.googleusercontent.com";

    /// <summary>Client secret cua Desktop app. Google coi la public cho desktop flow.</summary>
    public const string ClientSecret =
        "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";

    /// <summary>URL Apps Script web app verify email (POST { email, secret }).</summary>
    public const string AppsScriptUrl =
        "https://script.google.com/macros/s/AKfycbwNiaP9ZN5MJWBybhBFXz9okSkFwUIYq6diyML2fjK0wDEf-arFtI4ZyBg9vFjp5QtLpg/exec";

    /// <summary>Shared secret goi kem khi verify (chan spam, khong phai bao mat tuyet doi).</summary>
    public const string SharedSecret = "rvtapp_9xK2mP7qL4wZ8nT3";

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
}
