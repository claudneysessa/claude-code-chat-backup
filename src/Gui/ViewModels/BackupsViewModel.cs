namespace ClaudeChatBackup.Gui.ViewModels;

/// <summary>
/// Um arquivo de backup encontrado na pasta. <c>Error</c> preenchido quando o .zip não é um backup
/// desta ferramenta (ou está ilegível): a linha aparece, mas não dá para restaurar.
/// </summary>
internal sealed record BackupEntry(
    string Path,
    DateTimeOffset When,
    string Origin,
    int Sessions,
    int Files,
    long DataBytes,
    long ZipBytes,
    bool Partial,
    bool IsPreRestoreCopy,
    string? Error)
{
    public bool CanRestore => Error is null;
}

/// <summary>Lógica pura da tela "Backups" e do diálogo de restauração.</summary>
internal static class BackupsViewModel
{
    public const string PreRestorePrefix = "antes-da-restauracao-";

    /// <summary>Backups das pastas informadas, do mais recente para o mais antigo.</summary>
    public static IReadOnlyList<BackupEntry> List(IEnumerable<string> dirs) =>
        dirs.Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(d => Directory.GetFiles(d, "*.zip"))
            .Select(Read)
            .OrderByDescending(e => e.When)
            .ToList();

    public static BackupEntry Read(string zip)
    {
        var info = new FileInfo(zip);
        var preRestore = info.Name.StartsWith(PreRestorePrefix, StringComparison.OrdinalIgnoreCase);
        try
        {
            using var archive = ArchiveReader.Open(zip);
            var m = archive.Manifest;
            return new BackupEntry(zip, m.CreatedAt, $@"{m.MachineName}\{m.UserName}", m.Sessions.Count,
                m.Files.Count, m.Files.Sum(f => f.Size), info.Length, m.Partial, preRestore, null);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException
                                      or System.Text.Json.JsonException)
        {
            return new BackupEntry(zip, info.LastWriteTime, "—", 0, 0, 0, info.Length, false, preRestore,
                "Não é um backup do ClaudeChatBackup ou está corrompido.");
        }
    }

    /// <summary>Linha principal: "2 sessão(ões) · 3 arquivo(s)", com o tipo quando não é completo.</summary>
    public static string DescribeContent(BackupEntry e)
    {
        if (e.Error is not null) return "Arquivo inválido";
        var text = $"{e.Sessions} sessão(ões) · {e.Files} arquivo(s)";
        if (e.IsPreRestoreCopy) return text + " · cópia antes de restaurar";
        return e.Partial ? text + " · parcial" : text;
    }

    /// <summary>Linha de baixo: nome do arquivo, ou o motivo de não dar para restaurar.</summary>
    public static string Detail(BackupEntry e) => e.Error ?? System.IO.Path.GetFileName(e.Path);

    /// <summary>
    /// Pastas de trabalho do backup que não existem neste computador, reduzidas à pasta-mãe comum
    /// quando ela também não existe (ex.: <c>D:\Work</c> em vez de cada projeto). Caminhos dentro
    /// do perfil do usuário de origem ficam de fora: esses são trocados automaticamente.
    /// </summary>
    public static IReadOnlyList<string> SuggestMappings(BackupManifest manifest, Func<string, bool> directoryExists)
    {
        var cwds = manifest.Sessions.Select(s => s.Cwd)
            .Concat(manifest.Projects.Select(p => p.Cwd))
            .OfType<string>()
            .Where(c => c.Length > 3 && !IsUnder(c, manifest.UserProfile) && !directoryExists(c))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var roots = new List<string>();
        foreach (var cwd in cwds)
        {
            var parent = System.IO.Path.GetDirectoryName(cwd.TrimEnd('\\', '/'));
            // Pasta-mãe que já existe aqui não precisa de troca: sugerir só o projeto que falta.
            var root = parent is null || parent.Length <= 3 || directoryExists(parent) ? cwd : parent;
            if (!roots.Any(r => IsUnder(root, r)))
            {
                roots.RemoveAll(r => IsUnder(r, root));
                roots.Add(root);
            }
        }
        return roots.OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static bool IsUnder(string path, string root)
    {
        if (string.IsNullOrEmpty(root)) return false;
        var r = root.TrimEnd('\\', '/');
        return path.Equals(r, StringComparison.OrdinalIgnoreCase)
               || path.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Resumo do plano em uma frase, como aparece no diálogo de restauração.</summary>
    public static string DescribePlan(RestorePlan plan)
    {
        var parts = new List<string>
        {
            $"{plan.Count(RestoreAction.Create)} arquivo(s) novo(s)",
            $"{plan.Count(RestoreAction.Overwrite)} a substituir",
            $"{plan.Count(RestoreAction.Skip)} sem mudança",
        };
        return string.Join(" · ", parts);
    }
}
