using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ClaudeChatBackup.Infrastructure;

/// <summary>
/// Finds running Claude Desktop processes. The Claude Code CLI is also called claude.exe, so processes are
/// told apart by where the executable lives.
/// </summary>
public static partial class ClaudeDesktopProcess
{
    public static bool IsRunning() => Find().Count > 0;

    public static IReadOnlyList<Process> Find()
    {
        var found = new List<Process>();
        foreach (var p in Process.GetProcessesByName("claude"))
        {
            var path = TryGetPath(p);
            // A path we cannot read is treated as Desktop: better a false warning than a corrupted restore.
            if (path is null || IsDesktopPath(path)) found.Add(p);
        }
        return found;
    }

    public static bool IsDesktopPath(string path) =>
        path.Contains(@"\WindowsApps\Claude_", StringComparison.OrdinalIgnoreCase)
        || path.Contains(@"\AnthropicClaude\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Version of the running Claude Desktop, e.g. 3.1623.4.0, or null when it is not running.</summary>
    public static string? TryGetVersion()
    {
        foreach (var p in Find())
        {
            var path = TryGetPath(p);
            if (path is null) continue;
            var m = StoreVersion().Match(path);
            if (m.Success) return m.Groups[1].Value;
            var v = FileVersionInfo.GetVersionInfo(path).ProductVersion;
            if (!string.IsNullOrEmpty(v)) return v;
        }
        return null;
    }

    /// <summary>Asks Claude Desktop to close its windows and waits for it to exit.</summary>
    public static bool TryClose(TimeSpan timeout)
    {
        foreach (var p in Find()) p.CloseMainWindow();
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!IsRunning()) return true;
            Thread.Sleep(500);
        }
        return !IsRunning();
    }

    static string? TryGetPath(Process p)
    {
        try { return p.MainModule?.FileName; }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return null; }
    }

    [GeneratedRegex(@"\\Claude_(\d+(?:\.\d+){1,3})_", RegexOptions.IgnoreCase)]
    private static partial Regex StoreVersion();
}
