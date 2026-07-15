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
    public async Task Valid_within_grace_does_not_call_network()
    {
        var now = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var cache = new LicenseCache(TempCacheFile());
        cache.Write(new LicenseCacheData
        {
            Email = "a@b.com",
            Expiry = "2099-01-01",
            Allowed = true,
            LastVerifiedUtc = now.AddDays(-3).ToString("O") // 3 ngay truoc, trong grace 7
        });
        var verifier = new FakeVerifier(true, "2099-01-01");
        var svc = Build(cache, verifier, now);

        var state = await svc.GetStateAsync();

        Assert.True(state.IsValid);
        Assert.Equal(0, verifier.CallCount); // cache hit -> KHONG goi mang
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
    public async Task Expiry_in_past_is_expired()
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
        var svc = Build(cache, new FakeVerifier(true, "2026-01-01"), now);

        var state = await svc.GetStateAsync();

        Assert.Equal(LicenseStatus.Expired, state.Status);
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
