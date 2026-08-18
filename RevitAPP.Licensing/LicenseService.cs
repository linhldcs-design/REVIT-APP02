namespace RevitAPP.Licensing;

/// <summary>
/// API license dung chung boi addin RevitAPP (dang nhap) va 4 MCP tool (gate).
///
/// Luong:
///  - SignInAsync: mo browser OAuth -> lay email -> verify online -> ghi cache.
///  - GetStateAsync: explicit online verification cho dang nhap/UI/background worker.
///  - EnsureValid: command gate chi doc snapshot moi, khong bao gio cho HTTP tren UI thread.
///  - Background worker re-verify dinh ky; snapshot qua tuoi se fail closed ngay.
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
    private readonly object _refreshSync = new();
    private readonly object _snapshotSync = new();
    private readonly SemaphoreSlim _verifyGate = new(1, 1);
    private Task<LicenseState>? _refreshTask;
    private CancellationTokenSource? _backgroundCts;
    private Task? _backgroundTask;
    private LicenseState? _verifiedSnapshot;
    private DateTime _verifiedSnapshotUtc;
    private string? _verifiedSessionId;
    private long _snapshotGeneration;

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
        var generation = CaptureSnapshotGeneration();
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

        var state = BuildServerVerifiedState(email!, result);
        SetVerifiedSnapshot(newSession.SessionId!, generation, state);

        if (!result.Allowed)
        {
            return state;
        }

        return state;
    }

    /// <summary>Xoa cache (dang xuat).</summary>
    public void SignOut()
    {
        ClearVerifiedSnapshot();
        _cache.Clear();
    }

    /// <summary>
    ///     Helper dong bo cho command UI (nut ribbon): tra ve (ok, message).
    ///     ok=true -> cho phep chay. ok=false -> hien message roi return, KHONG ve thep.
    ///     Chi doc snapshot cuc bo de khong khoa UI Revit. Background worker cap nhat server.
    /// </summary>
    public static (bool Ok, string Message) EnsureValid()
    {
        try
        {
            var service = Instance;
            var state = service.GetCachedState();
            service.RefreshInBackgroundIfDue();
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
        await _verifyGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await GetStateOnlineCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _verifyGate.Release();
        }
    }

    private async Task<LicenseState> GetStateOnlineCoreAsync(CancellationToken ct)
    {
        var generation = CaptureSnapshotGeneration();
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
                Expiry = result.Expiry,
                SessionId = data.SessionId ?? Guid.NewGuid().ToString("N"),
                LastVerifiedUtc = _utcNow().ToString("O"),
                Allowed = result.Allowed
            });
            if (!sessionStillCurrent)
                return LicenseState.NotSignedIn();

            var state = BuildServerVerifiedState(data.Email!, result);
            SetVerifiedSnapshot(data.SessionId!, generation, state);
            return state;
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
        "unauthorized_v2" => "Cau hinh cap phep cua ung dung khong khop may chu. Vui long cap nhat RevitAPP.",
        "unauthorized" => "Cau hinh cap phep cua ung dung khong khop may chu. Vui long cap nhat RevitAPP.",
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

    /// <summary>
    /// Doc snapshot trong RAM da duoc server xac minh ma khong goi mang. File cache tren dia
    /// khong bao gio la nguon cap quyen. Ham nay duoc phep goi tren UI thread.
    /// </summary>
    public LicenseState GetCachedState()
    {
        LicenseState? snapshot;
        DateTime verifiedUtc;
        string? sessionId;
        lock (_snapshotSync)
        {
            snapshot = _verifiedSnapshot;
            verifiedUtc = _verifiedSnapshotUtc;
            sessionId = _verifiedSessionId;
        }

        var identity = _cache.Read();
        if (identity == null || string.IsNullOrEmpty(identity.Email))
            return LicenseState.NotSignedIn();

        if (snapshot == null ||
            !string.Equals(identity.SessionId, sessionId, StringComparison.Ordinal))
        {
            return LicenseState.Expired(identity.Email, identity.Expiry,
                "Dang cap nhat ban quyen. Vui long bam lai sau giay lat");
        }

        var age = _utcNow() - verifiedUtc;
        if (age < TimeSpan.Zero || age > LicenseConfig.MaximumSnapshotAge)
        {
            return LicenseState.Expired(identity.Email, identity.Expiry,
                "Dang cap nhat ban quyen. Vui long bam lai sau giay lat");
        }

        if (!snapshot.IsValid) return snapshot;

        if (!TryParseValidExpiry(snapshot.Expiry, out var expiry) ||
            _utcNow().Date > expiry.Date)
            return LicenseState.Expired(snapshot.Email, snapshot.Expiry, "License da het han");

        return snapshot;
    }

    /// <summary>
    /// Queue mot online refresh neu snapshot da toi chu ky. Single-flight trong process;
    /// caller khong cho task nay.
    /// </summary>
    public void RefreshInBackgroundIfDue()
    {
        DateTime verifiedUtc;
        lock (_snapshotSync) verifiedUtc = _verifiedSnapshotUtc;
        if (verifiedUtc != default &&
            _utcNow() - verifiedUtc < LicenseConfig.BackgroundRefreshInterval) return;

        var data = _cache.Read();
        if (data == null || string.IsNullOrEmpty(data.Email)) return;

        _ = RefreshInBackgroundAsync();
    }

    /// <summary>Queue/reuse online refresh task de test va host co the await khi can.</summary>
    public Task<LicenseState> RefreshInBackgroundAsync(CancellationToken ct = default)
    {
        lock (_refreshSync)
        {
            if (_refreshTask is { IsCompleted: false }) return _refreshTask;

            _refreshTask = Task.Run(() => GetStateAsync(ct), ct);
            return _refreshTask;
        }
    }

    /// <summary>Warm-up va duy tri snapshot RAM moi, khong chan UI Revit.</summary>
    public static void StartBackgroundRefresh()
    {
        var service = Instance;
        lock (service._refreshSync)
        {
            if (service._backgroundTask is { IsCompleted: false }) return;
            service._backgroundCts = new CancellationTokenSource();
            var ct = service._backgroundCts.Token;
            service._backgroundTask = Task.Run(() => service.RunBackgroundLoopAsync(ct), ct);
        }
    }

    /// <summary>Dung worker khi Revit shutdown; khong cho network tren UI thread.</summary>
    public static void StopBackgroundRefresh()
    {
        var service = Instance;
        CancellationTokenSource? cts;
        Task? task;
        lock (service._refreshSync)
        {
            cts = service._backgroundCts;
            task = service._backgroundTask;
            service._backgroundCts = null;
            service._backgroundTask = null;
        }

        cts?.Cancel();
        if (cts != null)
            _ = (task ?? Task.CompletedTask).ContinueWith(
                _ => cts.Dispose(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
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

    private long CaptureSnapshotGeneration()
    {
        lock (_snapshotSync) return _snapshotGeneration;
    }

    private async Task RunBackgroundLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var identity = _cache.Read();
                if (identity != null && !string.IsNullOrEmpty(identity.Email))
                    await RefreshInBackgroundAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Snapshot tu het han va fail closed; worker khong duoc crash host.
            }

            try
            {
                await Task.Delay(LicenseConfig.BackgroundRefreshInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private void SetVerifiedSnapshot(
        string expectedSessionId,
        long expectedGeneration,
        LicenseState state)
    {
        var current = _cache.Read();
        if (!string.Equals(current?.SessionId, expectedSessionId, StringComparison.Ordinal))
            return;

        lock (_snapshotSync)
        {
            if (_snapshotGeneration != expectedGeneration) return;
            _verifiedSnapshot = state;
            _verifiedSnapshotUtc = _utcNow();
            _verifiedSessionId = expectedSessionId;
        }
    }

    private void ClearVerifiedSnapshot()
    {
        lock (_snapshotSync)
        {
            _snapshotGeneration++;
            _verifiedSnapshot = null;
            _verifiedSnapshotUtc = default;
            _verifiedSessionId = null;
        }
    }

    private static bool TryParseValidExpiry(string? value, out DateTime expiry) =>
        DateTime.TryParseExact(value, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out expiry);

    private LicenseState BuildServerVerifiedState(string email, VerifyResult result)
    {
        if (!result.Allowed)
            return LicenseState.Denied(email, DescribeError(result.Error));

        if (!TryParseValidExpiry(result.Expiry, out var expiry) ||
            _utcNow().Date > expiry.Date)
            return LicenseState.Expired(email, result.Expiry, "License da het han hoac ngay het han khong hop le");

        return LicenseState.Valid(email, result.Expiry);
    }

}
