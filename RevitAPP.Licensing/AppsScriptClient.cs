using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitAPP.Licensing;

/// <summary>Ket qua verify tu Apps Script.</summary>
public sealed record VerifyResult(bool Allowed, string? Expiry, string? Error);

/// <summary>Interface de mock trong unit test.</summary>
public interface ILicenseVerifier
{
    Task<VerifyResult> VerifyAsync(string email, CancellationToken ct = default);
}

/// <summary>
/// Goi Apps Script web app: POST { email, secret } -> { allowed, expiry, error }.
/// </summary>
public sealed class AppsScriptClient : ILicenseVerifier
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private sealed class Response
    {
        [JsonPropertyName("allowed")] public bool Allowed { get; set; }
        [JsonPropertyName("expiry")] public string? Expiry { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
    }

    public async Task<VerifyResult> VerifyAsync(string email, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            email,
            secret = LicenseConfig.SharedSecret,
            machineId = MachineId.Current
        });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        // Apps Script /exec tra ve 302 redirect toi googleusercontent -> HttpClient tu follow.
        using var resp = await Http.PostAsync(LicenseConfig.AppsScriptUrl, content, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

        var parsed = JsonSerializer.Deserialize<Response>(body)
                     ?? throw new InvalidOperationException("Empty verify response");
        return new VerifyResult(parsed.Allowed, parsed.Expiry, parsed.Error);
    }
}
