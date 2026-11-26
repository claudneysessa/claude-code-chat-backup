namespace ClaudeChatBackup.Infrastructure;

/// <summary>
/// Pasta dos backups: <c>backups\</c> ao lado do executável (modo portable). Se ela não puder ser
/// gravada, usa <c>%LOCALAPPDATA%\ClaudeChatBackup\backups</c>. A pasta só é criada quando um backup
/// é realmente gravado.
/// </summary>
public static class BackupLocation
{
    public static string Portable => Path.Combine(AppContext.BaseDirectory, "backups");

    public static string Fallback => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeChatBackup", "backups");

    public static string Default => CanWrite(AppContext.BaseDirectory) ? Portable : Fallback;

    /// <summary>Pastas onde procurar backups existentes (a portable e a de fallback).</summary>
    public static IReadOnlyList<string> SearchDirs => [Portable, Fallback];

    public static string NewBackupPath(DateTime? now = null) =>
        Path.Combine(Default, $"claude-{Environment.MachineName}-{now ?? DateTime.Now:yyyyMMdd-HHmmss}.zip");

    static bool CanWrite(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
