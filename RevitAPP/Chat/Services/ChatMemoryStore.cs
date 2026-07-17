using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using RevitAPP.Chat.Models;

namespace RevitAPP.Chat.Services;

/// <summary>Versioned, DPAPI-encrypted, bounded local memory for Chat AI.</summary>
public sealed class ChatMemoryStore
{
    private const int MaxEntries = 500;
    private readonly object _gate = new();
    private readonly string _path;

    public ChatMemoryStore()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RevitAPP");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "chat-memory.dat");
    }

    public void Add(ChatMemoryEntry entry)
    {
        lock (_gate)
        {
            Sanitize(entry);
            var document = LoadUnsafe();
            var duplicate = document.Entries.LastOrDefault(item =>
                item.Project == entry.Project && item.UserText == entry.UserText &&
                item.ToolName == entry.ToolName && item.ArgumentsJson == entry.ArgumentsJson);
            if (duplicate is not null)
            {
                entry.Id = duplicate.Id;
                entry.Pinned |= duplicate.Pinned;
                document.Entries.Remove(duplicate);
            }
            document.Entries.Add(entry);
            Trim(document.Entries);
            SaveUnsafe(document);
        }
    }

    public IReadOnlyList<ChatMemoryEntry> Relevant(string project, string query, int limit = 12)
    {
        lock (_gate)
        {
            var terms = Terms(query);
            return LoadUnsafe().Entries
                .Where(entry => entry.Pinned || string.IsNullOrEmpty(entry.Project) || entry.Project == project)
                .Select(entry => new { Entry = entry, Score = Score(entry, terms, project) })
                .Where(item => item.Entry.Pinned || item.Score > 0)
                .OrderByDescending(item => item.Entry.Pinned)
                .ThenByDescending(item => item.Score)
                .ThenByDescending(item => item.Entry.CreatedAt)
                .Take(limit)
                .Select(item => item.Entry)
                .ToList();
        }
    }

    public IReadOnlyList<ChatMemoryEntry> Recent(string project, int limit = 20)
    {
        lock (_gate)
            return LoadUnsafe().Entries.Where(item => item.Project == project || item.Pinned)
                .OrderByDescending(item => item.CreatedAt).Take(limit).ToList();
    }

    public int Forget(string project, string query)
    {
        lock (_gate)
        {
            var document = LoadUnsafe();
            var normalized = Normalize(query);
            var removed = document.Entries.RemoveAll(item => !item.Pinned &&
                (item.Project == project || string.IsNullOrEmpty(project)) && Normalize(SearchText(item)).Contains(normalized));
            if (removed > 0) SaveUnsafe(document);
            return removed;
        }
    }

    public int Pin(string project, string query)
    {
        lock (_gate)
        {
            var document = LoadUnsafe();
            var normalized = Normalize(query);
            var matches = document.Entries.Where(item => item.Project == project && Normalize(SearchText(item)).Contains(normalized)).ToList();
            foreach (var item in matches) item.Pinned = true;
            if (matches.Count > 0) SaveUnsafe(document);
            return matches.Count;
        }
    }

    public int Clear()
    {
        lock (_gate)
        {
            var count = LoadUnsafe().Entries.Count;
            if (File.Exists(_path)) File.Delete(_path);
            return count;
        }
    }

    public string BuildContext(string project, string query)
    {
        var memories = Relevant(project, query);
        if (memories.Count == 0) return string.Empty;
        var lines = memories.Select(item =>
            $"- [{item.CreatedAt:yyyy-MM-dd}; {(item.Success ? "thành công" : "tham khảo")}; {item.ToolName}] " +
            $"Yêu cầu: {Clip(item.UserText, 240)} | Kết quả/cách làm: {Clip(item.AssistantText + " " + item.ResultJson, 420)}");
        return "BỘ NHỚ CỤC BỘ LIÊN QUAN (ưu tiên mục thành công/được ghim; không tự chạy thao tác nguy hiểm):\n" + string.Join("\n", lines);
    }

    private ChatMemoryDocument LoadUnsafe()
    {
        if (!File.Exists(_path)) return new ChatMemoryDocument();
        try
        {
            var encrypted = File.ReadAllBytes(_path);
            var bytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            using var stream = new MemoryStream(bytes);
            return (ChatMemoryDocument?)new DataContractJsonSerializer(typeof(ChatMemoryDocument)).ReadObject(stream)
                   ?? new ChatMemoryDocument();
        }
        catch { return new ChatMemoryDocument(); }
    }

    private void SaveUnsafe(ChatMemoryDocument document)
    {
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(ChatMemoryDocument)).WriteObject(stream, document);
        var encrypted = ProtectedData.Protect(stream.ToArray(), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_path, encrypted);
    }

    private static void Trim(List<ChatMemoryEntry> entries)
    {
        while (entries.Count > MaxEntries)
        {
            var candidate = entries.Where(item => !item.Pinned).OrderBy(item => item.CreatedAt).FirstOrDefault();
            if (candidate is null) break;
            entries.Remove(candidate);
        }
    }

    private static int Score(ChatMemoryEntry entry, HashSet<string> terms, string project)
    {
        var haystack = Terms(SearchText(entry));
        var score = terms.Count(term => haystack.Contains(term)) * 10;
        if (entry.Project == project) score += 3;
        if (entry.Success) score += 2;
        return score;
    }

    private static HashSet<string> Terms(string text) => Normalize(text)
        .Split(new[] { ' ', '\t', '\r', '\n', ',', '.', ':', ';', '/', '\\', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
        .Where(term => term.Length > 2).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string SearchText(ChatMemoryEntry item) =>
        $"{item.UserText} {item.AssistantText} {item.ToolName} {item.ArgumentsJson} {item.ResultJson}";

    private static string Normalize(string value)
    {
        var decomposed = (value ?? string.Empty).ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return new string(decomposed.Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray())
            .Normalize(NormalizationForm.FormC);
    }

    private static string Clip(string value, int max) => string.IsNullOrWhiteSpace(value)
        ? string.Empty : value.Length <= max ? value : value[..max] + "…";

    private static void Sanitize(ChatMemoryEntry entry)
    {
        entry.UserText = RedactSecrets(entry.UserText);
        entry.AssistantText = RedactSecrets(entry.AssistantText);
        entry.ArgumentsJson = RedactSecrets(entry.ArgumentsJson);
        entry.ResultJson = RedactSecrets(entry.ResultJson);
    }

    private static string RedactSecrets(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        value = Regex.Replace(value, @"sk-[A-Za-z0-9_-]{20,}", "[API_KEY_REDACTED]", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"AIza[A-Za-z0-9_-]{20,}", "[API_KEY_REDACTED]", RegexOptions.IgnoreCase);
        return value;
    }
}
