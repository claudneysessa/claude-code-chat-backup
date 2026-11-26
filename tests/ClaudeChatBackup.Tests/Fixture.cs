using System.Text.Json.Nodes;

namespace ClaudeChatBackup.Tests;

/// <summary>A throwaway "machine": a user profile with a Claude Desktop data root and a ~/.claude folder.</summary>
sealed class FakeMachine : IDisposable
{
    public const string Account = "11111111-1111-1111-1111-111111111111";
    public const string Org = "22222222-2222-2222-2222-222222222222";
    public const string CliId = "aaaaaaaa-0000-0000-0000-000000000001";
    public const string LocalId = "local_bbbbbbbb-0000-0000-0000-000000000001";
    public const string Cwd = @"X:\Proj\App";

    public string Home { get; }
    public string Desktop => Path.Combine(Home, "AppData", "Local", "Claude-Data", "claude_test");
    public string DotClaude => Path.Combine(Home, ".claude");
    public ClaudeLocations Locations => new(Desktop, DotClaude, Home);

    public FakeMachine(string name)
    {
        Home = Path.Combine(Path.GetTempPath(), "ccb-tests", $"{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Home);
    }

    public string SessionsDir(string account = Account, string org = Org) =>
        Path.Combine(Desktop, DataLayout.DesktopCodeSessions, account, org);

    public string ProjectDir(string cwd = Cwd) =>
        Path.Combine(DotClaude, DataLayout.Projects, DataLayout.EncodeProjectDir(cwd));

    /// <summary>One Desktop session with its transcript, a tool result, file history and the files that must stay out.</summary>
    public FakeMachine WithSession()
    {
        Write(Path.Combine(SessionsDir(), LocalId + ".json"), new JsonObject
        {
            ["sessionId"] = LocalId,
            ["cliSessionId"] = CliId,
            ["cwd"] = Cwd,
            ["originCwd"] = Cwd,
            ["gitAnchors"] = new JsonObject { ["gitRoot"] = Cwd, ["commonDir"] = Cwd + @"\.git" },
            ["writtenBranches"] = new JsonObject { [Cwd] = "master" },
            ["title"] = "Sessão de teste",
            ["createdAt"] = 1789392894026,
            ["lastActivityAt"] = 1792690354151,
            ["isArchived"] = false,
            ["note"] = @"texto que cita X:\Proj\App no meio",
        }.ToJsonString(new() { WriteIndented = true }));

        var lines = new[]
        {
            new JsonObject { ["type"] = "user", ["cwd"] = Cwd, ["sessionId"] = CliId,
                ["message"] = new JsonObject { ["role"] = "user", ["content"] = @"abra X:\Proj\App\a.cs por favor" } },
            new JsonObject { ["type"] = "assistant", ["cwd"] = Cwd, ["sessionId"] = CliId,
                ["message"] = new JsonObject { ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "tool_use", ["input"] = new JsonObject { ["file_path"] = Cwd + @"\a.cs" },
                }) } },
            new JsonObject { ["type"] = "system", ["note"] = "sem caminho, ação" },
        };
        Write(Path.Combine(ProjectDir(), CliId + ".jsonl"), string.Join("\n", lines.Select(l => l.ToJsonString())) + "\n");
        Write(Path.Combine(ProjectDir(), CliId, "tool-results", "r1.txt"), Cwd + " saída crua");
        Write(Path.Combine(DotClaude, DataLayout.FileHistory, CliId, "a.cs@v1"), "class A {}");
        Write(Path.Combine(DotClaude, "history.jsonl"), $$"""{"display":"oi","project":"{{Cwd.Replace("\\", "\\\\")}}"}""" + "\n");

        Write(Path.Combine(Desktop, "config.json"), """{"oauth:tokenCache":"SEGREDO"}""");
        Write(Path.Combine(Desktop, "ccd-session-presets.json"), """{"byAccount":{"ACCOUNT":{"x":1}}}""".Replace("ACCOUNT", Account));
        Write(Path.Combine(DotClaude, ".credentials.json"), """{"token":"SEGREDO"}""");
        Write(Path.Combine(Desktop, "Cache", "big.bin"), "cache");
        return this;
    }

    public static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Dispose()
    {
        try { Directory.Delete(Home, recursive: true); } catch (IOException) { }
    }
}
