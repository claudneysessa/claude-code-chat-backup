using System.Text.Json;

namespace ClaudeChatBackup.Infrastructure;

/// <summary>Reads the sessions and project folders that exist on disk for one set of locations.</summary>
public sealed class SessionCatalog
{
    public required IReadOnlyList<SessionInfo> Sessions { get; init; }
    public required IReadOnlyList<ManifestScope> Scopes { get; init; }

    /// <summary>Project folder full path to the working directory found in its transcripts.</summary>
    public required IReadOnlyDictionary<string, string?> ProjectDirs { get; init; }

    public static SessionCatalog Load(ClaudeLocations loc)
    {
        var transcripts = IndexTranscripts(loc.DotClaudeRoot, out var projectDirs);
        var sessions = new List<SessionInfo>();
        var scopes = new HashSet<ManifestScope>();
        var seenCli = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (loc.DesktopRoot is not null)
        {
            foreach (var dirName in DataLayout.DesktopDirs)
                foreach (var scope in EnumerateScopes(Path.Combine(loc.DesktopRoot, dirName)))
                    scopes.Add(scope);

            var codeRoot = Path.Combine(loc.DesktopRoot, DataLayout.DesktopCodeSessions);
            if (Directory.Exists(codeRoot))
            {
                foreach (var file in Directory.EnumerateFiles(codeRoot, "local_*.json", SearchOption.AllDirectories))
                {
                    var s = ReadDesktopSession(codeRoot, file);
                    if (s is null) continue;
                    if (s.CliSessionId is not null && transcripts.TryGetValue(s.CliSessionId, out var t))
                    {
                        s.TranscriptFile = t;
                        s.TranscriptBytes = new FileInfo(t).Length;
                        seenCli.Add(s.CliSessionId);
                    }
                    sessions.Add(s);
                }
            }
        }

        foreach (var (cliId, path) in transcripts)
        {
            if (seenCli.Contains(cliId)) continue;
            var fi = new FileInfo(path);
            projectDirs.TryGetValue(fi.DirectoryName!, out var cwd);
            sessions.Add(new SessionInfo
            {
                Id = cliId,
                CliSessionId = cliId,
                Kind = SessionKind.Cli,
                Title = ReadFirstPrompt(path),
                Cwd = cwd,
                LastActivityAt = fi.LastWriteTimeUtc,
                TranscriptFile = path,
                TranscriptBytes = fi.Length,
            });
        }

        return new SessionCatalog
        {
            Sessions = sessions.OrderByDescending(s => s.LastActivityAt ?? DateTimeOffset.MinValue).ToList(),
            Scopes = scopes.ToList(),
            ProjectDirs = projectDirs,
        };
    }

    /// <summary>Account/org folders: &lt;root&gt;/&lt;account&gt;/&lt;org&gt;. Non-GUID folders (e.g. skills-plugin) are skipped.</summary>
    public static IEnumerable<ManifestScope> EnumerateScopes(string root)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (var acct in Directory.EnumerateDirectories(root))
        {
            var a = Path.GetFileName(acct);
            if (!Guid.TryParse(a, out _)) continue;
            foreach (var org in Directory.EnumerateDirectories(acct))
            {
                var o = Path.GetFileName(org);
                if (Guid.TryParse(o, out _)) yield return new ManifestScope(a, o);
            }
        }
    }

    static Dictionary<string, string> IndexTranscripts(string dotClaude, out Dictionary<string, string?> projectDirs)
    {
        var byId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        projectDirs = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var projects = Path.Combine(dotClaude, DataLayout.Projects);
        if (!Directory.Exists(projects)) return byId;

        foreach (var dir in Directory.EnumerateDirectories(projects))
        {
            string? cwd = null;
            foreach (var file in Directory.EnumerateFiles(dir, "*.jsonl"))
            {
                byId[Path.GetFileNameWithoutExtension(file)] = file;
                cwd ??= ReadCwd(file);
            }
            projectDirs[dir] = cwd;
        }
        return byId;
    }

    static SessionInfo? ReadDesktopSession(string codeRoot, string file)
    {
        try
        {
            using var doc = JsonDocument.Parse(ReadShared(file));
            var r = doc.RootElement;
            var parts = Path.GetRelativePath(codeRoot, file).Split(Path.DirectorySeparatorChar);
            return new SessionInfo
            {
                Id = Str(r, "sessionId") ?? Path.GetFileNameWithoutExtension(file),
                CliSessionId = Str(r, "cliSessionId"),
                Kind = SessionKind.Desktop,
                Title = Str(r, "title"),
                Cwd = Str(r, "cwd"),
                Model = Str(r, "model"),
                IsArchived = r.TryGetProperty("isArchived", out var a) && a.ValueKind == JsonValueKind.True,
                CreatedAt = EpochMs(r, "createdAt"),
                LastActivityAt = EpochMs(r, "lastActivityAt"),
                Account = parts.Length >= 3 ? parts[0] : null,
                Org = parts.Length >= 3 ? parts[1] : null,
                DesktopFile = file,
            };
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return null;
        }
    }

    /// <summary>Limite do título tirado do primeiro pedido; o resto aparece no tooltip da tela.</summary>
    public const int MaxTitleLength = 300;

    static string? ReadCwd(string jsonl)
    {
        using var fs = OpenShared(jsonl);
        foreach (var root in ReadHeadLines(fs, 200))
            if (Str(root, "cwd") is { Length: > 0 } cwd) return cwd;
        return null;
    }

    static string? ReadFirstPrompt(string jsonl)
    {
        using var fs = OpenShared(jsonl);
        return FirstPrompt(fs);
    }

    /// <summary>Primeiro pedido do usuário num transcript (texto, não comandos internos), em uma linha.</summary>
    public static string? FirstPrompt(Stream transcript)
    {
        foreach (var root in ReadHeadLines(transcript, 200))
        {
            if (Str(root, "type") != "user" || !root.TryGetProperty("message", out var msg)) continue;
            if (!msg.TryGetProperty("content", out var content)) continue;
            string? text = content.ValueKind switch
            {
                JsonValueKind.String => content.GetString(),
                JsonValueKind.Array => content.EnumerateArray()
                    .Where(p => Str(p, "type") == "text")
                    .Select(p => Str(p, "text"))
                    .FirstOrDefault(),
                _ => null,
            };
            if (string.IsNullOrWhiteSpace(text) || text.StartsWith('<')) continue;
            text = text.ReplaceLineEndings(" ").Trim();
            return text.Length > MaxTitleLength ? text[..MaxTitleLength] + "…" : text;
        }
        return null;
    }

    static IEnumerable<JsonElement> ReadHeadLines(Stream stream, int maxLines)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        for (var i = 0; i < maxLines && reader.ReadLine() is { } line; i++)
        {
            JsonElement root;
            try
            {
                using var doc = JsonDocument.Parse(line);
                root = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                continue;
            }
            yield return root;
        }
    }

    static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    static DateTimeOffset? EpochMs(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.TryGetInt64(out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : null;

    /// <summary>Opens a file Claude may be writing to at the same time.</summary>
    public static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    static byte[] ReadShared(string path)
    {
        using var fs = OpenShared(path);
        using var ms = new MemoryStream();
        fs.CopyTo(ms);
        return ms.ToArray();
    }
}
