using System.IO.Compression;
using System.Text.Json.Nodes;

namespace ClaudeChatBackup.Tests;

public sealed class BackupRestoreTests : IDisposable
{
    readonly FakeMachine _source = new FakeMachine("src").WithSession();
    readonly FakeMachine _target = new("dst");
    readonly string _zip;

    public BackupRestoreTests() => _zip = Path.Combine(_source.Home, "backup.zip");

    public void Dispose()
    {
        _source.Dispose();
        _target.Dispose();
    }

    BackupResult Backup(BackupOptions? options = null) => BackupService.Run(_source.Locations, _zip, options);

    const string OtherLocal = "local_dddddddd-0000-0000-0000-000000000002";
    const string OtherCli = "eeeeeeee-0000-0000-0000-000000000002";

    /// <summary>Segunda sessão no mesmo projeto, para restaurar uma sem a outra.</summary>
    void AddSecondSession()
    {
        FakeMachine.Write(Path.Combine(_source.SessionsDir(), OtherLocal + ".json"),
            $$"""{"sessionId":"{{OtherLocal}}","cliSessionId":"{{OtherCli}}","cwd":"X:\\Proj\\App","title":"Outra"}""");
        FakeMachine.Write(Path.Combine(_source.ProjectDir(), OtherCli + ".jsonl"),
            """{"type":"user","cwd":"X:\\Proj\\App","message":{"content":"outra"}}""" + "\n");
        FakeMachine.Write(Path.Combine(_source.ProjectDir(), OtherCli, "tool-results", "r.txt"), "x");
        FakeMachine.Write(Path.Combine(_source.DotClaude, DataLayout.FileHistory, OtherCli, "b.cs@v1"), "class B {}");
    }

    [Fact]
    public void Restore_only_the_chosen_session_from_a_full_backup()
    {
        AddSecondSession();
        Backup();

        using var archive = ArchiveReader.Open(_zip);
        Assert.Equal(2, archive.Manifest.Sessions.Count);
        var options = new RestoreOptions { SessionFilter = [OtherLocal] };
        var plan = RestoreService.Plan(archive, _target.Locations, options);
        RestoreService.Execute(archive, plan, options);

        Assert.True(File.Exists(Path.Combine(_target.SessionsDir(), OtherLocal + ".json")));
        Assert.True(File.Exists(Path.Combine(_target.ProjectDir(), OtherCli + ".jsonl")));
        Assert.True(File.Exists(Path.Combine(_target.ProjectDir(), OtherCli, "tool-results", "r.txt")));
        Assert.True(File.Exists(Path.Combine(_target.DotClaude, DataLayout.FileHistory, OtherCli, "b.cs@v1")));

        Assert.False(File.Exists(Path.Combine(_target.SessionsDir(), FakeMachine.LocalId + ".json")));
        Assert.False(File.Exists(Path.Combine(_target.ProjectDir(), FakeMachine.CliId + ".jsonl")));
        Assert.False(Directory.Exists(Path.Combine(_target.DotClaude, DataLayout.FileHistory, FakeMachine.CliId)));
        Assert.False(File.Exists(Path.Combine(_target.DotClaude, "history.jsonl"))); // shared files stay out
        Assert.False(File.Exists(Path.Combine(_target.Desktop, "ccd-session-presets.json")));
    }

    [Fact]
    public void Session_filter_accepts_cli_id_and_rejects_unknown_ids()
    {
        AddSecondSession();
        Backup();
        using var archive = ArchiveReader.Open(_zip);

        var byCli = RestoreService.Plan(archive, _target.Locations, new RestoreOptions { SessionFilter = [OtherCli[..8]] });
        Assert.All(byCli.Items, i => Assert.True(
            i.File.Path.Contains(OtherCli) || i.File.Path.Contains(OtherLocal), i.File.Path));

        Assert.Throws<InvalidOperationException>(() =>
            RestoreService.Plan(archive, _target.Locations, new RestoreOptions { SessionFilter = ["ffffffff"] }));
    }

    [Theory]
    [InlineData("desktop/claude-code-sessions/a/o/local_x.json", true)]
    [InlineData("desktop/claude-code-sessions/a/o/local_y.json", false)]
    [InlineData("dotclaude/projects/P/c1.jsonl", true)]
    [InlineData("dotclaude/projects/P/c1/tool-results/r.txt", true)]
    [InlineData("dotclaude/projects/P/c2.jsonl", false)]
    [InlineData("dotclaude/file-history/c1/a@v1", true)]
    [InlineData("dotclaude/history.jsonl", false)]
    [InlineData("desktop/ccd-session-presets.json", false)]
    public void Archive_paths_are_matched_to_their_session(string path, bool expected) =>
        Assert.Equal(expected, DataLayout.BelongsToSession(path, "local_x", "c1"));

    [Fact]
    public void Catalog_links_desktop_session_to_transcript()
    {
        var s = Assert.Single(SessionCatalog.Load(_source.Locations).Sessions);
        Assert.Equal(FakeMachine.LocalId, s.Id);
        Assert.Equal(SessionKind.Desktop, s.Kind);
        Assert.Equal("Sessão de teste", s.Title);
        Assert.NotNull(s.TranscriptFile);
        Assert.True(s.TranscriptBytes > 0);
    }

    [Fact]
    public void Cli_only_transcript_is_listed_with_first_prompt()
    {
        var cwd = @"X:\Outro";
        FakeMachine.Write(Path.Combine(_source.ProjectDir(cwd), "cccccccc-0000-0000-0000-000000000001.jsonl"),
            """{"type":"user","cwd":"X:\\Outro","message":{"content":"primeira pergunta"}}""" + "\n");

        var cli = Assert.Single(SessionCatalog.Load(_source.Locations).Sessions, s => s.Kind == SessionKind.Cli);
        Assert.Equal("primeira pergunta", cli.Title);
        Assert.Equal(cwd, cli.Cwd);
    }

    [Fact]
    public void Cli_title_keeps_a_long_first_prompt()
    {
        var prompt = "Preciso levar esta branch do \"1234\" a \"RELEASE_V07001R001B006_OS1234\" "
                     + "e depois revisar os commits pendentes antes do merge";
        FakeMachine.Write(Path.Combine(_source.ProjectDir(@"X:\Outro"), "cccccccc-0000-0000-0000-000000000001.jsonl"),
            new JsonObject { ["type"] = "user", ["cwd"] = @"X:\Outro", ["message"] = new JsonObject { ["content"] = prompt } }
                .ToJsonString() + "\n");

        var cli = Assert.Single(SessionCatalog.Load(_source.Locations).Sessions, s => s.Kind == SessionKind.Cli);
        Assert.Equal(prompt, cli.Title);
    }

    [Fact]
    public void Old_backup_with_cut_title_gets_the_full_first_prompt_back()
    {
        var prompt = new string('x', 120) + " fim do pedido";
        var cli = "cccccccc-0000-0000-0000-000000000009";
        var transcript = Path.Combine(_source.Home, cli + ".jsonl");
        File.WriteAllText(transcript, new JsonObject
        {
            ["type"] = "user", ["message"] = new JsonObject { ["content"] = prompt },
        }.ToJsonString() + "\n");

        // Backups feitos antes da v0.6.0 guardavam só os primeiros 80 caracteres.
        var manifest = new BackupManifest { UserProfile = _source.Home, DotClaudeRoot = _source.DotClaude };
        manifest.Sessions.Add(new SessionInfo { Id = cli, CliSessionId = cli, Kind = SessionKind.Cli, Title = prompt[..80] + "…" });
        ArchiveWriter.Write(_zip, manifest, [new ArchiveItem(transcript, $"dotclaude/projects/X--P/{cli}.jsonl")]);

        using var archive = ArchiveReader.Open(_zip);
        archive.RecoverFullTitles();
        Assert.Equal(prompt, Assert.Single(archive.Manifest.Sessions).Title);
    }

    [Fact]
    public void Backup_includes_sessions_and_leaves_out_secrets_and_caches()
    {
        var result = Backup();
        var paths = result.Manifest.Files.Select(f => f.Path).ToList();

        Assert.Contains($"desktop/claude-code-sessions/{FakeMachine.Account}/{FakeMachine.Org}/{FakeMachine.LocalId}.json", paths);
        Assert.Contains($"dotclaude/projects/X--Proj-App/{FakeMachine.CliId}.jsonl", paths);
        Assert.Contains($"dotclaude/projects/X--Proj-App/{FakeMachine.CliId}/tool-results/r1.txt", paths);
        Assert.Contains($"dotclaude/file-history/{FakeMachine.CliId}/a.cs@v1", paths);
        Assert.Contains("desktop/ccd-session-presets.json", paths);
        Assert.DoesNotContain(paths, p => p.EndsWith("config.json") && !p.EndsWith("desktop_config.json"));
        Assert.DoesNotContain(paths, p => p.Contains(".credentials"));
        Assert.DoesNotContain(paths, p => p.Contains("Cache/"));

        using var archive = ArchiveReader.Open(_zip);
        Assert.Empty(archive.Verify());
        Assert.Equal(new ManifestScope(FakeMachine.Account, FakeMachine.Org), Assert.Single(archive.Manifest.Scopes));
        Assert.Equal(FakeMachine.Cwd, Assert.Single(archive.Manifest.Projects).Cwd);
    }

    [Fact]
    public void Backup_filtered_by_session_takes_only_that_session()
    {
        var result = Backup(new BackupOptions { SessionFilter = ["bbbbbbbb"] });
        Assert.True(result.Manifest.Partial);
        Assert.Single(result.Sessions);
        Assert.DoesNotContain(result.Manifest.Files, f => f.Path.EndsWith("history.jsonl") || f.Path.EndsWith("presets.json"));
        Assert.Contains(result.Manifest.Files, f => f.Path.EndsWith("a.cs@v1"));
    }

    [Fact]
    public void Backup_drops_a_half_written_last_line()
    {
        var transcript = Path.Combine(_source.ProjectDir(), FakeMachine.CliId + ".jsonl");
        var complete = File.ReadAllText(transcript);
        File.AppendAllText(transcript, """{"type":"assistant","mess""");

        Backup();
        using var archive = ArchiveReader.Open(_zip);
        var file = archive.Manifest.Files.Single(f => f.Path.EndsWith(".jsonl") && f.Path.Contains("projects"));
        using var reader = new StreamReader(archive.OpenFile(file));
        Assert.Equal(complete, reader.ReadToEnd());
    }

    [Fact]
    public void Restore_on_another_machine_remaps_paths_account_and_project_folder()
    {
        Directory.CreateDirectory(_target.SessionsDir("33333333-3333-3333-3333-333333333333", "44444444-4444-4444-4444-444444444444"));
        Backup();

        using var archive = ArchiveReader.Open(_zip);
        var options = new RestoreOptions { Mappings = [new PathMapping(@"X:\Proj", @"Y:\Work")] };
        var plan = RestoreService.Plan(archive, _target.Locations, options);
        Assert.Equal("Y--Work-App", plan.ProjectRenames["X--Proj-App"]);
        RestoreService.Execute(archive, plan, options);

        var sessionFile = Path.Combine(
            _target.SessionsDir("33333333-3333-3333-3333-333333333333", "44444444-4444-4444-4444-444444444444"),
            FakeMachine.LocalId + ".json");
        var session = JsonNode.Parse(File.ReadAllText(sessionFile))!;
        Assert.Equal(@"Y:\Work\App", (string?)session["cwd"]);
        Assert.Equal(@"Y:\Work\App\.git", (string?)session["gitAnchors"]!["commonDir"]);
        Assert.Equal("master", (string?)session["writtenBranches"]![@"Y:\Work\App"]);
        Assert.Equal(@"texto que cita X:\Proj\App no meio", (string?)session["note"]); // prose is not touched

        var presets = JsonNode.Parse(File.ReadAllText(Path.Combine(_target.Desktop, "ccd-session-presets.json")))!;
        Assert.NotNull(presets["byAccount"]!["33333333-3333-3333-3333-333333333333"]);

        var transcript = Path.Combine(_target.ProjectDir(@"Y:\Work\App"), FakeMachine.CliId + ".jsonl");
        var lines = File.ReadAllLines(transcript).Select(l => JsonNode.Parse(l)!).ToList();
        Assert.All(lines.Take(2), l => Assert.Equal(@"Y:\Work\App", (string?)l["cwd"]));
        Assert.Equal(@"abra X:\Proj\App\a.cs por favor", (string?)lines[0]["message"]!["content"]);
        Assert.Equal(@"Y:\Work\App\a.cs", (string?)lines[1]["message"]!["content"]![0]!["input"]!["file_path"]);
        var sourceTranscript = Path.Combine(_source.ProjectDir(), FakeMachine.CliId + ".jsonl");
        Assert.Equal(File.ReadAllLines(sourceTranscript)[2], File.ReadAllLines(transcript)[2]); // untouched line, same bytes

        // Tool results and file history are restored byte-for-byte.
        Assert.Equal(FakeMachine.Cwd + " saída crua",
            File.ReadAllText(Path.Combine(_target.ProjectDir(@"Y:\Work\App"), FakeMachine.CliId, "tool-results", "r1.txt")));
        Assert.True(File.Exists(Path.Combine(_target.DotClaude, DataLayout.FileHistory, FakeMachine.CliId, "a.cs@v1")));

        var history = File.ReadAllText(Path.Combine(_target.DotClaude, "history.jsonl"));
        Assert.Contains(@"Y:\\Work\\App", history);
    }

    [Fact]
    public void Restore_maps_user_profile_automatically()
    {
        var file = Path.Combine(_source.SessionsDir(), FakeMachine.LocalId + ".json");
        var node = JsonNode.Parse(File.ReadAllText(file))!;
        node["cwd"] = Path.Combine(_source.Home, "repo");
        File.WriteAllText(file, node.ToJsonString());
        Backup();

        using var archive = ArchiveReader.Open(_zip);
        RestoreService.Execute(archive, RestoreService.Plan(archive, _target.Locations));

        var restored = JsonNode.Parse(File.ReadAllText(Path.Combine(_target.SessionsDir(), FakeMachine.LocalId + ".json")))!;
        Assert.Equal(Path.Combine(_target.Home, "repo"), (string?)restored["cwd"]);
    }

    [Fact]
    public void Restore_newer_policy_keeps_newer_target_file_and_overwrite_saves_rollback()
    {
        Backup();
        var existing = Path.Combine(_target.SessionsDir(), FakeMachine.LocalId + ".json");
        FakeMachine.Write(existing, """{"sessionId":"local_bbbbbbbb-0000-0000-0000-000000000001","title":"versão do destino"}""");
        File.SetLastWriteTimeUtc(existing, DateTime.UtcNow.AddDays(1));

        using var archive = ArchiveReader.Open(_zip);
        var plan = RestoreService.Plan(archive, _target.Locations);
        Assert.Equal(RestoreAction.Skip, plan.Items.Single(i => i.TargetPath == existing).Action);

        var rollbackDir = Path.Combine(_target.Home, "rollback");
        var options = new RestoreOptions { Conflict = ConflictPolicy.Overwrite, RollbackDir = rollbackDir };
        var result = RestoreService.Execute(archive, RestoreService.Plan(archive, _target.Locations, options), options);

        Assert.Equal("Sessão de teste", (string?)JsonNode.Parse(File.ReadAllText(existing))!["title"]);
        Assert.NotNull(result.RollbackZip);
        using var rollback = ArchiveReader.Open(result.RollbackZip);
        var saved = Assert.Single(rollback.Manifest.Files);
        using var reader = new StreamReader(rollback.OpenFile(saved));
        Assert.Contains("versão do destino", reader.ReadToEnd());
    }

    [Fact]
    public void Identical_files_are_skipped()
    {
        Backup();
        using (var archive = ArchiveReader.Open(_zip))
            RestoreService.Execute(archive, RestoreService.Plan(archive, _target.Locations));

        using var again = ArchiveReader.Open(_zip);
        var plan = RestoreService.Plan(again, _target.Locations, new RestoreOptions { Conflict = ConflictPolicy.Overwrite });
        Assert.Contains(plan.Items, i => i.Action == RestoreAction.Skip && i.Reason == "idêntico");
    }

    [Fact]
    public void Corrupted_backup_restores_nothing()
    {
        Backup();
        using (var zip = ZipFile.Open(_zip, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry($"dotclaude/file-history/{FakeMachine.CliId}/a.cs@v1")!;
            entry.Delete();
            using var w = new StreamWriter(zip.CreateEntry(entry.FullName).Open());
            w.Write("adulterado");
        }

        using var archive = ArchiveReader.Open(_zip);
        Assert.NotEmpty(archive.Verify());
        var plan = RestoreService.Plan(archive, _target.Locations);
        Assert.Throws<InvalidDataException>(() => RestoreService.Execute(archive, plan));
        Assert.False(Directory.Exists(_target.DotClaude));
    }

    [Fact]
    public void Archive_with_path_traversal_is_rejected()
    {
        var manifest = new BackupManifest { DotClaudeRoot = _source.DotClaude, UserProfile = _source.Home };
        manifest.Files.Add(new ManifestFile("dotclaude/../../evil.txt", 1, "00", DateTime.UtcNow));
        using (var fs = File.Create(_zip))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        using (var s = zip.CreateEntry(DataLayout.ManifestEntry).Open())
            ManifestSerializer.Write(s, manifest);

        Assert.Throws<InvalidDataException>(() => ArchiveReader.Open(_zip));
    }
}
