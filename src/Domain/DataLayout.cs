using System.Text.RegularExpressions;

namespace ClaudeChatBackup.Domain;

/// <summary>
/// What goes into a backup and how archive paths map back to disk.
/// Archive paths always use '/' and start with <see cref="DesktopPrefix"/> or <see cref="DotClaudePrefix"/>.
/// </summary>
public static partial class DataLayout
{
    public const string DesktopPrefix = "desktop/";
    public const string DotClaudePrefix = "dotclaude/";
    public const string ManifestEntry = "manifest.json";

    public const string DesktopCodeSessions = "claude-code-sessions";
    public const string DesktopAgentSessions = "local-agent-mode-sessions";
    public const string Projects = "projects";
    public const string FileHistory = "file-history";

    public static readonly string[] DesktopDirs = [DesktopCodeSessions, DesktopAgentSessions];

    // config.json is left out on purpose: it holds the OAuth token cache. ccd-ids.json holds a device salt.
    public static readonly string[] DesktopFiles =
    [
        "claude_desktop_config.json",
        "ccd-session-presets.json",
        "git-worktrees.json",
        "extensions-installations.json",
        "mcp-user-tool-toggles.json",
        "cowork-enabled-cli-ops.json",
    ];

    public static readonly string[] DotClaudeDirs = [Projects, FileHistory, "skills"];

    // .credentials.json is never included.
    public static readonly string[] DotClaudeFiles = ["history.jsonl", "settings.json", "CLAUDE.md"];

    /// <summary>Folder name Claude Code uses under ~/.claude/projects for a working directory.</summary>
    public static string EncodeProjectDir(string cwd) => NonAlphanumeric().Replace(cwd, "-");

    [GeneratedRegex("[^a-zA-Z0-9]")]
    private static partial Regex NonAlphanumeric();

    /// <summary>How a file's content is rewritten when paths are remapped on restore.</summary>
    public static ContentKind GetContentKind(string archivePath)
    {
        if (archivePath.StartsWith(DesktopPrefix, StringComparison.Ordinal))
        {
            var rel = archivePath[DesktopPrefix.Length..];
            if (!rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return ContentKind.Raw;
            return !rel.Contains('/') || rel.StartsWith(DesktopCodeSessions + "/") || rel.StartsWith(DesktopAgentSessions + "/")
                ? ContentKind.Json
                : ContentKind.Raw;
        }

        if (archivePath.StartsWith(DotClaudePrefix, StringComparison.Ordinal))
        {
            var parts = archivePath[DotClaudePrefix.Length..].Split('/');
            // Transcripts sit directly in projects/<dir>/. Tool results and file-history snapshots stay byte-for-byte.
            if (parts is [Projects, _, var name] && name.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
                return ContentKind.JsonLines;
            if (parts is ["history.jsonl"])
                return ContentKind.JsonLines;
        }

        return ContentKind.Raw;
    }

    /// <summary>
    /// True when an archive file belongs to one session: its Desktop metadata (<c>local_….json</c>), its
    /// transcript, the transcript's sidecar folder (tool results, subagents) or its file history.
    /// Shared files (history.jsonl, settings, Desktop config, Cowork) belong to no session.
    /// </summary>
    public static bool BelongsToSession(string archivePath, string? localId, string? cliId)
    {
        var parts = archivePath.Split('/');
        if (localId is not null && parts is ["desktop", DesktopCodeSessions, ..])
            return parts[^1].Equals(localId + ".json", StringComparison.OrdinalIgnoreCase);
        if (cliId is null) return false;
        return parts switch
        {
            ["dotclaude", Projects, _, var name] => name.Equals(cliId + ".jsonl", StringComparison.OrdinalIgnoreCase),
            ["dotclaude", Projects, _, var dir, _, ..] => dir.Equals(cliId, StringComparison.OrdinalIgnoreCase),
            ["dotclaude", FileHistory, var id, _, ..] => id.Equals(cliId, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    /// <summary>Rejects anything that could escape the target folder (zip slip).</summary>
    public static bool IsSafeArchivePath(string archivePath)
    {
        if (!archivePath.StartsWith(DesktopPrefix, StringComparison.Ordinal)
            && !archivePath.StartsWith(DotClaudePrefix, StringComparison.Ordinal))
            return false;
        if (archivePath.Contains('\\') || archivePath.Contains(':')) return false;
        return archivePath.Split('/').All(s => s.Length > 0 && s != "." && s != "..");
    }

    public static string ToArchivePath(string prefix, string root, string fullPath) =>
        prefix + Path.GetRelativePath(root, fullPath).Replace('\\', '/');
}

public enum ContentKind { Raw, Json, JsonLines }
