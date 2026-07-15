using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RevitAPP.Licensing;

/// <summary>Interface de mock dang nhap trong test.</summary>
public interface IOAuthSignIn
{
    /// <summary>Mo browser, thuc hien OAuth, tra ve email da xac thuc. Null neu nguoi dung huy.</summary>
    Task<string?> SignInAsync(CancellationToken ct = default);
}

/// <summary>
/// Google OAuth Desktop flow (PKCE + loopback 127.0.0.1). Mo browser he thong,
/// nhan authorization code qua HttpListener, doi lay id_token, giai ma email tu payload JWT.
/// </summary>
public sealed class GoogleOAuthClient : IOAuthSignIn
{
    private static readonly HttpClient Http = new();

    public async Task<string?> SignInAsync(CancellationToken ct = default)
    {
        // 1) PKCE
        var codeVerifier = RandomUrlToken(32);
        byte[] challengeHash;
        using (var sha = SHA256.Create())
            challengeHash = sha.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
        var codeChallenge = Base64Url(challengeHash);
        var state = RandomUrlToken(16);

        // 2) Loopback listener tren port ephemeral
        var port = GetFreePort();
        var redirectUri = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        // 3) Mo browser toi trang consent
        var authUrl =
            $"{LicenseConfig.AuthEndpoint}?response_type=code" +
            $"&client_id={Uri.EscapeDataString(LicenseConfig.ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&scope={Uri.EscapeDataString(LicenseConfig.OAuthScope)}" +
            $"&code_challenge={codeChallenge}&code_challenge_method=S256" +
            $"&state={state}";
        OpenBrowser(authUrl);

        // 4) Cho redirect (timeout 3 phut)
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(3));

        HttpListenerContext context;
        var getContextTask = listener.GetContextAsync();
        // WaitAsync khong co tren net48 -> dung WhenAny voi Delay theo timeout token.
        var timeoutTask = Task.Delay(Timeout.Infinite, timeoutCts.Token);
        var finished = await Task.WhenAny(getContextTask, timeoutTask);
        if (finished != getContextTask)
            return null; // nguoi dung khong hoan tat trong 3 phut (timeout/huy)
        context = await getContextTask;

        var query = context.Request.QueryString;
        var code = query["code"];
        var returnedState = query["state"];
        await WriteBrowserResponse(context, code != null && returnedState == state);

        if (string.IsNullOrEmpty(code) || returnedState != state)
            return null;

        // 5) Doi code lay token
        var idToken = await ExchangeCodeForIdTokenAsync(code!, codeVerifier, redirectUri, ct);
        if (idToken == null) return null;

        // 6) Giai ma email tu id_token (payload JWT). Lay truc tiep tu Google qua HTTPS -> khong verify chu ky.
        return ExtractEmail(idToken);
    }

    private static async Task<string?> ExchangeCodeForIdTokenAsync(
        string code, string codeVerifier, string redirectUri, CancellationToken ct)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = LicenseConfig.ClientId,
            ["client_secret"] = LicenseConfig.ClientSecret,
            ["code_verifier"] = codeVerifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri
        });

        using var resp = await Http.PostAsync(LicenseConfig.TokenEndpoint, form, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("id_token", out var t) ? t.GetString() : null;
    }

    private static string? ExtractEmail(string idToken)
    {
        var parts = idToken.Split('.');
        if (parts.Length < 2) return null;
        var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        using var doc = JsonDocument.Parse(payloadJson);
        return doc.RootElement.TryGetProperty("email", out var e) ? e.GetString() : null;
    }

    // ---- helpers ----

    private static int GetFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static void OpenBrowser(string url) =>
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });

    private static async Task WriteBrowserResponse(HttpListenerContext ctx, bool ok)
    {
        var html = ok
            ? "<html><body style='font-family:sans-serif;text-align:center;padding-top:60px'>" +
              "<h2>Dang nhap thanh cong</h2><p>Ban co the dong tab nay va quay lai Revit.</p></body></html>"
            : "<html><body style='font-family:sans-serif;text-align:center;padding-top:60px'>" +
              "<h2>Dang nhap that bai</h2><p>Vui long thu lai trong Revit.</p></body></html>";
        var buf = Encoding.UTF8.GetBytes(html);
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = buf.Length;
        await ctx.Response.OutputStream.WriteAsync(buf, 0, buf.Length);
        ctx.Response.OutputStream.Close();
    }

    private static string RandomUrlToken(int bytes)
    {
        var buf = new byte[bytes];
        using (var rng = RandomNumberGenerator.Create())
            rng.GetBytes(buf);
        return Base64Url(buf);
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
    }
}
