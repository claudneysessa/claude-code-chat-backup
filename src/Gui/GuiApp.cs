namespace ClaudeChatBackup.Gui;

/// <summary>Inicializa e exibe a interface visual da ferramenta.</summary>
public static class GuiApp
{
    public static int Run(string? droppedZip = null)
    {
        ApplicationConfiguration.Initialize();
        System.Windows.Forms.Application.Run(new MainForm(droppedZip));
        return 0;
    }
}
