using RevitAPP.Licensing;
using Xunit;

namespace RevitAPP.Licensing.Tests;

public class LicenseServiceTests
{
    private static string TempCacheFile() =>
        Path.Combine(Path.GetTempPath(), "revitapp-lic-test-" + Guid.NewGuid().ToString("N") + ".json");

    /// <summary>Verifier gia: dem so lan goi de kiem tra "cache hit khong goi mang".</summary>
    private sealed class FakeVerifier(bool allowed, string? expiry, string? error = null) : ILicenseVerifier
    {
        public int CallCount { get; private set; }
        public bool ThrowOffline { get; set; }

        public Task<VerifyResult> VerifyAsync(string email, CancellationToken ct = default)
        {
            CallCount++;
            if (ThrowOffline) throw new HttpRequestException("offline");
            return Task.FromResult(new VerifyResult(allowed, expiry, error));
        }
    }

    private sealed class FakeOAuth(string? email) : IOAuthSignIn
    {
        public Task<string?> SignInAsync(CancellationToken ct = default) => Task.FromResult(email);
    }

    private static LicenseService Build(
        LicenseCache cache, ILicenseVerifier verifier, DateTime now,
        IOAuthSignIn? oauth = null, int graceDays = 7) =>
        new(oauth ?? new FakeOAuth("a@b.com"), verifier, cache, () => now, graceDays);

    [Fact]
    public async Task NotSignedIn_when_no_cache()
    {
        var cache = new LicenseCache(TempCacheFile());
        var svc = Build(cache, new FakeVerifier(true, "2099-01-01"), DateTime.UtcNow);

        var state = await svc.GetStateAsync();

        Assert.Equal(LicenseStatus.NotSignedIn, state.Status);
    }

    [Fact]
    public async Task SignIn_allowed_writes_cache_and_returns_valid()
    {
        var cache = new LicenseCache(TempCacheFile());
        var svc = Build(cache, new FakeVerifier(true, "2099-01-01"), DateTime.UtcNow,
            new FakeOAuth("khach@gmail.com"));

        var state = await svc.SignInAsync();

        Assert.True(state.IsValid);
        Assert.Equal("khach@gmail.com", state.Email);
        Assert.True(cache.Read()!.Allowed);
    }

    [Fact]
    public async Task Recent_cached_license_reverifies_and_blocks_server_expiry_without_signout()
    {
        var now = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var cache = new LicenseCache(TempCacheFile());
        cache.Write(new LicenseCacheData
        {
            Email = "a@b.com",
            Expiry = "2099-01-01",
            Allowed = true,
            LastVerifiedUtc = now.ToString("O")
        });
        var verifier = new FakeVerifier(false, "2026-01-01", "expired");
        var svc = Build(cache, verifier, now);

        var state = await svc.GetStateAsync();

        Assert.Equal(LicenseStatus.Denied, state.Status);
        Assert.Equal(1, verifier.CallCount);
        Assert.False(cache.Read()!.Allowed);
        Assert.Equal("2026-01-01", cache.Read()!.Expiry);
    }

    private sealed class ThrowingWriteCache(string path) : LicenseCache(path)
    {
        public bool ThrowOnWrite { get; set; }

        public override void Write(LicenseCacheData data)
        {
            if (ThrowOnWrite) throw new IOException("cache locked");
            base.Write(data);
        }

        public override bool WriteIfSessionMatches(
            string expectedSessionId,
            LicenseCacheData data)
        {
            if (ThrowOnWrite) throw new IOException("cache locked");
            return base.WriteIfSessionMatches(expectedSessionId, data);
        }
    }

    private sealed class DelayedVerifier(bool allowed, string? expiry) : ILicenseVerifier
    {
        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete() => _release.TrySetResult(true);

        public async Task<VerifyResult> VerifyAsync(string email, CancellationToken ct = default)
        {
            Started.TrySetResult(true);
            await _release.Task.WaitAsync(ct);
            return new VerifyResult(allowed, expiry, allowed ? null : "expired");
        }
    }

    [Fact]
    public async Task Recent_cached_license_is_blocked_when_server_is_offline()
    {
        var now = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var cache = new LicenseCache(TempCacheFile());
        cache.Write(new LicenseCacheData
        {
            Email = "a@b.com",
            Expiry = "2099-01-01",
            Allowed = true,
            LastVerifiedUtc = now.ToString("O")
        });
        var verifier = new FakeVerifier(true, "2099-01-01") { ThrowOffline = true };
        var svc = Build(cache, verifier, now);

        var state = await svc.GetStateAsync();

        Assert.Equal(LicenseStatus.Expired, state.Status);
        Assert.Equal(1, verifier.CallCount);
    }

    [Fact]
    public async Task Expired_cached_date_can_be_renewed_by_server_without_signout()
    {
        var now = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var cache = new LicenseCache(TempCacheFile());
        cache.Write(new LicenseCacheData
        {
            Email = "a@b.com",
            Expiry = "2026-01-01",
            Allowed = true,
            LastVerifiedUtc = now.ToString("O")
        });
        var verifier = new FakeVerifier(true, "2099-01-01");
        var svc = Build(cache, verifier, now);

        var state = await svc.GetStateAsync();

        Assert.True(state.IsValid);
        Assert.Equal(1, verifier.CallCount);
        Assert.Equal("2099-01-01", cache.Read()!.Expiry);
    }

    [Fact]
    public async Task Allowed_server_result_is_not_blocked_when_cache_write_fails()
    {
        var now = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var cache = new ThrowingWriteCache(TempCacheFile());
        cache.Write(new LicenseCacheData
        {
            Email = "a@b.com",
            Expiry = "2026-07-30",
            Allowed = true,
            LastVerifiedUtc = now.ToString("O")
        });
        cache.ThrowOnWrite = true;
        var verifier = new FakeVerifier(true, "2026-08-30");
        var svc = Build(cache, verifier, now);

        var state = await svc.GetStateAsync();

        Assert.True(state.IsValid);
        Assert.Equal(1, verifier.CallCount);
    }

    [Fact]
    public async Task Concurrent_cache_writes_leave_valid_json()
    {
        var file = TempCacheFile();
        var cache = new LicenseCache(file);
        var writes = Enumerable.Range(1, 20)
            .Select(i => Task.Run(() => cache.Write(new LicenseCacheData
            {
                Email = "a@b.com",
                Expiry = $"2099-01-{i:00}",
                Allowed = true,
                LastVerifiedUtc = DateTime.UtcNow.ToString("O")
            })));

        await Task.WhenAll(writes);

        var data = cache.Read();
        Assert.NotNull(data);
        Assert.Equal("a@b.com", data!.Email);
        Assert.True(data.Allowed);
    }

    [Fact]
    public async Task SignOut_during_online_verification_cannot_recreate_cache()
    {
        var now = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var cache = new LicenseCache(TempCacheFile());
        cache.Write(new LicenseCacheData
        {
            Email = "a@b.com",
            Expiry = "2099-01-01",
            Allowed = true,
            LastVerifiedUtc = now.ToString("O")
        });
        var verifier = new DelayedVerifier(true, "2099-01-01");
        var svc = Build(cache, verifier, now);

        var stateTask = svc.GetStateAsync();
        await verifier.Started.Task;
        svc.SignOut();
        verifier.Complete();
        var state = await stateTask;

        Assert.Equal(LicenseStatus.NotSignedIn, state.Status);
        Assert.Null(cache.Read());
    }

    [Fact]
    public async Task SignOut_during_sign_in_cannot_recreate_cache()
    {
        var now = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var cache = new LicenseCache(TempCacheFile());
        cache.Write(new LicenseCacheData
        {
            Email = "old@b.com",
            Expiry = "2099-01-01",
            SessionId = "old-session",
            Allowed = true,
            LastVerifiedUtc = now.ToString("O")
        });
        var verifier = new DelayedVerifier(true, "2099-01-01");
        var svc = Build(cache, verifier, now, new FakeOAuth("a@b.com"));

        var stateTask = svc.SignInAsync();
        await verifier.Started.Task;
        svc.SignOut();
        verifier.Complete();
        var state = await stateTask;

        Assert.Equal(LicenseStatus.NotSignedIn, state.Status);
        Assert.Null(cache.Read());
    }

    [Fact]
    public async Task First_sign_in_cannot_commit_after_concurrent_sign_out()
    {
        var now = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var cache = new LicenseCache(TempCacheFile());
        var verifier = new DelayedVerifier(true, "2099-01-01");
        var svc = Build(cache, verifier, now, new FakeOAuth("a@b.com"));

        var stateTask = svc.SignInAsync();
        await verifier.Started.Task;
        svc.SignOut();
        verifier.Complete();
        var state = await stateTask;

        Assert.Equal(LicenseStatus.NotSignedIn, state.Status);
        Assert.Null(cache.Read());
    }

    [Fact]
    public async Task SignIn_repairs_malformed_cache()
    {
        var file = TempCacheFile();
        File.WriteAllText(file, "{ malformed json");
        var cache = new LicenseCache(file);
        var now = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var svc = Build(
            cache,
            new FakeVerifier(true, "2099-01-01"),
            now,
            new FakeOAuth("a@b.com"));

        var state = await svc.SignInAsync();

        Assert.True(state.IsValid);
        Assert.Equal("a@b.com", cache.Read()!.Email);
        Assert.False(string.IsNullOrEmpty(cache.Read()!.SessionId));
    }

    [Fact]
    public async Task Concurrent_first_verifications_migrate_legacy_cache_without_false_denial()
    {
        var now = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var cache = new LicenseCache(TempCacheFile());
        cache.Write(new LicenseCacheData
        {
            Email = "a@b.com",
            Expiry = "2099-01-01",
            SessionId = null,
            Allowed = true,
            LastVerifiedUtc = now.ToString("O")
        });
        var verifier = new DelayedVerifier(true, "2099-01-01");
        var svc = Build(cache, verifier, now);

        var first = svc.GetStateAsync();
        var second = svc.GetStateAsync();
        await verifier.Started.Task;
        verifier.Complete();
        var states = await Task.WhenAll(first, second);

        Assert.All(states, state => Assert.True(state.IsValid));
        Assert.False(string.IsNullOrEmpty(cache.Read()!.SessionId));
    }

    [Fact]
    public async Task Past_grace_reverifies_online()
    {
        var now = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var cache = new LicenseCache(TempCacheFile());
        cache.Write(new LicenseCacheData
        {
            Email = "a@b.com",
            Expiry = "2099-01-01",
            Allowed = true,
            LastVerifiedUtc = now.AddDays(-10).ToString("O") // qua grace 7 ngay
        });
        var verifier = new FakeVerifier(true, "2099-01-01");
        var svc = Build(cache, verifier, now);

        var state = await svc.GetStateAsync();

        Assert.True(state.IsValid);
        Assert.Equal(1, verifier.CallCount); // qua grace -> goi mang lai
    }

    [Fact]
    public async Task Server_expiry_is_denied()
    {
        var now = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var cache = new LicenseCache(TempCacheFile());
        cache.Write(new LicenseCacheData
        {
            Email = "a@b.com",
            Expiry = "2026-01-01", // da qua
            Allowed = true,
            LastVerifiedUtc = now.AddDays(-1).ToString("O")
        });
        var svc = Build(cache, new FakeVerifier(false, "2026-01-01", "expired"), now);

        var state = await svc.GetStateAsync();

        Assert.Equal(LicenseStatus.Denied, state.Status);
    }

    [Fact]
    public async Task Offline_past_grace_is_blocked()
    {
        var now = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var cache = new LicenseCache(TempCacheFile());
        cache.Write(new LicenseCacheData
        {
            Email = "a@b.com",
            Expiry = "2099-01-01",
            Allowed = true,
            LastVerifiedUtc = now.AddDays(-10).ToString("O") // qua grace
        });
        var verifier = new FakeVerifier(true, "2099-01-01") { ThrowOffline = true };
        var svc = Build(cache, verifier, now);

        var state = await svc.GetStateAsync();

        Assert.Equal(LicenseStatus.Expired, state.Status); // offline + qua grace -> chan
    }

    [Fact]
    public async Task SignIn_denied_returns_denied_and_not_valid()
    {
        var cache = new LicenseCache(TempCacheFile());
        var svc = Build(cache, new FakeVerifier(false, null, "not_found"), DateTime.UtcNow,
            new FakeOAuth("lave@gmail.com"));

        var state = await svc.SignInAsync();

        Assert.Equal(LicenseStatus.Denied, state.Status);
        Assert.False(state.IsValid);
    }

    [Fact]
    public void SignOut_clears_cache()
    {
        var file = TempCacheFile();
        var cache = new LicenseCache(file);
        cache.Write(new LicenseCacheData { Email = "a@b.com", Allowed = true });
        var svc = Build(cache, new FakeVerifier(true, "2099-01-01"), DateTime.UtcNow);

        svc.SignOut();

        Assert.Null(cache.Read());
    }
}
