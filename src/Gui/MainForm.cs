using System.Drawing.Drawing2D;
using ClaudeChatBackup.Gui.Theming;
using ClaudeChatBackup.Gui.Views;

namespace ClaudeChatBackup.Gui;

/// <summary>
/// Janela principal: barra lateral de navegação, barra de status e área de conteúdo com troca de
/// telas (Sessões, Backups) por visibilidade de painéis. Aceita um .zip arrastado para a janela.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly SessionsView _sessions = new();
    private readonly BackupsView _backups = new();

    private readonly Label _statusLeft = new() { AutoSize = true };
    private readonly Label _statusRight = new() { AutoSize = true };
    private readonly NavItem _navSessions = new("Sessões", NavIcon.Sessions);
    private readonly NavItem _navBackups = new("Backups", NavIcon.Backups);
    private readonly GuardCard _guard = new();
    private readonly System.Windows.Forms.Timer _guardTimer = new() { Interval = 3000 };
    private readonly ClaudeLocations _locations = ClaudeLocations.Discover();

    private Control _activeView;

    public MainForm(string? droppedZip = null)
    {
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? "ClaudeChatBackup");
        Text = "Claude Chat Backup";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1180, 760);
        MinimumSize = new Size(980, 620);
        AllowDrop = true;
        DarkWindow.Apply(this);

        BuildShell();
        ToastHost.Attach(this);
        _activeView = _sessions;

        _sessions.StatusChanged += (_, text) => { if (_activeView == _sessions) _statusRight.Text = text; };
        _backups.StatusChanged += (_, text) => { if (_activeView == _backups) _statusRight.Text = text; };
        _sessions.BackupCreated += (_, _) => RefreshCounters();
        _sessions.Refreshed += (_, _) => RefreshCounters();
        _backups.Changed += (_, _) => RefreshCounters();

        // F5 relê a tela ativa, como o botão Atualizar.
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.F5) return;
            if (_activeView == _sessions) _sessions.RefreshList(announce: true);
            else _backups.RefreshList(announce: true);
            e.Handled = true;
        };
        _backups.Restored += (_, _) => { _sessions.Reload(); RefreshCounters(); };

        DragEnter += (_, e) => e.Effect = DroppedZip(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        DragDrop += (_, e) =>
        {
            if (DroppedZip(e) is not { } zip) return;
            Navigate(_backups);
            _backups.Restore(zip);
        };

        _guardTimer.Tick += (_, _) => RefreshGuard();
        _guardTimer.Start();

        Navigate(_sessions);
        RefreshGuard();
        RefreshCounters();

        Shown += (_, _) =>
        {
            ActiveControl = null;
            if (droppedZip is null) return;
            Navigate(_backups);
            _backups.Restore(droppedZip);
        };
        FormClosed += (_, _) => _guardTimer.Dispose();
    }

    private static string? DroppedZip(DragEventArgs e) =>
        e.Data?.GetData(DataFormats.FileDrop) is string[] files
            ? files.FirstOrDefault(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(f))
            : null;

    private void BuildShell()
    {
        BuildStatusBar();
        BuildRail();

        var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Shell };
        foreach (var view in new Control[] { _sessions, _backups })
        {
            view.Visible = false;
            content.Controls.Add(view);
        }

        Controls.Add(content);
        Controls.Add(_rail);
        Controls.Add(_statusBar);
    }

    // ---- Barra de status -----------------------------------------------------

    private Panel _statusBar = null!;

    private void BuildStatusBar()
    {
        _statusBar = new Panel { Dock = DockStyle.Bottom, Height = Theme.StatusBarH, BackColor = Theme.Rail };
        _statusBar.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.BorderSoft, 1f);
            e.Graphics.DrawLine(pen, 0, 0, Width, 0);
        };

        _statusLeft.Text = $"v{typeof(MainForm).Assembly.GetName().Version?.ToString(3)}";
        _statusLeft.Font = Theme.Mono;
        _statusLeft.ForeColor = Theme.TextFaint;
        _statusLeft.Location = new Point(20, (Theme.StatusBarH - _statusLeft.PreferredHeight) / 2);

        _statusRight.Font = Theme.Body;
        _statusRight.ForeColor = Theme.TextMuted;
        _statusRight.AutoSize = false;
        _statusRight.AutoEllipsis = true;
        _statusRight.TextAlign = ContentAlignment.MiddleRight;
        _statusRight.Padding = new Padding(0, 0, 20, 0);

        _statusBar.Resize += (_, _) =>
        {
            _statusLeft.Top = (Theme.StatusBarH - _statusLeft.PreferredHeight) / 2;
            var left = _statusLeft.Right + 20;
            _statusRight.SetBounds(left, 0, Math.Max(0, _statusBar.ClientSize.Width - left), Theme.StatusBarH);
        };

        _statusBar.Controls.Add(_statusLeft);
        _statusBar.Controls.Add(_statusRight);
    }

    // ---- Barra lateral -------------------------------------------------------

    private Panel _rail = null!;

    private void BuildRail()
    {
        _rail = new Panel { Dock = DockStyle.Left, Width = Theme.RailWidth, BackColor = Theme.Rail };
        _rail.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border, 1f);
            e.Graphics.DrawLine(pen, _rail.Width - 1, 0, _rail.Width - 1, _rail.Height);
        };

        // Marca: ladrilho 30x30 raio 8 com gradiente + balão de conversa.
        var brand = new Panel { BackColor = Theme.Rail, Height = 44 };
        brand.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var tile = new Rectangle(6, 6, 30, 30);
            using (var path = Theme.RoundRect(tile, 8))
            using (var brush = new LinearGradientBrush(tile, Theme.Accent, Theme.AccentDeep, 45f))
                g.FillPath(brush, path);
            using (var pen = new Pen(Theme.AccentText, 1.8f) { LineJoin = LineJoin.Round })
            using (var bubble = Theme.RoundRect(new Rectangle(tile.X + 7, tile.Y + 8, 16, 11), 3))
            {
                g.DrawPath(pen, bubble);
                g.DrawLines(pen, [new Point(tile.X + 11, tile.Y + 19), new Point(tile.X + 11, tile.Y + 23), new Point(tile.X + 15, tile.Y + 19)]);
            }

            TextRenderer.DrawText(g, "Claude", Theme.H2, new Point(tile.Right + 6, 4), Theme.Text);
            TextRenderer.DrawText(g, "CHAT BACKUP", Theme.Caps, new Point(tile.Right + 8, 24), Theme.TextFaint);
        };

        var navLabel = new Label
        {
            Text = "NAVEGAÇÃO",
            Font = Theme.Caps,
            ForeColor = Theme.TextFaint,
            AutoSize = true,
            Margin = new Padding(8, 0, 0, 8),
        };

        _navSessions.Click += (_, _) => Navigate(_sessions);
        _navBackups.Click += (_, _) => Navigate(_backups);

        var nav = new VStack { BackColor = Theme.Rail, AutoHeight = true };
        nav.Controls.Add(navLabel);
        foreach (var item in new[] { _navSessions, _navBackups })
        {
            item.Height = Theme.HeightNav;
            item.Margin = new Padding(0, 3, 0, 0);
            nav.Controls.Add(item);
        }

        var paths = BuildPaths();

        var footer = new VStack { Dock = DockStyle.Bottom, BackColor = Theme.Rail, Padding = new Padding(14, 0, 14, 20) };
        footer.Controls.Add(_guard);
        footer.Controls.Add(paths);
        footer.Height = _guard.Height + paths.Height + paths.Margin.Top + footer.Padding.Vertical;

        var top = new VStack { Dock = DockStyle.Top, BackColor = Theme.Rail, Padding = new Padding(14, 20, 14, 0), AutoHeight = true };
        top.Controls.Add(brand);
        nav.Margin = new Padding(0, 26, 0, 0);
        top.Controls.Add(nav);

        _rail.Controls.Add(footer);
        _rail.Controls.Add(top);
    }

    /// <summary>
    /// Rodapé com as duas pastas do Claude em forma curta (perfil vira <c>~</c>, a do Desktop mostra só
    /// a pasta final); o caminho completo fica no tooltip e o clique abre a pasta no Explorer.
    /// </summary>
    private Panel BuildPaths()
    {
        var panel = new Panel { Height = 78, BackColor = Theme.Rail, Margin = new Padding(0, 14, 0, 0) };
        panel.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.BorderSoft, 1f);
            e.Graphics.DrawLine(pen, 0, 0, panel.Width, 0);
        };
        panel.Controls.Add(new Label
        {
            Text = "PASTAS DO CLAUDE", Font = Theme.Caps, ForeColor = Theme.TextFaint, AutoSize = true,
            Location = new Point(6, 14), BackColor = Theme.Rail,
        });

        var tips = new ToolTip();
        AddPath(panel, tips, "Desktop", _locations.DesktopRoot, 36);
        AddPath(panel, tips, "Code", _locations.DotClaudeRoot, 56);
        return panel;
    }

    private void AddPath(Panel panel, ToolTip tips, string name, string? path, int top)
    {
        panel.Controls.Add(new Label
        {
            Text = name, Font = Theme.Small, ForeColor = Theme.TextMuted, AutoSize = false,
            Location = new Point(6, top), Size = new Size(58, 18), BackColor = Theme.Rail,
        });
        var value = new Label
        {
            Text = ShortPath(path),
            Font = Theme.MonoSmall,
            ForeColor = Theme.RailPath,
            AutoSize = false,
            AutoEllipsis = true,
            Location = new Point(64, top + 1),
            Size = new Size(Theme.RailWidth - 28 - 64, 18),
            BackColor = Theme.Rail,
            Cursor = path is null ? Cursors.Default : Cursors.Hand,
        };
        tips.SetToolTip(value, path ?? "não encontrado");
        if (path is not null)
            value.Click += (_, _) => System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        panel.Controls.Add(value);
    }

    private string ShortPath(string? path)
    {
        if (path is null) return "não encontrado";
        if (path.Contains(@"\Claude-Data\", StringComparison.OrdinalIgnoreCase)) return Path.GetFileName(path);
        return path.StartsWith(_locations.UserProfile, StringComparison.OrdinalIgnoreCase)
            ? "~" + path[_locations.UserProfile.Length..]
            : path;
    }

    private void RefreshGuard() => _guard.UpdateState(!ClaudeDesktopProcess.IsRunning());

    private void RefreshCounters()
    {
        _backups.Reload();
        _navSessions.Counter = _sessions.SessionCount.ToString();
        _navBackups.Counter = _backups.BackupCount.ToString();
        _navSessions.Invalidate();
        _navBackups.Invalidate();
    }

    // ---- Navegação -----------------------------------------------------------

    private void Navigate(Control view)
    {
        _activeView.Visible = false;
        _activeView = view;
        _activeView.Visible = true;
        _activeView.BringToFront();

        _navSessions.Active = view == _sessions;
        _navBackups.Active = view == _backups;

        if (view == _sessions) _sessions.Reload();
        else if (view == _backups) _backups.Reload();
    }
}
