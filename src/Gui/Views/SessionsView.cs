using ClaudeChatBackup.Gui.Theming;
using ClaudeChatBackup.Gui.ViewModels;

namespace ClaudeChatBackup.Gui.Views;

/// <summary>
/// Tela "Sessões": as conversas da aba Code e do Cowork deste computador, com busca, seleção e os
/// dois botões de backup (selecionadas ou tudo).
/// </summary>
internal sealed class SessionsView : Panel
{
    private readonly Table _table = new(
        [new Column("SESSÃO"), new Column("ATIVIDADE", 130, Mono: true), new Column("TIPO", 90), new Column("TAMANHO", 90, RightAlign: true, Mono: true)],
        checkable: true);
    private readonly SearchBox _search = new() { Placeholder = "Buscar por título, pasta ou id" };
    private readonly FlatButton _backupSelected = new("Backup das selecionadas");
    private readonly FlatButton _backupAll = new("Backup de tudo", FlatButton.Kind.Primary);
    private readonly FlatButton _refresh = new("Atualizar");
    private readonly Label _empty = new() { Font = Theme.Body, ForeColor = Theme.TextMuted, AutoSize = true };

    private IReadOnlyList<SessionInfo> _sessions = [];
    private ClaudeLocations _locations = ClaudeLocations.Discover();

    public event EventHandler<string>? StatusChanged;
    public event EventHandler? BackupCreated;
    public event EventHandler? Refreshed;

    public int SessionCount => _sessions.Count;

    public SessionsView()
    {
        BackColor = Theme.Shell;
        Dock = DockStyle.Fill;

        var toolbar = new Panel { Dock = DockStyle.Top, Height = Theme.HeightControl + 8, BackColor = Theme.Shell, Padding = new Padding(Theme.PadScreenX, 0, Theme.PadScreenX, 0) };
        _search.Width = 320;
        _search.Location = new Point(Theme.PadScreenX, 0);
        _search.Inner.TextChanged += (_, _) => Fill();
        _backupSelected.SizeToText();
        _backupAll.SizeToText();
        _backupSelected.Enabled = false;
        _backupSelected.Click += async (_, _) => await RunBackup(selectedOnly: true);
        _backupAll.Click += async (_, _) => await RunBackup(selectedOnly: false);
        _refresh.SizeToText();
        _refresh.Click += (_, _) => RefreshList(announce: true);
        toolbar.Controls.AddRange([_search, _refresh, _backupSelected, _backupAll]);
        toolbar.Resize += (_, _) => LayoutToolbar();

        var area = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Shell, Padding = new Padding(Theme.PadScreenX, 12, Theme.PadScreenX, 30) };
        _table.Dock = DockStyle.Fill;
        area.Controls.Add(_table);
        area.Controls.Add(_empty);
        area.Resize += (_, _) => _empty.Location = new Point(
            Math.Max(0, (area.ClientSize.Width - _empty.Width) / 2),
            Math.Max(area.Padding.Top, (area.ClientSize.Height - _empty.Height) / 2));

        // O Dock=Fill entra antes: o layout consome as bordas do fim da coleção para o começo.
        Controls.Add(area);
        Controls.Add(toolbar);
        Controls.Add(ScreenHeader.Build(
            "Sessões",
            "Conversas da aba Code e do Cowork guardadas neste computador. Clique para selecionar."));
    }

    /// <summary>Relê as sessões do disco (botão Atualizar e F5), mantendo busca e seleção.</summary>
    public void RefreshList(bool announce)
    {
        Reload();
        Refreshed?.Invoke(this, EventArgs.Empty);
        if (announce) ToastHost.Show($"Lista atualizada: {_sessions.Count} sessão(ões).");
    }

    public void Reload()
    {
        _locations = ClaudeLocations.Discover();
        try
        {
            _sessions = SessionCatalog.Load(_locations).Sessions;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _sessions = [];
            StatusChanged?.Invoke(this, ex.Message);
        }
        Fill();
    }

    private void Fill()
    {
        var visible = SessionsViewModel.Filter(_sessions, _search.Inner.Text);
        var selected = _table.Rows.Where(r => r.Checked).Select(r => ((SessionInfo)r.Tag!).Id).ToHashSet();

        _table.SetRows(visible.Select(s =>
        {
            var row = new TableRow
            {
                Cells = [SessionsViewModel.Title(s), Formatting.When(s.LastActivityAt), SessionsViewModel.Kind(s), Formatting.Size(s.TranscriptBytes)],
                Detail = s.Cwd ?? "pasta desconhecida",
                Tag = s,
                Checked = selected.Contains(s.Id),
            };
            row.CheckedChanged += (_, _) => UpdateSelection();
            return row;
        }));

        _empty.Text = _sessions.Count == 0
            ? "Nenhuma sessão encontrada neste computador."
            : "Nenhuma sessão corresponde à busca.";
        _empty.Visible = visible.Count == 0;
        _table.Visible = visible.Count > 0;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var count = _table.Rows.Count(r => r.Checked);
        _backupSelected.Text = count == 0 ? "Backup das selecionadas" : $"Backup das selecionadas ({count})";
        _backupSelected.SizeToText();
        _backupSelected.Enabled = count > 0;
        LayoutToolbar();
        StatusChanged?.Invoke(this, $"{_sessions.Count} sessão(ões) · {count} selecionada(s)");
    }

    /// <summary>Botões alinhados à direita: Atualizar, Backup das selecionadas, Backup de tudo.</summary>
    private void LayoutToolbar()
    {
        if (_backupAll.Parent is not { } toolbar) return;
        _backupAll.Location = new Point(toolbar.ClientSize.Width - Theme.PadScreenX - _backupAll.Width, 1);
        _backupSelected.Location = new Point(_backupAll.Left - Theme.GapSm - _backupSelected.Width, 1);
        _refresh.Location = new Point(_backupSelected.Left - Theme.GapSm - _refresh.Width, 1);
    }

    private async Task RunBackup(bool selectedOnly)
    {
        var ids = selectedOnly ? _table.Rows.Where(r => r.Checked).Select(r => ((SessionInfo)r.Tag!).Id).ToList() : [];
        if (selectedOnly && ids.Count == 0) return;

        var output = BackupLocation.NewBackupPath();
        var count = 0;
        var progress = new Progress<string>(p =>
        {
            if (++count % 25 == 0) StatusChanged?.Invoke(this, $"Copiando {count} arquivo(s)… {Path.GetFileName(p)}");
        });

        SetBusy(true);
        try
        {
            var loc = _locations;
            var result = await Task.Run(() =>
                BackupService.Run(loc, output, new BackupOptions { SessionFilter = ids }, progress));
            var size = Formatting.Size(new FileInfo(result.ZipPath).Length);
            ToastHost.Show($"Backup criado: {result.Sessions.Count} sessão(ões), {size}.");
            StatusChanged?.Invoke(this, $"Backup salvo em {result.ZipPath}");
            BackupCreated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            if (FindForm() is { } owner)
            {
                using var dialog = new ErrorDialog(owner, ex.Message);
                dialog.ShowModalDimmed();
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _backupAll.Enabled = !busy;
        _backupSelected.Enabled = !busy && _table.Rows.Any(r => r.Checked);
        _table.Enabled = !busy;
        UseWaitCursor = busy;
    }
}
