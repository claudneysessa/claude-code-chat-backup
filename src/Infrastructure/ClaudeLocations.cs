namespace ClaudeChatBackup.Infrastructure;

/// <summary>
/// Where Claude keeps its data on one machine: the Claude Desktop data root and the Claude Code
/// folder (~/.claude). Either can be overridden, which is how tests and cross-machine restores work.
/// </summary>
public sealed record ClaudeLocations(string? DesktopRoot, string DotClaudeRoot, string UserProfile)
{
    public static ClaudeLocations Discover(string? desktopOverride = null, string? dotClaudeOverride = null)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dotClaude = dotClaudeOverride
            ?? Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")
            ?? Path.Combine(profile, ".claude");
        var desktop = desktopOverride ?? FindDesktopRoots().FirstOrDefault();
        return new ClaudeLocations(
            desktop is null ? null : Path.GetFullPath(desktop),
            Path.GetFullPath(dotClaude),
            profile);
    }

    /// <summary>
    /// Candidate Claude Desktop data roots, most recently used first:
    /// %LOCALAPPDATA%\Claude-Data\claude_* (Microsoft Store / MSIX) and %APPDATA%\Claude (classic installer).
    /// </summary>
    public static IReadOnlyList<string> FindDesktopRoots()
    {
        var candidates = new List<string>();

        var claudeData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Claude-Data");
        if (Directory.Exists(claudeData))
            candidates.AddRange(Directory.EnumerateDirectories(claudeData, "claude_*"));

        candidates.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude"));

        return candidates
            .Where(LooksLikeDesktopRoot)
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .ToList();
    }

    static bool LooksLikeDesktopRoot(string dir) =>
        Directory.Exists(Path.Combine(dir, DataLayout.DesktopCodeSessions))
        || Directory.Exists(Path.Combine(dir, DataLayout.DesktopAgentSessions))
        || File.Exists(Path.Combine(dir, "claude_desktop_config.json"));
}
