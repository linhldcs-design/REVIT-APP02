namespace RevitAPP.Licensing;

/// <summary>
/// API license dung chung boi addin RevitAPP (dang nhap) va 4 MCP tool (gate).
///
/// Luong:
///  - SignInAsync: mo browser OAuth -> lay email -> verify online -> ghi cache.
///  - GetStateAsync: doc cache; con trong grace (7 ngay) -> Valid khong goi mang;
///    qua grace -> re-verify online; offline + qua grace -> Expired (chan).
///
/// Singleton <see cref="Instance"/> dung cho MCP tool (khong co DI); addin co the tu new voi
/// dependency inject de test.
/// </summary>
public sealed class LicenseService
{
    private static readonly Lazy<LicenseService> Lazy = new(() => new LicenseService());
    public static LicenseService Instance => Lazy.Value;

    private readonly IOAuthSignIn _oauth;
    private readonly ILicenseVerifier _verifier;
    private readonly LicenseCache _cache;
    private readonly Func<DateTime> _utcNow;
    private readonly int _graceDays;

    public LicenseService(
        IOAuthSignIn? oauth = null,
        ILicenseVerifier? verifier = null,
        LicenseCache? cache = null,
        Func<DateTime>? utcNow = null,
        int graceDays = LicenseConfig.CacheGraceDays)
    {
        _oauth = oauth ?? new GoogleOAuthClient();
        _verifier = verifier ?? new AppsScriptClient();
        _cache = cache ?? new LicenseCache();
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _graceDays = graceDays;
    }

    /// <summary>Dang nhap Google + verify. Goi tu ribbon UI (co browser). Tra ve state sau khi dang nhap.</summary>
    public async Task<LicenseState> SignInAsync(CancellationToken ct = default)
    {
        var email = await _oauth.SignInAsync(ct);
        if (string.IsNullOrEmpty(email))
            return LicenseState.NotSignedIn();

        var result = await _verifier.VerifyAsync(email!, ct);
        if (!result.Allowed)
        {
            // Ghi cache "khong duoc phep" de UI hien ly do, nhung khong cho dung.
            _cache.Write(new LicenseCacheData
            {
                Email = email,
                Expiry = result.Expiry,
                LastVerifiedUtc = _utcNow().ToString("O"),
                Allowed = false
            });
            return LicenseState.Denied(email, DescribeError(result.Error));
        }

        _cache.Write(new LicenseCacheData
        {
            Email = email,
            Expiry = result.Expiry,
            LastVerifiedUtc = _utcNow().ToString("O"),
            Allowed = true
        });
        return LicenseState.Valid(email!, result.Expiry);
    }

    /// <summary>Xoa cache (dang xuat).</summary>
    public void SignOut() => _cache.Clear();

    /// <summary>
    ///     Helper dong bo cho command UI (nut ribbon): tra ve (ok, message).
    ///     ok=true -> cho phep chay. ok=false -> hien message roi return, KHONG ve thep.
    ///     Doc cache (nhanh); chi goi mang khi cache qua grace.
    /// </summary>
    public static (bool Ok, string Message) EnsureValid()
    {
        try
        {
            var state = Instance.GetStateAsync().GetAwaiter().GetResult();
            if (state.IsValid) return (true, string.Empty);
            return (false,
                $"Chua kich hoat ban quyen: {state.Reason}.\n\n" +
                "Mo ribbon RevitAPP -> nut \"License\" -> Dang nhap Google bang tai khoan da duoc cap quyen.");
        }
        catch (Exception ex)
        {
            return (false, "Loi kiem tra ban quyen: " + ex.Message +
                           "\n\nMo ribbon RevitAPP -> License de dang nhap lai.");
        }
    }

    /// <summary>
    /// Trang thai hien tai. Doc cache truoc; chi goi mang khi cache qua grace.
    /// Dung boi ca UI (hien status) va MCP gate (chan/cho).
    /// </summary>
    public async Task<LicenseState> GetStateAsync(CancellationToken ct = default)
    {
        var data = _cache.Read();
        if (data == null || string.IsNullOrEmpty(data.Email))
            return LicenseState.NotSignedIn();

        // Expiry qua khu -> het han tuyet doi (khong can goi mang).
        if (IsExpiryPassed(data.Expiry))
            return LicenseState.Expired(data.Email, data.Expiry, "License da het han");

        // Cache con trong grace -> tin cache, khong goi mang.
        if (data.Allowed && IsWithinGrace(data.LastVerifiedUtc))
            return LicenseState.Valid(data.Email!, data.Expiry);

        // Qua grace (hoac lan truoc bi denied) -> re-verify online.
        try
        {
            var result = await _verifier.VerifyAsync(data.Email!, ct);
            _cache.Write(new LicenseCacheData
            {
                Email = data.Email,
                Expiry = result.Expiry ?? data.Expiry,
                LastVerifiedUtc = _utcNow().ToString("O"),
                Allowed = result.Allowed
            });
            return result.Allowed
                ? LicenseState.Valid(data.Email!, result.Expiry ?? data.Expiry)
                : LicenseState.Denied(data.Email, DescribeError(result.Error));
        }
        catch
        {
            // Offline + qua grace -> chan (theo chinh sach: grace = 7 ngay ke tu verify cuoi).
            return LicenseState.Expired(data.Email, data.Expiry,
                "Khong ket noi duoc server va cache da qua han. Can dang nhap lai khi co mang.");
        }
    }

    /// <summary>Dich ma loi tu server -> thong bao tieng Viet.</summary>
    private static string DescribeError(string? error) => error switch
    {
        "not_found" => "Email chua duoc cap quyen",
        "device_limit" => "Da vuot so may cho phep cho tai khoan nay. Lien he nha cung cap de tang so may hoac go bot may cu.",
        "expired" => "License da het han",
        null => "License da het han",
        _ => $"Khong duoc phep ({error})"
    };

    private bool IsWithinGrace(string? lastVerifiedUtc)
    {
        if (!DateTime.TryParse(lastVerifiedUtc, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var last))
            return false;
        return _utcNow() - last.ToUniversalTime() <= TimeSpan.FromDays(_graceDays);
    }

    private bool IsExpiryPassed(string? expiry)
    {
        // expiry dang yyyy-MM-dd. Cho phep het ngay do (23:59:59).
        if (!DateTime.TryParse(expiry, null,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var d))
            return false; // khong parse duoc -> khong chan boi expiry, de verify quyet dinh
        return _utcNow() > d.Date.AddDays(1).AddSeconds(-1);
    }
}
