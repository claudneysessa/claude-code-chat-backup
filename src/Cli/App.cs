using System.Globalization;

namespace ClaudeChatBackup.Cli;

/// <summary>Linha de comando: paths, list, backup, verify e restore. Mensagens em pt-BR.</summary>
internal static class CliApp
{
    public static int Run(string[] argv)
    {
        try
        {
            var a = Args.Parse(argv);
            if (a.Command is null or "help" || a.Has("--help"))
            {
                Console.WriteLine(Usage);
                return a.Command is null && !a.Has("--help") ? 2 : 0;
            }

            return a.Command switch
            {
                "paths" => Paths(a),
                "list" => List(a),
                "backup" => Backup(a),
                "verify" => Verify(a),
                "restore" => Restore(a),
                _ => throw new UsageException($"Comando desconhecido: {a.Command}"),
            };
        }
        catch (UsageException e)
        {
            Console.Error.WriteLine($"Erro: {e.Message}");
            Console.Error.WriteLine("Use 'ClaudeChatBackup help' para ver os comandos.");
            return 2;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Erro: {e.Message}");
            return 1;
        }
    }

    public const string Usage = """
        ClaudeChatBackup - backup e restore das sessões do Claude Desktop (aba Code / Cowork)

        Sem argumentos (ou com 'gui') abre a janela. Uso pela linha de comando:
          ClaudeChatBackup paths
          ClaudeChatBackup list    [--from <backup.zip>]
          ClaudeChatBackup backup  [--out <backup.zip>] [--session <id>]...
          ClaudeChatBackup verify  <backup.zip>
          ClaudeChatBackup restore <backup.zip> [opções]

        Opções do restore:
          --session <id>             Restaura só esta sessão (prefixo do id; veja 'list --from'). Pode repetir.
          --map <ORIGEM>=<DESTINO>   Troca uma pasta (ex.: --map "D:\Code=E:\Code"). Pode repetir.
          --conflict newer|overwrite|skip
                                     Arquivo que já existe: sobrescreve se o backup for mais novo (padrão),
                                     sempre, ou nunca.
          --target-scope <conta>/<org>
                                     Conta/organização de destino quando houver mais de uma.
          --keep-scope               Não move as sessões para a conta do destino.
          --dry-run                  Mostra o que seria feito, sem gravar nada.
          --verbose                  Lista cada arquivo.
          --close-claude             Fecha o Claude Desktop antes de restaurar.
          --yes                      Não pede confirmação.
          --no-rollback              Não guarda cópia dos arquivos sobrescritos.
          --rollback-dir <pasta>     Onde guardar essa cópia (padrão: a pasta de backups).
          --force                    Restaura mesmo com o Claude Desktop aberto (não recomendado).

        Opções gerais:
          --desktop-dir <pasta>      Pasta de dados do Claude Desktop (padrão: detectada).
          --claude-dir <pasta>       Pasta do Claude Code (padrão: %USERPROFILE%\.claude).
          --home <pasta>             Perfil do usuário de destino (padrão: o atual).

        O que fica de fora de propósito: tokens de login (config.json, .credentials.json), caches e
        binários. Chats da aba Chat (claude.ai) ficam no servidor e não estão nestes arquivos.
        """;

    static ClaudeLocations Locate(Args a)
    {
        var loc = ClaudeLocations.Discover(a.Get("--desktop-dir"), a.Get("--claude-dir"));
        return a.Get("--home") is { } home ? loc with { UserProfile = Path.GetFullPath(home) } : loc;
    }

    public static int Paths(Args a)
    {
        a.EnsureOnly("--home");
        var loc = Locate(a);
        Console.WriteLine($"Claude Desktop : {loc.DesktopRoot ?? "(não encontrado)"}");
        Console.WriteLine($"Claude Code    : {loc.DotClaudeRoot}{(Directory.Exists(loc.DotClaudeRoot) ? "" : "  (não existe)")}");
        Console.WriteLine($"Perfil         : {loc.UserProfile}");
        var others = ClaudeLocations.FindDesktopRoots().Skip(1).ToList();
        foreach (var o in others) Console.WriteLine($"Outra instalação: {o}");
        var running = ClaudeDesktopProcess.IsRunning();
        Console.WriteLine($"Desktop aberto : {(running ? "sim" : "não")}{(running && ClaudeDesktopProcess.TryGetVersion() is { } v ? $" (versão {v})" : "")}");
        if (loc.DesktopRoot is not null)
            foreach (var s in DataLayout.DesktopDirs.SelectMany(d => SessionCatalog.EnumerateScopes(Path.Combine(loc.DesktopRoot, d))).Distinct())
                Console.WriteLine($"Conta/org      : {s.Account}/{s.Org}");
        return 0;
    }

    public static int List(Args a)
    {
        a.EnsureOnly("--from", "--home");
        IReadOnlyList<SessionInfo> sessions;
        if (a.Get("--from") is { } zip)
        {
            using var archive = ArchiveReader.Open(zip);
            archive.RecoverFullTitles();
            PrintManifestHeader(archive.Manifest);
            sessions = archive.Manifest.Sessions;
        }
        else
        {
            sessions = SessionCatalog.Load(Locate(a)).Sessions;
        }

        Console.WriteLine($"{"ID",-10} {"ATIVIDADE",-16} {"TIPO",-7} {"TAMANHO",9}  TÍTULO  [PASTA]");
        foreach (var s in sessions)
        {
            var id = (s.Id.StartsWith("local_") ? s.Id[6..] : s.Id)[..Math.Min(8, s.Id.Length)];
            var when = s.LastActivityAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "-";
            var kind = s.Kind == SessionKind.Desktop ? (s.IsArchived ? "arquiv." : "desktop") : "cli";
            Console.WriteLine($"{id,-10} {when,-16} {kind,-7} {FormatSize(s.TranscriptBytes),9}  {s.Title ?? "(sem título)"}  [{s.Cwd ?? "?"}]");
        }
        Console.WriteLine($"\n{sessions.Count} sessão(ões).");
        return 0;
    }

    public static int Backup(Args a)
    {
        a.EnsureOnly("--out", "--session", "--home");
        var loc = Locate(a);
        if (loc.DesktopRoot is null && !Directory.Exists(loc.DotClaudeRoot))
            throw new IOException("Nenhuma pasta do Claude encontrada neste computador.");

        var output = a.Get("--out") ?? BackupLocation.NewBackupPath();
        output = Path.GetFullPath(output);

        var filter = a.GetAll("--session");
        if (ClaudeDesktopProcess.IsRunning())
            Console.WriteLine("Aviso: Claude Desktop aberto. O backup funciona, mas a sessão em uso pode sair sem o último turno.");

        using var progress = new ConsoleProgress("Copiando");
        var result = BackupService.Run(loc, output, new BackupOptions { SessionFilter = filter }, progress);
        progress.Done();

        if (filter.Count > 0 && result.Sessions.Count == 0)
        {
            File.Delete(output);
            throw new InvalidOperationException("Nenhuma sessão corresponde ao filtro --session.");
        }

        var total = result.Manifest.Files.Sum(f => f.Size);
        Console.WriteLine($"Backup criado: {output}");
        Console.WriteLine($"  {result.Sessions.Count} sessão(ões), {result.Manifest.Files.Count} arquivo(s), " +
                          $"{FormatSize(total)} de dados, {FormatSize(new FileInfo(output).Length)} no zip.");
        return 0;
    }

    public static int Verify(Args a)
    {
        a.EnsureOnly();
        var zip = a.Positionals.FirstOrDefault() ?? throw new UsageException("Informe o arquivo de backup.");
        using var archive = ArchiveReader.Open(zip);
        PrintManifestHeader(archive.Manifest);
        using var progress = new ConsoleProgress("Verificando");
        var problems = archive.Verify(progress);
        progress.Done();
        if (problems.Count == 0)
        {
            Console.WriteLine($"OK: {archive.Manifest.Files.Count} arquivo(s) conferidos.");
            return 0;
        }
        foreach (var p in problems) Console.WriteLine($"  {p}");
        Console.WriteLine($"FALHOU: {problems.Count} problema(s).");
        return 1;
    }

    public static int Restore(Args a)
    {
        a.EnsureOnly("--map", "--session", "--conflict", "--target-scope", "--keep-scope", "--dry-run", "--verbose",
            "--close-claude", "--yes", "--no-rollback", "--rollback-dir", "--force", "--home");
        var zip = a.Positionals.FirstOrDefault() ?? throw new UsageException("Informe o arquivo de backup.");
        var target = Locate(a);
        var options = new RestoreOptions
        {
            Mappings = a.GetAll("--map").Select(ParseMapping).ToList(),
            SessionFilter = a.GetAll("--session"),
            Conflict = a.Get("--conflict")?.ToLowerInvariant() switch
            {
                null or "newer" => ConflictPolicy.Newer,
                "overwrite" => ConflictPolicy.Overwrite,
                "skip" => ConflictPolicy.Skip,
                var c => throw new UsageException($"--conflict inválido: {c}"),
            },
            TargetScope = a.Get("--target-scope") is { } ts ? ParseScope(ts) : null,
            KeepScope = a.Has("--keep-scope"),
            RollbackDir = a.Has("--no-rollback")
                ? null
                : Path.GetFullPath(a.Get("--rollback-dir") ?? BackupLocation.Default),
        };

        using var archive = ArchiveReader.Open(zip);
        PrintManifestHeader(archive.Manifest);
        var plan = RestoreService.Plan(archive, target, options);
        PrintPlan(plan, a.Has("--verbose") || a.Has("--dry-run"));

        if (a.Has("--dry-run"))
        {
            Console.WriteLine("\n--dry-run: nada foi gravado.");
            return 0;
        }
        if (plan.Count(RestoreAction.Create) + plan.Count(RestoreAction.Overwrite) == 0)
        {
            Console.WriteLine("\nNada a restaurar.");
            return 0;
        }

        if (!a.Has("--force") && ClaudeDesktopProcess.IsRunning())
        {
            if (!a.Has("--close-claude"))
                throw new InvalidOperationException(
                    "Claude Desktop está aberto. Feche o app (ou use --close-claude) antes de restaurar: " +
                    "ele regrava as sessões ao fechar e desfaria o restore.");
            Console.WriteLine("Fechando o Claude Desktop...");
            if (!ClaudeDesktopProcess.TryClose(TimeSpan.FromSeconds(30)))
                throw new InvalidOperationException("O Claude Desktop não fechou em 30 s. Feche manualmente e tente de novo.");
        }

        if (!a.Has("--yes") && !Confirm("\nContinuar com o restore?")) return 1;

        using var progress = new ConsoleProgress("Restaurando");
        var result = RestoreService.Execute(archive, plan, options, progress);
        progress.Done();

        Console.WriteLine($"Restore concluído: {plan.Count(RestoreAction.Create)} criado(s), " +
                          $"{plan.Count(RestoreAction.Overwrite)} sobrescrito(s).");
        if (result.RollbackZip is not null)
            Console.WriteLine($"Cópia dos arquivos sobrescritos: {result.RollbackZip}");
        return 0;
    }

    static void PrintManifestHeader(BackupManifest m)
    {
        Console.WriteLine($"Backup de {m.MachineName}\\{m.UserName} em {m.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}" +
                          $"{(m.Partial ? " (parcial)" : "")}{(m.ClaudeDesktopVersion is { } v ? $", Claude {v}" : "")}");
        Console.WriteLine($"  {m.Sessions.Count} sessão(ões), {m.Files.Count} arquivo(s), {FormatSize(m.Files.Sum(f => f.Size))}");
    }

    static void PrintPlan(RestorePlan plan, bool detailed)
    {
        Console.WriteLine($"\nDestino: Desktop = {plan.Target.DesktopRoot ?? "(não encontrado)"}");
        Console.WriteLine($"         Code    = {plan.Target.DotClaudeRoot}");

        if (plan.Remapper.Paths.Count > 0)
        {
            Console.WriteLine("\nTroca de caminhos:");
            foreach (var m in plan.Remapper.Paths) Console.WriteLine($"  {m.From}  ->  {m.To}");
        }
        if (plan.ProjectRenames.Count > 0)
        {
            Console.WriteLine("\nPastas de projeto renomeadas:");
            foreach (var (from, to) in plan.ProjectRenames) Console.WriteLine($"  {from}  ->  {to}");
        }

        Console.WriteLine($"\nArquivos: {plan.Count(RestoreAction.Create)} novo(s), {plan.Count(RestoreAction.Overwrite)} " +
                          $"a sobrescrever, {plan.Count(RestoreAction.Skip)} ignorado(s).");

        if (detailed)
        {
            foreach (var i in plan.Items.Where(i => i.Action != RestoreAction.Skip || plan.Items.Count < 50))
            {
                var tag = i.Action switch { RestoreAction.Create => "novo ", RestoreAction.Overwrite => "sobre", _ => "pula " };
                Console.WriteLine($"  [{tag}] {i.TargetPath}{(i.Reason is null ? "" : $"  ({i.Reason})")}");
            }
        }

        foreach (var w in plan.Warnings) Console.WriteLine($"\nAviso: {w}");
    }

    static bool Confirm(string question)
    {
        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("Entrada não interativa: use --yes para confirmar.");
            return false;
        }
        Console.Write($"{question} [s/N] ");
        var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
        return answer is "s" or "sim" or "y" or "yes";
    }

    static PathMapping ParseMapping(string value)
    {
        var eq = value.IndexOf('=');
        if (eq <= 0 || eq == value.Length - 1) throw new UsageException($"--map precisa ser ORIGEM=DESTINO: {value}");
        return new PathMapping(value[..eq].Trim().Trim('"'), value[(eq + 1)..].Trim().Trim('"'));
    }

    static ManifestScope ParseScope(string value)
    {
        var parts = value.Split('/', '\\');
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out _) || !Guid.TryParse(parts[1], out _))
            throw new UsageException($"--target-scope precisa ser <conta>/<org> (dois GUIDs): {value}");
        return new ManifestScope(parts[0], parts[1]);
    }

    public static string FormatSize(long bytes) => (bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.0} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.00} GB",
    }).Replace('.', ',');
}

/// <summary>One self-overwriting status line on an interactive console; silent when output is redirected.</summary>
internal sealed class ConsoleProgress(string verb) : IProgress<string>, IDisposable
{
    readonly bool _live = !Console.IsOutputRedirected;
    int _count;
    long _lastTick;

    public void Report(string value)
    {
        _count++;
        if (!_live || Environment.TickCount64 - _lastTick < 100) return;
        _lastTick = Environment.TickCount64;
        var width = Math.Max(20, Console.WindowWidth - 1);
        var line = $"{verb} [{_count}] {value}";
        if (line.Length > width) line = line[..(width - 1)] + "…";
        Console.Write("\r" + line.PadRight(width));
    }

    public void Done()
    {
        if (_live && _count > 0) Console.Write("\r" + new string(' ', Math.Max(20, Console.WindowWidth - 1)) + "\r");
        _count = 0;
    }

    public void Dispose() => Done();
}
