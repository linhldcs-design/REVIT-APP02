namespace RevitAPP.Licensing;

/// <summary>
/// API license dung chung boi addin RevitAPP (dang nhap) va 4 MCP tool (gate).
///
/// Luong:
///  - SignInAsync: mo browser OAuth -> lay email -> verify online -> ghi cache.
///  - GetStateAsync: doc email tu cache, sau do luon re-verify online.
///    Cache chi nho tai khoan/trang thai hien thi, khong tu cap quyen su dung.
///    Offline/timeout -> fail closed de thu hoi license co hieu luc o lan bam lenh ke tiep.
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
        // Giu tham so de tuong thich source/binary voi caller cu. Cache grace khong con
        // duoc dung de cap quyen; server la nguon su that cho moi lan kiem tra.
        _ = graceDays;
    }

    /// <summary>Dang nhap Google + verify. Goi tu ribbon UI (co browser). Tra ve state sau khi dang nhap.</summary>
    public async Task<LicenseState> SignInAsync(CancellationToken ct = default)
    {
        var initialSession = _cache.ReadOrCreateSessionSnapshot();
        var email = await _oauth.SignInAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(email))
            return LicenseState.NotSignedIn();

        var result = await _verifier.VerifyAsync(email!, ct).ConfigureAwait(false);
        var newSession = new LicenseCacheData
        {
            Email = email,
            Expiry = result.Expiry,
            SessionId = Guid.NewGuid().ToString("N"),
            LastVerifiedUtc = _utcNow().ToString("O"),
            Allowed = result.Allowed
        };
        if (!TryCommitSignIn(initialSession, newSession))
            return LicenseState.NotSignedIn();

        if (!result.Allowed)
        {
            return LicenseState.Denied(email, DescribeError(result.Error));
        }

        return LicenseState.Valid(email!, result.Expiry);
    }

    /// <summary>Xoa cache (dang xuat).</summary>
    public void SignOut() => _cache.Clear();

    /// <summary>
    ///     Helper dong bo cho command UI (nut ribbon): tra ve (ok, message).
    ///     ok=true -> cho phep chay. ok=false -> hien message roi return, KHONG ve thep.
    ///     Luon goi server de thay doi tren Google Sheet co hieu luc o lan bam lenh ke tiep.
    /// </summary>
    public static (bool Ok, string Message) EnsureValid()
    {
        try
        {
            // Revit calls this method on its UI thread. Run the async verification on the thread pool
            // so an expired cache cannot deadlock while an HTTP continuation waits for Revit's context.
            var state = Task.Run(() => Instance.GetStateAsync()).GetAwaiter().GetResult();
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
    /// Trang thai hien tai. Cache chi cung cap email; server quyet dinh quyen moi lan goi.
    /// Dung boi ca UI, Ribbon, Chat va MCP gate.
    /// </summary>
    public async Task<LicenseState> GetStateAsync(CancellationToken ct = default)
    {
        var data = _cache.ReadOrCreateSessionSnapshot();
        if (string.IsNullOrEmpty(data.Email))
            return LicenseState.NotSignedIn();

        // Luon re-verify. Khong duoc tin Allowed/Expiry cu trong cache vi admin co the
        // thu hoi, rut ngan hoac gia han license tren server ma nguoi dung khong dang xuat.
        try
        {
            var result = await _verifier.VerifyAsync(data.Email!, ct).ConfigureAwait(false);
            var sessionStillCurrent = TryUpdateCurrentSession(data.SessionId!, new LicenseCacheData
            {
                Email = data.Email,
                Expiry = result.Expiry ?? data.Expiry,
                SessionId = data.SessionId ?? Guid.NewGuid().ToString("N"),
                LastVerifiedUtc = _utcNow().ToString("O"),
                Allowed = result.Allowed
            });
            if (!sessionStillCurrent)
                return LicenseState.NotSignedIn();

            return result.Allowed
                ? LicenseState.Valid(data.Email!, result.Expiry ?? data.Expiry)
                : LicenseState.Denied(data.Email, DescribeError(result.Error));
        }
        catch
        {
            var current = _cache.Read();
            if (current == null ||
                !string.Equals(current.Email, data.Email, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(current.SessionId, data.SessionId, StringComparison.Ordinal))
                return LicenseState.NotSignedIn();

            // Fail closed: neu cho phep cache offline thi license da bi thu hoi van dung duoc.
            return LicenseState.Expired(data.Email, data.Expiry,
                "Khong ket noi duoc server cap phep. Can co mang de kiem tra license.");
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

    private bool TryUpdateCurrentSession(string expectedSessionId, LicenseCacheData data)
    {
        try
        {
            return _cache.WriteIfSessionMatches(expectedSessionId, data);
        }
        catch
        {
            // Server da quyet dinh quyen cua lan goi hien tai. Loi luu cache khong duoc
            // bien mot license hop le thanh false-deny; lan bam sau se verify online lai.
            return true;
        }
    }

    private bool TryCommitSignIn(LicenseCacheData initialSession, LicenseCacheData data)
    {
        try
        {
            return _cache.WriteIfSessionMatches(
                initialSession.SessionId!,
                data);
        }
        catch
        {
            return false;
        }
    }

}
