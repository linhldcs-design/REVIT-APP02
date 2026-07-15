namespace RevitAPP.Licensing;

public enum LicenseStatus
{
    /// <summary>Chua dang nhap lan nao (khong co cache).</summary>
    NotSignedIn,

    /// <summary>Hop le — cho phep dung.</summary>
    Valid,

    /// <summary>Het han (expiry qua khu, hoac cache qua grace + offline).</summary>
    Expired,

    /// <summary>Server tra ve khong duoc phep (email khong co trong danh sach).</summary>
    Denied
}

/// <summary>
/// Trang thai license tai 1 thoi diem. Chi Valid moi cho phep ve thep.
/// </summary>
public sealed record LicenseState(
    LicenseStatus Status,
    string? Email,
    string? Expiry,
    string Reason)
{
    public bool IsValid => Status == LicenseStatus.Valid;

    public static LicenseState NotSignedIn() =>
        new(LicenseStatus.NotSignedIn, null, null, "Chua dang nhap");

    public static LicenseState Valid(string email, string? expiry) =>
        new(LicenseStatus.Valid, email, expiry, $"Da kich hoat (het han {expiry})");

    public static LicenseState Expired(string? email, string? expiry, string reason) =>
        new(LicenseStatus.Expired, email, expiry, reason);

    public static LicenseState Denied(string? email, string reason) =>
        new(LicenseStatus.Denied, email, null, reason);
}
