using System.Security.Cryptography;
using System.Text;

namespace RevitAPP.Licensing;

/// <summary>
///     Sinh Machine ID on dinh cho may hien tai (chong chia se tai khoan qua nhieu may).
///     Dua tren MachineGuid trong registry Windows + ten may. Hash SHA-256 -> 16 ky tu hex,
///     khong lo thong tin phan cung that.
/// </summary>
public static class MachineId
{
    private static string? _cached;

    public static string Current => _cached ??= Compute();

    private static string Compute()
    {
        var raw = ReadMachineGuid() + "|" + Environment.MachineName;
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
        // 8 byte dau -> 16 hex, du unique cho muc dich license. (BitConverter chay ca net48 lan net8.)
        var sb = new StringBuilder(16);
        for (var i = 0; i < 8; i++) sb.Append(hash[i].ToString("X2"));
        return sb.ToString();
    }

    /// <summary>
    ///     MachineGuid = dinh danh on dinh do Windows sinh khi cai OS (khong doi khi doi phan cung nho).
    /// </summary>
    private static string ReadMachineGuid()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Cryptography");
            var guid = key?.GetValue("MachineGuid") as string;
            if (!string.IsNullOrEmpty(guid)) return guid!;
        }
        catch
        {
            // Khong doc duoc registry -> fallback ten may (kem on dinh hon nhung van chay).
        }
        return Environment.MachineName;
    }
}
