namespace ClaudeChatBackup.Application;

public enum ConflictPolicy
{
    /// <summary>Overwrite only when the file in the backup is newer than the one on disk.</summary>
    Newer,
    Overwrite,
    Skip,
}

public enum RestoreAction { Create, Overwrite, Skip }

public sealed class RestoreOptions
{
    /// <summary>Extra folder mappings (e.g. D:\Code → E:\Code), applied before the automatic ones.</summary>
    public IReadOnlyList<PathMapping> Mappings { get; init; } = [];

    /// <summary>Account/org to move sessions into. Null picks the only one present on the target, if any.</summary>
    public ManifestScope? TargetScope { get; init; }

    /// <summary>Keep the account/org folders from the backup even when the target uses different ones.</summary>
    public bool KeepScope { get; init; }

    public ConflictPolicy Conflict { get; init; } = ConflictPolicy.Newer;

    /// <summary>
    /// Sessions to restore (Desktop local_… or CLI id, prefix allowed). Empty restores everything,
    /// including shared files; otherwise only the files of the chosen sessions.
    /// </summary>
    public IReadOnlyList<string> SessionFilter { get; init; } = [];

    /// <summary>Where the copy of overwritten files goes. Null disables it.</summary>
    public string? RollbackDir { get; init; }
}

public sealed record RestoreItem(ManifestFile File, string TargetPath, RestoreAction Action, ContentKind Kind, string? Reason);

public sealed class RestorePlan
{
    public required BackupManifest Manifest { get; init; }
    public required ClaudeLocations Target { get; init; }
    public required PathRemapper Remapper { get; init; }
    public required IReadOnlyList<RestoreItem> Items { get; init; }
    public required IReadOnlyDictionary<string, string> ProjectRenames { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }

    public int Count(RestoreAction a) => Items.Count(i => i.Action == a);
}

public sealed record RestoreResult(RestorePlan Plan, string? RollbackZip);

public static class RestoreService
{
    public static RestorePlan Plan(ArchiveReader archive, ClaudeLocations target, RestoreOptions? options = null)
    {
        options ??= new RestoreOptions();
        var m = archive.Manifest;
        var warnings = new List<string>();

        var mappings = new List<PathMapping>(options.Mappings);
        if (m.DesktopRoot is not null && target.DesktopRoot is not null)
            mappings.Add(new PathMapping(m.DesktopRoot, target.DesktopRoot));
        mappings.Add(new PathMapping(m.DotClaudeRoot, target.DotClaudeRoot));
        mappings.Add(new PathMapping(m.UserProfile, target.UserProfile));

        var ids = ResolveScope(m, target, options, warnings);
        var remapper = new PathRemapper(mappings, ids);
        var renames = ProjectRenames(m, remapper);

        var desktopSkipped = 0;
        var items = new List<RestoreItem>();
        foreach (var f in SelectFiles(m, options.SessionFilter))
        {
            var path = MapTarget(f.Path, target, remapper.Ids, renames);
            if (path is null)
            {
                desktopSkipped++;
                continue;
            }
            var kind = DataLayout.GetContentKind(f.Path);
            var (action, reason) = Decide(f, path, kind, options.Conflict);
            items.Add(new RestoreItem(f, path, action, kind, reason));
        }

        if (desktopSkipped > 0)
            warnings.Add($"Claude Desktop não encontrado no destino: {desktopSkipped} arquivo(s) do Desktop ignorados. " +
                         "Abra o Claude Desktop uma vez neste computador ou informe --desktop-dir.");

        foreach (var i in items.Where(i => i.Action != RestoreAction.Skip))
        {
            var name = Path.GetFileNameWithoutExtension(i.TargetPath);
            if (!name.StartsWith("local_", StringComparison.Ordinal)) continue;
            var marker = Path.Combine(Path.GetDirectoryName(i.TargetPath)!, "deleted_" + name["local_".Length..]);
            if (File.Exists(marker))
                warnings.Add($"Sessão {name} está marcada como apagada no destino ({Path.GetFileName(marker)}); pode não aparecer no app.");
        }

        return new RestorePlan
        {
            Manifest = m,
            Target = target,
            Remapper = remapper,
            Items = items,
            ProjectRenames = renames,
            Warnings = warnings,
        };
    }

    public static RestoreResult Execute(
        ArchiveReader archive, RestorePlan plan, RestoreOptions? options = null, IProgress<string>? progress = null)
    {
        options ??= new RestoreOptions();

        var problems = archive.Verify();
        if (problems.Count > 0)
            throw new InvalidDataException("Backup corrompido, nada foi restaurado:\n  " + string.Join("\n  ", problems));

        var rollback = WriteRollback(plan, options.RollbackDir);

        foreach (var item in plan.Items.Where(i => i.Action != RestoreAction.Skip))
        {
            progress?.Report(item.TargetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(item.TargetPath)!);
            var tmp = item.TargetPath + ".ccb-tmp";
            try
            {
                using (var src = archive.OpenFile(item.File))
                using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var ids = item.File.Path.StartsWith(DataLayout.DesktopPrefix, StringComparison.Ordinal);
                    switch (item.Kind)
                    {
                        case ContentKind.Json: plan.Remapper.RemapJsonFile(src, dst, ids); break;
                        case ContentKind.JsonLines: plan.Remapper.RemapJsonLinesFile(src, dst, ids); break;
                        default: src.CopyTo(dst); break;
                    }
                }
                File.SetLastWriteTimeUtc(tmp, DateTime.SpecifyKind(item.File.LastWriteUtc, DateTimeKind.Utc));
                File.Move(tmp, item.TargetPath, overwrite: true);
            }
            catch
            {
                File.Delete(tmp);
                throw;
            }
        }

        return new RestoreResult(plan, rollback);
    }

    static IEnumerable<ManifestFile> SelectFiles(BackupManifest m, IReadOnlyList<string> filter)
    {
        if (filter.Count == 0) return m.Files;
        var chosen = BackupService.Select(m.Sessions, filter);
        if (chosen.Count == 0)
            throw new InvalidOperationException("Nenhuma sessão do backup corresponde à seleção.");
        return m.Files.Where(f => chosen.Any(s => DataLayout.BelongsToSession(
            f.Path, s.Id.StartsWith("local_", StringComparison.OrdinalIgnoreCase) ? s.Id : null, s.CliSessionId)));
    }

    /// <summary>Copies every file about to be overwritten into a normal backup archive.</summary>
    static string? WriteRollback(RestorePlan plan, string? rollbackDir)
    {
        var overwrites = plan.Items.Where(i => i.Action == RestoreAction.Overwrite).ToList();
        if (rollbackDir is null || overwrites.Count == 0) return null;

        var t = plan.Target;
        var items = overwrites.Select(i =>
            t.DesktopRoot is { } d && IsUnder(i.TargetPath, d)
                ? new ArchiveItem(i.TargetPath, DataLayout.ToArchivePath(DataLayout.DesktopPrefix, d, i.TargetPath))
                : new ArchiveItem(i.TargetPath, DataLayout.ToArchivePath(DataLayout.DotClaudePrefix, t.DotClaudeRoot, i.TargetPath)));

        var zip = Path.Combine(rollbackDir, $"antes-da-restauracao-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        ArchiveWriter.Write(zip, new BackupManifest
        {
            CreatedAt = DateTimeOffset.Now,
            MachineName = Environment.MachineName,
            UserName = Environment.UserName,
            UserProfile = t.UserProfile,
            DesktopRoot = t.DesktopRoot,
            DotClaudeRoot = t.DotClaudeRoot,
            Partial = true,
        }, items);
        return zip;
    }

    static Dictionary<string, string> ResolveScope(
        BackupManifest m, ClaudeLocations target, RestoreOptions options, List<string> warnings)
    {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (options.KeepScope || target.DesktopRoot is null || m.Scopes.Count == 0) return ids;

        var targetScope = options.TargetScope;
        if (targetScope is null)
        {
            var existing = DataLayout.DesktopDirs
                .SelectMany(d => SessionCatalog.EnumerateScopes(Path.Combine(target.DesktopRoot, d)))
                .Distinct()
                .ToList();
            if (existing.Count == 0 || existing.Any(s => m.Scopes.Contains(s))) return ids;
            if (existing.Count > 1)
            {
                warnings.Add("O destino tem mais de uma conta/organização; sessões mantidas na conta original. " +
                             "Use --target-scope <conta>/<org> para escolher.");
                return ids;
            }
            targetScope = existing[0];
        }

        if (m.Scopes.Count > 1)
        {
            warnings.Add("O backup tem mais de uma conta/organização; sessões mantidas nas contas originais.");
            return ids;
        }

        var source = m.Scopes[0];
        if (source == targetScope) return ids;
        ids[source.Account] = targetScope.Account;
        ids[source.Org] = targetScope.Org;
        warnings.Add($"Sessões movidas da conta {source.Account}/{source.Org} para {targetScope.Account}/{targetScope.Org}.");
        return ids;
    }

    /// <summary>Project folders are named after their working directory, so they move with it.</summary>
    static Dictionary<string, string> ProjectRenames(BackupManifest m, PathRemapper remapper)
    {
        var renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in m.Projects)
        {
            if (p.Cwd is null || !DataLayout.EncodeProjectDir(p.Cwd).Equals(p.Dir, StringComparison.OrdinalIgnoreCase))
                continue; // long paths get a hashed name; leave those alone
            if (remapper.MapPath(p.Cwd) is { } newCwd)
                renames[p.Dir] = DataLayout.EncodeProjectDir(newCwd);
        }
        return renames;
    }

    static string? MapTarget(
        string archivePath, ClaudeLocations target, IReadOnlyDictionary<string, string> ids,
        IReadOnlyDictionary<string, string> renames)
    {
        string root;
        string[] parts;
        if (archivePath.StartsWith(DataLayout.DesktopPrefix, StringComparison.Ordinal))
        {
            if (target.DesktopRoot is null) return null;
            root = target.DesktopRoot;
            parts = archivePath[DataLayout.DesktopPrefix.Length..].Split('/');
            if (parts.Length > 1 && DataLayout.DesktopDirs.Contains(parts[0]))
                for (var i = 1; i < parts.Length - 1; i++)
                    if (ids.TryGetValue(parts[i], out var to)) parts[i] = to;
        }
        else
        {
            root = target.DotClaudeRoot;
            parts = archivePath[DataLayout.DotClaudePrefix.Length..].Split('/');
            if (parts.Length > 2 && parts[0] == DataLayout.Projects && renames.TryGetValue(parts[1], out var dir))
                parts[1] = dir;
        }

        var full = Path.GetFullPath(Path.Combine([root, .. parts]));
        if (!IsUnder(full, root))
            throw new InvalidDataException($"Caminho fora da pasta de destino: {archivePath}");
        return full;
    }

    static (RestoreAction, string?) Decide(ManifestFile f, string path, ContentKind kind, ConflictPolicy policy)
    {
        var existing = new FileInfo(path);
        if (!existing.Exists) return (RestoreAction.Create, null);

        if (kind == ContentKind.Raw && existing.Length == f.Size)
        {
            using var s = SessionCatalog.OpenShared(path);
            if (ArchiveReader.Hash(s).Sha256.Equals(f.Sha256, StringComparison.OrdinalIgnoreCase))
                return (RestoreAction.Skip, "idêntico");
        }

        return policy switch
        {
            ConflictPolicy.Overwrite => (RestoreAction.Overwrite, null),
            ConflictPolicy.Skip => (RestoreAction.Skip, "já existe"),
            _ => DateTime.SpecifyKind(f.LastWriteUtc, DateTimeKind.Utc) > existing.LastWriteTimeUtc.AddSeconds(2)
                ? (RestoreAction.Overwrite, "backup mais recente")
                : (RestoreAction.Skip, "destino igual ou mais recente"),
        };
    }

    static bool IsUnder(string path, string root)
    {
        var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }
}
