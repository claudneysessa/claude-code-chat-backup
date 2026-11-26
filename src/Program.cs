using System.Runtime.InteropServices;
using ClaudeChatBackup.Cli;
using ClaudeChatBackup.Gui;

namespace ClaudeChatBackup;

/// <summary>
/// Entrada híbrida: sem argumentos (duplo clique), com o verbo <c>gui</c> ou com um backup
/// arrastado para o exe, abre a interface visual; com argumentos, roda como ferramenta de linha de
/// comando (anexa-se ao console do terminal pai para não abrir janela própria).
/// </summary>
internal static class Program
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    [STAThread]
    private static int Main(string[] args)
    {
        var droppedZip = args.Length == 1 && args[0].EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(args[0])
            ? Path.GetFullPath(args[0])
            : null;

        if (args.Length == 0 || droppedZip is not null || args[0].Equals("gui", StringComparison.OrdinalIgnoreCase))
        {
            FreeConsole();
            return GuiApp.Run(droppedZip);
        }

        AttachToParentConsole();
        return CliApp.Run(args);
    }

    private static void AttachToParentConsole()
    {
        if (!AttachConsole(AttachParentProcess) && !AllocConsole())
        {
            return;
        }

        try
        {
            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(stdout);
            var stderr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
            Console.SetError(stderr);
        }
        catch (IOException)
        {
            // sem console disponível
        }
    }
}
