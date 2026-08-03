namespace RevitAPP.Core.Services;

public static class DwgOutputPathPolicy
{
    public static string ResolveSuggestedDirectory(string? documentPath, string fallbackDirectory)
    {
        if (TryGetLocalDirectory(documentPath, requireExisting: false, out var directory))
            return directory;

        if (!TryGetLocalDirectory(Path.Combine(fallbackDirectory, "placeholder.dwg"), requireExisting: false, out directory))
            throw new ArgumentException("Fallback output directory must be a fully qualified local path.", nameof(fallbackDirectory));

        return directory;
    }

    public static string? GetExistingInitialDirectory(string? outputPath) =>
        TryGetLocalDirectory(outputPath, requireExisting: true, out var directory) ? directory : null;

    public static bool TryValidateOutputPath(string? outputPath, out string error)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            error = "Hãy bấm Duyệt và chọn đường dẫn file DWG đầu ra.";
            return false;
        }

        if (!string.Equals(Path.GetExtension(outputPath), ".dwg", StringComparison.OrdinalIgnoreCase))
        {
            error = "File đầu ra phải có phần mở rộng .dwg.";
            return false;
        }

        if (!TryGetLocalDirectory(outputPath, requireExisting: true, out _))
        {
            error = "Hãy chọn thư mục Windows hoặc thư mục mạng đang tồn tại; không dùng đường dẫn ảo Autodesk Docs.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryGetLocalDirectory(
        string? filePath,
        bool requireExisting,
        out string directory)
    {
        directory = string.Empty;
        if (string.IsNullOrWhiteSpace(filePath)) return false;

        try
        {
            if (!IsFullyQualified(filePath!)) return false;
            var candidate = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            if (requireExisting && !Directory.Exists(candidate)) return false;
            directory = candidate;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsFullyQualified(string path)
    {
        if (!Path.IsPathRooted(path)) return false;
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return true;
        return path.Length >= 3
               && char.IsLetter(path[0])
               && path[1] == ':'
               && (path[2] == Path.DirectorySeparatorChar
                   || path[2] == Path.AltDirectorySeparatorChar);
    }
}
