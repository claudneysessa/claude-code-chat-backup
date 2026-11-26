namespace ClaudeChatBackup.Gui.ViewModels;

/// <summary>Lógica pura da tela "Sessões": busca e textos de cada linha.</summary>
internal static class SessionsViewModel
{
    /// <summary>Sessões cujo título, pasta ou id contêm o texto buscado (sem diferenciar maiúsculas).</summary>
    public static IReadOnlyList<SessionInfo> Filter(IEnumerable<SessionInfo> sessions, string? query)
    {
        var q = query?.Trim();
        if (string.IsNullOrEmpty(q)) return sessions.ToList();
        return sessions.Where(s =>
                Contains(s.Title, q) || Contains(s.Cwd, q) || Contains(s.Id, q) || Contains(s.CliSessionId, q))
            .ToList();
    }

    public static string Title(SessionInfo s) => string.IsNullOrWhiteSpace(s.Title) ? "(sem título)" : s.Title;

    public static string Kind(SessionInfo s) => s.Kind switch
    {
        SessionKind.Cli => "Terminal",
        _ when s.IsArchived => "Arquivada",
        _ => "Desktop",
    };

    static bool Contains(string? value, string q) =>
        value?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false;
}
