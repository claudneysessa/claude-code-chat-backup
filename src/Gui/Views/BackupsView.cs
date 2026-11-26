using System.Diagnostics;
using ClaudeChatBackup.Gui.Theming;
using ClaudeChatBackup.Gui.ViewModels;

namespace ClaudeChatBackup.Gui.Views;

/// <summary>
/// Tela "Backups": os .zip da pasta de backups (e qualquer um aberto pelo botão), cada linha com
/// verificar e restaurar. Restaurar abre o <see cref="RestoreDialog"/>.
/// </summary>
internal sealed class BackupsView : Panel
{
    private readonly Table _table = new(
        [new Column("BACKUP"), new Column("ORIGEM", 160), new Column("QUANDO", 122, Mono: true), new Column("TAMANHO", 72, RightAlign: true, Mono: true)],
        actionWidth: 282);
    private readonly Label _empty = new()
    {
        Font = Theme.Body,
        ForeColor = Theme.TextMuted,
        AutoSize = true,
        Text = "Nenhum backup ainda. Faça um na tela Sessões ou abra um .zip.",
    };

    public event EventHandler<string>? StatusChanged;
    public event EventHandler? Restored;

    /// <summary>A lista mudou (atualizada ou backup excluído): a janela recalcula os contadores.</summary>
    public event EventHandler? Changed;

    public int BackupCount { get; private set; }

    public BackupsView()
    {
        BackColor = Theme.Shell;
        Dock = DockStyle.Fill;

        var toolbar = new Panel { Dock = DockStyle.Top, Height = Theme.HeightControl + 8, BackColor = Theme.Shell };
        var open = new FlatButton("Abrir backup…");
        var folder = new FlatButton("Abrir pasta de backups", FlatButton.Kind.Ghost);
        open.SizeToText();
        folder.SizeToText();
        open.Location = new Point(Theme.PadScreenX, 1);
        folder.Location = new Point(open.Right + Theme.GapSm, 1);
        open.Click += (_, _) => PickAndRestore();
        folder.Click += (_, _) => OpenFolder();
        var refresh = new FlatButton("Atualizar");
        refresh.SizeToText();
        refresh.Click += (_, _) => RefreshList(announce: true);
        toolbar.Controls.AddRange([open, folder, refresh]);
        toolbar.Resize += (_, _) =>
            refresh.Location = new Point(toolbar.ClientSize.Width - Theme.PadScreenX - refresh.Width, 1);

        var area = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Shell, Padding = new Padding(Theme.PadScreenX, 12, Theme.PadScreenX, 30) };
        _table.Dock = DockStyle.Fill;
        area.Controls.Add(_table);
        area.Controls.Add(_empty);
        area.Resize += (_, _) => _empty.Location = new Point(
            Math.Max(0, (area.ClientSize.Width - _empty.Width) / 2),
            Math.Max(area.Padding.Top, (area.ClientSize.Height - _empty.Height) / 2));

        Controls.Add(area);
        Controls.Add(toolbar);
        Controls.Add(ScreenHeader.Build(
            "Backups",
            "Cada .zip traz um manifesto com o SHA-256 de cada arquivo. Em outro computador, as pastas são trocadas na restauração."));
    }

    /// <summary>Relê a pasta de backups (botão Atualizar e F5).</summary>
    public void RefreshList(bool announce)
    {
        Reload();
        Changed?.Invoke(this, EventArgs.Empty);
        if (announce) ToastHost.Show($"Lista atualizada: {BackupCount} backup(s).");
    }

    public void Reload()
    {
        var entries = BackupsViewModel.List(BackupLocation.SearchDirs);
        BackupCount = entries.Count;

        _table.SetRows(entries.Select(entry =>
        {
            var row = new TableRow
            {
                Cells = [BackupsViewModel.DescribeContent(entry), entry.Origin, Formatting.When(entry.When), Formatting.Size(entry.ZipBytes)],
                Detail = BackupsViewModel.Detail(entry),
                DetailColor = entry.CanRestore ? null : Theme.DangerText,
                Tag = entry,
            };
            var verify = new FlatButton("Verificar") { Enabled = entry.CanRestore };
            var restore = new FlatButton("Restaurar", FlatButton.Kind.Primary) { Enabled = entry.CanRestore };
            var delete = new FlatButton("Excluir", FlatButton.Kind.DangerOutline);
            verify.Click += async (_, _) => await Verify(entry.Path);
            restore.Click += (_, _) => Restore(entry.Path);
            delete.Click += (_, _) => Delete(entry);
            row.AddActions(verify, restore, delete);
            return row;
        }));

        _table.Visible = entries.Count > 0;
        _empty.Visible = entries.Count == 0;
        StatusChanged?.Invoke(this, $"{entries.Count} backup(s) em {BackupLocation.Default}");
    }

    /// <summary>Abre o diálogo de restauração para um .zip (botão, arrastar ou argumento do exe).</summary>
    public void Restore(string zip)
    {
        if (FindForm() is not { } owner) return;
        try
        {
            using var dialog = new RestoreDialog(owner, zip);
            if (dialog.ShowModalDimmed() != DialogResult.OK) return;
            ToastHost.Show(dialog.ResultMessage ?? "Restauração concluída.");
            Restored?.Invoke(this, EventArgs.Empty);
            Reload();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            using var error = new ErrorDialog(owner, ex.Message);
            error.ShowModalDimmed();
        }
    }

    private void Delete(BackupEntry entry)
    {
        if (FindForm() is not { } owner) return;
        using (var dialog = new ConfirmDeleteBackupDialog(owner, entry))
            if (dialog.ShowModalDimmed() != DialogResult.OK) return;

        try
        {
            RecycleBin.Send(entry.Path);
            ToastHost.Show($"{Path.GetFileName(entry.Path)} foi para a Lixeira.");
            RefreshList(announce: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            using var error = new ErrorDialog(owner, ex.Message);
            error.ShowModalDimmed();
        }
    }

    private async Task Verify(string zip)
    {
        if (FindForm() is not { } owner) return;
        UseWaitCursor = true;
        StatusChanged?.Invoke(this, $"Verificando {Path.GetFileName(zip)}…");
        try
        {
            var problems = await Task.Run(() =>
            {
                using var archive = ArchiveReader.Open(zip);
                return (Files: archive.Manifest.Files.Count, Problems: archive.Verify());
            });
            if (problems.Problems.Count == 0)
            {
                ToastHost.Show($"Backup íntegro: {problems.Files} arquivo(s) conferidos.");
                StatusChanged?.Invoke(this, $"{Path.GetFileName(zip)}: OK");
            }
            else
            {
                using var error = new ErrorDialog(owner,
                    $"{problems.Problems.Count} problema(s):\n" + string.Join("\n", problems.Problems.Take(8)));
                error.ShowModalDimmed();
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            using var error = new ErrorDialog(owner, ex.Message);
            error.ShowModalDimmed();
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void PickAndRestore()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Escolha o backup",
            Filter = "Backup do Claude (*.zip)|*.zip",
            InitialDirectory = Directory.Exists(BackupLocation.Default) ? BackupLocation.Default : AppContext.BaseDirectory,
        };
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK) Restore(dialog.FileName);
    }

    private static void OpenFolder()
    {
        var dir = BackupLocation.Default;
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }
}
