namespace ClaudeChatBackup.Application;

public sealed class BackupOptions
{
    /// <summary>Session ids (Desktop local_… or CLI id, prefix allowed). Empty means everything.</summary>
    public IReadOnlyList<string> SessionFilter { get; init; } = [];
    public string? MachineName { get; init; }
    public string? UserName { get; init; }
}

public sealed record BackupResult(string ZipPath, BackupManifest Manifest, IReadOnlyList<SessionInfo> Sessions);

public static class BackupService
{
    public static BackupResult Run(
        ClaudeLocations loc, string zipPath, BackupOptions? options = null, IProgress<string>? progress = null)
    {
        options ??= new BackupOptions();
        var catalog = SessionCatalog.Load(loc);
        var partial = options.SessionFilter.Count > 0;
        var sessions = partial ? Select(catalog.Sessions, options.SessionFilter) : catalog.Sessions;

        var items = partial ? ItemsForSessions(loc, sessions) : ItemsForEverything(loc);
        items = items.DistinctBy(i => i.ArchivePath, StringComparer.OrdinalIgnoreCase).ToList();

        var usedProjectDirs = items
            .Select(i => i.ArchivePath.Split('/'))
            .Where(p => p.Length >= 3 && p[0] == "dotclaude" && p[1] == DataLayout.Projects)
            .Select(p => p[2])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var manifest = new BackupManifest
        {
            ToolVersion = typeof(BackupService).Assembly.GetName().Version?.ToString(3),
            CreatedAt = DateTimeOffset.Now,
            MachineName = options.MachineName ?? Environment.MachineName,
            UserName = options.UserName ?? Environment.UserName,
            UserProfile = loc.UserProfile,
            DesktopRoot = loc.DesktopRoot,
            DotClaudeRoot = loc.DotClaudeRoot,
            ClaudeDesktopVersion = ClaudeDesktopProcess.TryGetVersion(),
            Partial = partial,
            Scopes = catalog.Scopes.ToList(),
            Projects = catalog.ProjectDirs
                .Select(kv => new ManifestProject(Path.GetFileName(kv.Key), kv.Value))
                .Where(p => usedProjectDirs.Contains(p.Dir))
                .ToList(),
            Sessions = sessions.ToList(),
        };

        ArchiveWriter.Write(zipPath, manifest, items, progress);
        return new BackupResult(zipPath, manifest, sessions);
    }

    public static IReadOnlyList<SessionInfo> Select(IEnumerable<SessionInfo> sessions, IReadOnlyList<string> filter)
    {
        static string Bare(string id) => id.StartsWith("local_", StringComparison.OrdinalIgnoreCase) ? id[6..] : id;
        return sessions
            .Where(s => filter.Any(f =>
                Bare(s.Id).StartsWith(Bare(f), StringComparison.OrdinalIgnoreCase)
                || (s.CliSessionId?.StartsWith(Bare(f), StringComparison.OrdinalIgnoreCase) ?? false)))
            .ToList();
    }

    static List<ArchiveItem> ItemsForEverything(ClaudeLocations loc)
    {
        var items = new List<ArchiveItem>();
        if (loc.DesktopRoot is { } desktop)
            AddTree(items, DataLayout.DesktopPrefix, desktop, DataLayout.DesktopDirs, DataLayout.DesktopFiles);
        AddTree(items, DataLayout.DotClaudePrefix, loc.DotClaudeRoot, DataLayout.DotClaudeDirs, DataLayout.DotClaudeFiles);
        return items;
    }

    static void AddTree(List<ArchiveItem> items, string prefix, string root, string[] dirs, string[] files)
    {
        foreach (var name in files)
        {
            var path = Path.Combine(root, name);
            if (File.Exists(path)) items.Add(new ArchiveItem(path, DataLayout.ToArchivePath(prefix, root, path)));
        }
        foreach (var name in dirs)
            AddDirectory(items, prefix, root, Path.Combine(root, name));
    }

    static void AddDirectory(List<ArchiveItem> items, string prefix, string root, string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            items.Add(new ArchiveItem(file, DataLayout.ToArchivePath(prefix, root, file)));
    }

    /// <summary>Only what one session needs: Desktop metadata, transcript, tool results and file history.</summary>
    static List<ArchiveItem> ItemsForSessions(ClaudeLocations loc, IEnumerable<SessionInfo> sessions)
    {
        var items = new List<ArchiveItem>();
        foreach (var s in sessions)
        {
            if (s.DesktopFile is { } df && loc.DesktopRoot is { } desktop)
                items.Add(new ArchiveItem(df, DataLayout.ToArchivePath(DataLayout.DesktopPrefix, desktop, df)));

            if (s.TranscriptFile is { } tf)
            {
                items.Add(new ArchiveItem(tf, DataLayout.ToArchivePath(DataLayout.DotClaudePrefix, loc.DotClaudeRoot, tf)));
                var sidecar = Path.Combine(Path.GetDirectoryName(tf)!, Path.GetFileNameWithoutExtension(tf));
                AddDirectory(items, DataLayout.DotClaudePrefix, loc.DotClaudeRoot, sidecar);
            }

            if (s.CliSessionId is { } cli)
                AddDirectory(items, DataLayout.DotClaudePrefix, loc.DotClaudeRoot,
                    Path.Combine(loc.DotClaudeRoot, DataLayout.FileHistory, cli));
        }
        return items;
    }
}
