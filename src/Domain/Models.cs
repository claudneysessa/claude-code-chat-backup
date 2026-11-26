using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeChatBackup.Domain;

public enum SessionKind { Desktop, Cli }

/// <summary>One Claude Code session, as listed by the Desktop app or found only as a CLI transcript.</summary>
public sealed class SessionInfo
{
    public required string Id { get; set; }
    public string? CliSessionId { get; set; }
    public SessionKind Kind { get; set; }
    public string? Title { get; set; }
    public string? Cwd { get; set; }
    public string? Model { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
    public string? Account { get; set; }
    public string? Org { get; set; }
    public long TranscriptBytes { get; set; }

    [JsonIgnore] public string? DesktopFile { get; set; }
    [JsonIgnore] public string? TranscriptFile { get; set; }
}

/// <summary>Describes a backup archive. Stored as manifest.json at the archive root.</summary>
public sealed class BackupManifest
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string Tool { get; set; } = "ClaudeChatBackup";
    public string? ToolVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string MachineName { get; set; } = "";
    public string UserName { get; set; } = "";
    public string UserProfile { get; set; } = "";
    public string? DesktopRoot { get; set; }
    public string DotClaudeRoot { get; set; } = "";
    public string? ClaudeDesktopVersion { get; set; }
    public bool Partial { get; set; }
    public List<ManifestScope> Scopes { get; set; } = [];
    public List<ManifestProject> Projects { get; set; } = [];
    public List<SessionInfo> Sessions { get; set; } = [];
    public List<ManifestFile> Files { get; set; } = [];
}

/// <summary>An account/org folder pair under claude-code-sessions or local-agent-mode-sessions.</summary>
public sealed record ManifestScope(string Account, string Org);

/// <summary>A folder under ~/.claude/projects and the working directory it was named after.</summary>
public sealed record ManifestProject(string Dir, string? Cwd);

public sealed record ManifestFile(string Path, long Size, string Sha256, DateTime LastWriteUtc);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BackupManifest))]
internal partial class ManifestJsonContext : JsonSerializerContext;

public static class ManifestSerializer
{
    public static void Write(Stream stream, BackupManifest manifest) =>
        JsonSerializer.Serialize(stream, manifest, ManifestJsonContext.Default.BackupManifest);

    public static BackupManifest Read(Stream stream) =>
        JsonSerializer.Deserialize(stream, ManifestJsonContext.Default.BackupManifest)
        ?? throw new InvalidDataException("manifest.json vazio.");
}
