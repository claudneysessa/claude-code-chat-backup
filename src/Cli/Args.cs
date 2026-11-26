namespace ClaudeChatBackup.Cli;

/// <summary>Minimal argument parser: a command, positionals, --flags and repeatable --name value options.</summary>
sealed class Args
{
    static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase)
    {
        "--dry-run", "--verbose", "--force", "--yes", "--close-claude", "--no-rollback", "--keep-scope", "--help", "-h",
    };

    readonly Dictionary<string, List<string>> _options = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    public string? Command { get; private set; }
    public List<string> Positionals { get; } = [];

    public static Args Parse(string[] argv)
    {
        var a = new Args();
        for (var i = 0; i < argv.Length; i++)
        {
            var arg = argv[i];
            if (arg.StartsWith('-'))
            {
                var eq = arg.IndexOf('=');
                if (arg.StartsWith("--") && eq > 0 && !Flags.Contains(arg[..eq]))
                {
                    a.Add(arg[..eq], arg[(eq + 1)..]);
                }
                else if (Flags.Contains(arg))
                {
                    a._flags.Add(arg == "-h" ? "--help" : arg);
                }
                else
                {
                    if (i + 1 >= argv.Length) throw new UsageException($"Falta o valor de {arg}.");
                    a.Add(arg, argv[++i]);
                }
            }
            else if (a.Command is null) a.Command = arg.ToLowerInvariant();
            else a.Positionals.Add(arg);
        }
        return a;
    }

    void Add(string name, string value)
    {
        if (!_options.TryGetValue(name, out var list)) _options[name] = list = [];
        list.Add(value);
    }

    public bool Has(string flag) => _flags.Contains(flag);
    public string? Get(string name) => _options.TryGetValue(name, out var l) ? l[^1] : null;
    public IReadOnlyList<string> GetAll(string name) => _options.TryGetValue(name, out var l) ? l : [];

    public void EnsureOnly(params string[] allowed)
    {
        var all = new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase) { "--desktop-dir", "--claude-dir", "--help" };
        foreach (var name in _options.Keys.Concat(_flags))
            if (!all.Contains(name)) throw new UsageException($"Opção desconhecida para '{Command}': {name}");
    }
}

sealed class UsageException(string message) : Exception(message);
