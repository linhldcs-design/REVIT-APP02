using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace RevitAPP.Licensing;

/// <summary>
/// Noi dung cache luu tren dia (%AppData%\RevitAPP\license.json).
/// </summary>
public sealed class LicenseCacheData
{
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("sessionId")] public string? SessionId { get; set; }

    /// <summary>Thoi diem verify online thanh cong gan nhat (UTC ISO 8601).</summary>
    [JsonPropertyName("lastVerifiedUtc")] public string? LastVerifiedUtc { get; set; }

    /// <summary>Ket qua verify gan nhat (server cho phep hay khong).</summary>
    [JsonPropertyName("allowed")] public bool Allowed { get; set; }
}

/// <summary>
/// Doc/ghi cache license. Ghi atomic (temp + move) de tranh corrupt khi 2 process (addin + MCP) cung cham.
/// </summary>
public class LicenseCache
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly string _path;

    public LicenseCache(string? path = null) => _path = path ?? LicenseConfig.CacheFile;

    public LicenseCacheData? Read()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var raw = File.ReadAllText(_path);
            var data = JsonSerializer.Deserialize<LicenseCacheData>(raw);
            return string.IsNullOrEmpty(data?.Email) ? null : data;
        }
        catch
        {
            // Cache hong -> coi nhu chua co, buoc dang nhap lai.
            return null;
        }
    }

    public virtual void Write(LicenseCacheData data)
    {
        WithWriteLock(() =>
        {
            WriteLocked(data);
            return true;
        });
    }

    /// <summary>
    /// Doc session duoi mutex va migrate cache legacy/corrupt/absent sang mot generation
    /// co sessionId. Email null la tombstone signed-out, nhung generation van duoc giu.
    /// </summary>
    public virtual LicenseCacheData ReadOrCreateSessionSnapshot() =>
        WithWriteLock(() =>
        {
            var current = ReadRawLocked();
            if (current != null && !string.IsNullOrEmpty(current.SessionId))
                return current;

            var migrated = current ?? new LicenseCacheData { Allowed = false };
            migrated.SessionId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(migrated.Email))
            {
                migrated.Email = null;
                migrated.Expiry = null;
                migrated.LastVerifiedUtc = null;
                migrated.Allowed = false;
            }
            WriteLocked(migrated);
            return migrated;
        });

    /// <summary>
    /// Conditional commit theo ca email va session id. Cache cu chua co sessionId van
    /// duoc migrate mot lan; sau do response cua session cu khong the ghi de session moi.
    /// </summary>
    public virtual bool WriteIfSessionMatches(
        string expectedSessionId,
        LicenseCacheData data) =>
        WithWriteLock(() =>
        {
            var current = ReadRawLocked();
            var sessionMatches = string.Equals(
                current?.SessionId, expectedSessionId, StringComparison.Ordinal);
            if (!sessionMatches) return false;

            WriteLocked(data);
            return true;
        });

    public void Clear()
    {
        try
        {
            WithWriteLock(() =>
            {
                WriteLocked(new LicenseCacheData
                {
                    Email = null,
                    Expiry = null,
                    SessionId = Guid.NewGuid().ToString("N"),
                    LastVerifiedUtc = null,
                    Allowed = false
                });
                return true;
            });
        }
        catch { /* ignore */ }
    }

    private LicenseCacheData? ReadRawLocked()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            return JsonSerializer.Deserialize<LicenseCacheData>(File.ReadAllText(_path));
        }
        catch
        {
            return null;
        }
    }

    private void WriteLocked(LicenseCacheData data)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var tmp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, JsonOpts));
            if (File.Exists(_path))
                File.Replace(tmp, _path, null);
            else
                File.Move(tmp, _path);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
        }
    }

    private T WithWriteLock<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, BuildMutexName(_path));
        var lockTaken = false;
        try
        {
            try
            {
                lockTaken = mutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                lockTaken = true;
            }

            if (!lockTaken)
                throw new IOException("Timed out waiting to update the license cache.");

            return action();
        }
        finally
        {
            if (lockTaken) mutex.ReleaseMutex();
        }
    }

    private static string BuildMutexName(string path)
    {
        using var sha = SHA256.Create();
        var fullPath = Path.GetFullPath(path).ToUpperInvariant();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(fullPath));
        var suffix = BitConverter.ToString(hash, 0, 8).Replace("-", string.Empty);
        return @"Local\RevitAPP.LicenseCache." + suffix;
    }
}
