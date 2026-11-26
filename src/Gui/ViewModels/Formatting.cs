using System.Globalization;

namespace ClaudeChatBackup.Gui.ViewModels;

/// <summary>Data e tamanho legíveis em pt-BR, iguais em todas as telas.</summary>
internal static class Formatting
{
    /// <summary>"500 B", "2,0 KB", "3,0 MB", "1,25 GB".</summary>
    public static string Size(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024d:0.0} KB".Replace('.', ',');
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024d):0.0} MB".Replace('.', ',');
        return $"{bytes / (1024d * 1024d * 1024d):0.00} GB".Replace('.', ',');
    }

    /// <summary>"18/08/2026 21:04" no fuso local.</summary>
    public static string When(DateTimeOffset? when) =>
        when?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) ?? "—";
}
