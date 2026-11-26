using Microsoft.VisualBasic.FileIO;

namespace ClaudeChatBackup.Infrastructure;

/// <summary>Exclusão recuperável: o arquivo vai para a Lixeira do Windows em vez de sumir.</summary>
public static class RecycleBin
{
    public static void Send(string path)
    {
        if (!File.Exists(path)) return;
        FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }
}
