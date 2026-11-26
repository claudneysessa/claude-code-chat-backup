using ClaudeChatBackup.Gui.Theming;
using ClaudeChatBackup.Gui.ViewModels;

namespace ClaudeChatBackup.Gui.Views;

/// <summary>
/// Restauração de um backup em duas colunas: à esquerda as sessões do pacote (busca, caixa no
/// cabeçalho para marcar todas, Shift+clique para intervalos); à direita a troca de pastas, o que
/// fazer com arquivos que já existem e o plano. Ocupa quase a tela toda e pode ser redimensionado.
/// O Claude Desktop precisa estar fechado na execução; o diálogo oferece fechá-lo.
/// </summary>
internal sealed class RestoreDialog : DarkDialog
{
    private const int OptionsWidth = 430;

    private readonly ArchiveReader _archive;
    private readonly Dictionary<string, bool> _marked;
    private readonly VStack _maps = new() { BackColor = Theme.Card, AutoHeight = true };
    private readonly Table _sessions = new(
        [new Column("SESSÃO"), new Column("ATIVIDADE", 130, Mono: true), new Column("TAMANHO", 80, RightAlign: true, Mono: true)],
        checkable: true);
    private readonly SearchBox _search = new() { Placeholder = "Buscar por título ou pasta" };
    private readonly Label _sessionCount = new() { Font = Theme.BodyBold, ForeColor = Theme.Text, AutoSize = true };
    private readonly Label _summary = new() { Font = Theme.Small, ForeColor = Theme.TextMuted, AutoSize = false };
    private readonly FlatButton _cancel = new("Cancelar");
    private readonly FlatButton _analyze = new("Analisar");
    private readonly FlatButton _restore = new("Restaurar", FlatButton.Kind.Primary) { Enabled = false };
    private readonly (FlatButton Button, ConflictPolicy Policy)[] _policies;

    private ConflictPolicy _conflict = ConflictPolicy.Newer;
    private RestorePlan? _plan;
    private RestoreOptions? _planOptions;

    /// <summary>Frase para o aviso de sucesso, preenchida quando a restauração termina.</summary>
    public string? ResultMessage { get; private set; }

    public RestoreDialog(Form owner, string zip) : base(owner, "Restaurar backup", 1100, 760)
    {
        var screen = Screen.FromControl(owner).WorkingArea;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimumSize = new Size(1000, 640);
        ClientSize = new Size(Math.Min(1320, screen.Width - 80), Math.Min(900, screen.Height - 80));

        _archive = ArchiveReader.Open(zip);
        _archive.RecoverFullTitles();
        var m = _archive.Manifest;
        // Começa tudo desmarcado: restaurar é escolha explícita, nunca o padrão.
        _marked = m.Sessions.ToDictionary(s => s.Id, _ => false, StringComparer.OrdinalIgnoreCase);
        SetHeader("Restaurar backup",
            $@"{m.MachineName}\{m.UserName} · {Formatting.When(m.CreatedAt)} · {m.Sessions.Count} sessão(ões) · "
            + Formatting.Size(m.Files.Sum(f => f.Size)) + $" · {Path.GetFileName(zip)}");

        _policies =
        [
            (new FlatButton("Manter o mais recente (recomendado)"), ConflictPolicy.Newer),
            (new FlatButton("Substituir pelo que está no backup"), ConflictPolicy.Overwrite),
            (new FlatButton("Manter o que já existe aqui"), ConflictPolicy.Skip),
        ];

        Body.Controls.Add(BuildSessionsColumn());
        Body.Controls.Add(BuildOptionsColumn());

        FillSessions();
        foreach (var from in BackupsViewModel.SuggestMappings(m, Directory.Exists)) AddMap(from, "");

        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _analyze.Click += async (_, _) => await Analyze();
        _restore.Click += async (_, _) => await Restore();
        SetFooterButtons(_cancel, _analyze, _restore);

        Shown += (_, _) => ActiveControl = _search.Inner;
        FormClosed += (_, _) => _archive.Dispose();
    }

    // ---- Coluna das sessões ----------------------------------------------------

    private Control BuildSessionsColumn()
    {
        var column = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card };

        var stack = new VStack { Dock = DockStyle.Top, BackColor = Theme.Card, AutoHeight = true };
        stack.Controls.Add(Caps("SESSÕES DO BACKUP"));
        stack.Controls.Add(Hint("Clique numa linha para marcar · Shift+clique marca um intervalo · "
                                + "a caixa do cabeçalho marca ou desmarca todas.", 20));

        var bar = new Panel { Height = Theme.HeightControl + 2, BackColor = Theme.Card };
        var all = new FlatButton("Marcar todas");
        var none = new FlatButton("Desmarcar todas");
        all.SizeToText();
        none.SizeToText();
        _search.Width = 300;
        _search.Location = new Point(0, 0);
        all.Location = new Point(_search.Right + Theme.GapSm, 1);
        none.Location = new Point(all.Right + Theme.GapXs, 1);
        all.Click += (_, _) => _sessions.SetAllChecked(true);
        none.Click += (_, _) => _sessions.SetAllChecked(false);
        _search.Inner.TextChanged += (_, _) => FillSessions();
        bar.Controls.AddRange([_search, all, none, _sessionCount]);
        bar.Resize += (_, _) => PlaceCount(bar);
        stack.Controls.Add(bar);

        _sessions.Dock = DockStyle.Fill;
        var spacer = new Panel { Dock = DockStyle.Top, Height = Theme.GapMd, BackColor = Theme.Card };

        column.Controls.Add(_sessions);
        column.Controls.Add(spacer);
        column.Controls.Add(stack);
        return column;
    }

    private void FillSessions()
    {
        var visible = SessionsViewModel.Filter(_archive.Manifest.Sessions, _search.Inner.Text)
            .OrderByDescending(s => s.LastActivityAt ?? DateTimeOffset.MinValue);

        _sessions.SetRows(visible.Select(s =>
        {
            var row = new TableRow
            {
                Cells = [SessionsViewModel.Title(s), Formatting.When(s.LastActivityAt),
                         s.TranscriptBytes == 0 ? "vazia" : Formatting.Size(s.TranscriptBytes)],
                Detail = s.Cwd ?? "pasta desconhecida",
                Tag = s,
                Checked = _marked.GetValueOrDefault(s.Id),
            };
            row.CheckedChanged += (_, _) =>
            {
                _marked[s.Id] = row.Checked;
                UpdateSessionCount();
                ResetPlan();
            };
            return row;
        }));
        UpdateSessionCount();
    }

    private void UpdateSessionCount()
    {
        _sessionCount.Text = $"{_marked.Count(kv => kv.Value)} de {_marked.Count} marcada(s)";
        if (_sessionCount.Parent is { } bar) PlaceCount(bar);
    }

    private void PlaceCount(Control bar) => _sessionCount.Location = new Point(
        bar.ClientSize.Width - _sessionCount.Width, (bar.Height - _sessionCount.Height) / 2);

    // ---- Coluna das opções e do plano ------------------------------------------

    private Control BuildOptionsColumn()
    {
        var column = new Panel
        {
            Dock = DockStyle.Right,
            Width = OptionsWidth,
            BackColor = Theme.Card,
            Padding = new Padding(26, 0, 0, 0),
        };
        column.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border, 1f);
            e.Graphics.DrawLine(pen, 12, 0, 12, column.Height);
        };

        var options = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, AutoScroll = true };
        DarkWindow.DarkScrollBars(options);
        var stack = new VStack { Dock = DockStyle.Top, BackColor = Theme.Card, AutoHeight = true, Padding = new Padding(0, 0, 8, 0) };
        // Escolha fixa primeiro; a lista de trocas (tamanho variável) vem depois e pode rolar.
        stack.Controls.Add(Caps("SE O ARQUIVO JÁ EXISTIR"));
        foreach (var (button, policy) in _policies)
        {
            button.Margin = new Padding(0, 0, 0, Theme.GapXs);
            button.Click += (_, _) =>
            {
                _conflict = policy;
                PaintPolicies();
                ResetPlan();
            };
            stack.Controls.Add(button);
        }
        PaintPolicies();
        stack.Controls.Add(Caps("TROCAR PASTAS", top: 20));
        stack.Controls.Add(Hint("Pastas de projeto do backup que não existem aqui. Informe onde elas estão neste "
                                + "computador; em branco, o caminho fica igual.", 36));
        stack.Controls.Add(_maps);
        stack.Controls.Add(AddMapButton());
        options.Controls.Add(stack);

        var plan = new Panel { Dock = DockStyle.Bottom, Height = 170, BackColor = Theme.Card, Padding = new Padding(0, 14, 0, 0) };
        plan.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border, 1f);
            e.Graphics.DrawLine(pen, 0, 4, plan.Width, 4);
        };
        _summary.Dock = DockStyle.Fill;
        _summary.Text = "Marque as sessões que quer trazer e clique em Analisar. Nada é gravado até você clicar em Restaurar.";
        var planCaps = Caps("PLANO");
        planCaps.Dock = DockStyle.Top;
        plan.Controls.Add(_summary);
        plan.Controls.Add(planCaps);

        column.Controls.Add(options);
        column.Controls.Add(plan);
        return column;
    }

    private static Label Caps(string text, int top = 0) => new()
    {
        Text = text, Font = Theme.Caps, ForeColor = Theme.TextFaint, AutoSize = false, Height = 18,
        Margin = new Padding(0, top, 0, 6),
    };

    private static Label Hint(string text, int height) => new()
    {
        Text = text, Font = Theme.Small, ForeColor = Theme.TextFaint, AutoSize = false, Height = height,
        Margin = new Padding(0, 0, 0, 8),
    };

    private Control AddMapButton()
    {
        var holder = new Panel { Height = 34, BackColor = Theme.Card, Margin = new Padding(0, 2, 0, 0) };
        var add = new FlatButton("+ Adicionar troca de pasta", FlatButton.Kind.Ghost) { Height = 32 };
        add.SizeToText();
        add.Click += (_, _) => AddMap("", "");
        holder.Controls.Add(add);
        return holder;
    }

    private void PaintPolicies()
    {
        foreach (var (button, policy) in _policies)
        {
            button.Variant = policy == _conflict ? FlatButton.Kind.Primary : FlatButton.Kind.Secondary;
            button.Invalidate();
        }
    }

    private void AddMap(string from, string to)
    {
        var row = new MapRow(from, to);
        row.Changed += (_, _) => ResetPlan();
        row.RemoveRequested += (_, _) =>
        {
            _maps.Controls.Remove(row);
            row.Dispose();
            ResetPlan();
        };
        _maps.Controls.Add(row);
    }

    // ---- Plano e execução ------------------------------------------------------

    private void ResetPlan()
    {
        if (_plan is null) return;
        _plan = null;
        _restore.Enabled = false;
        _summary.ForeColor = Theme.TextMuted;
        _summary.Text = "As opções mudaram. Clique em Analisar de novo.";
    }

    private RestoreOptions? BuildOptions()
    {
        var mappings = new List<PathMapping>();
        foreach (var row in _maps.Controls.OfType<MapRow>())
        {
            var (from, to) = (row.From, row.To);
            if (from.Length == 0 || to.Length == 0) continue;
            if (!Path.IsPathRooted(from) || !Path.IsPathRooted(to))
            {
                ShowSummaryError($"Use caminhos completos nas trocas de pasta (ex.: D:\\Projetos): «{from}» → «{to}».");
                return null;
            }
            mappings.Add(new PathMapping(from, to));
        }

        var marked = _marked.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
        if (marked.Count == 0)
        {
            ShowSummaryError("Marque ao menos uma sessão para restaurar.");
            return null;
        }

        return new RestoreOptions
        {
            // Todas marcadas = restauração completa, com os arquivos compartilhados.
            SessionFilter = marked.Count == _marked.Count ? [] : marked,
            Mappings = mappings,
            Conflict = _conflict,
            RollbackDir = BackupLocation.Default,
        };
    }

    private async Task Analyze()
    {
        if (BuildOptions() is not { } options) return;
        SetBusy(true);
        _summary.ForeColor = Theme.TextMuted;
        _summary.Text = "Analisando…";
        try
        {
            var plan = await Task.Run(() => RestoreService.Plan(_archive, ClaudeLocations.Discover(), options));
            _plan = plan;
            _planOptions = options;

            var lines = new List<string>
            {
                options.SessionFilter.Count == 0
                    ? "Restauração completa: todas as sessões e as configurações do backup."
                    : $"{options.SessionFilter.Count} sessão(ões) escolhida(s); configurações deste computador ficam como estão.",
                BackupsViewModel.DescribePlan(plan),
            };
            if (plan.ProjectRenames.Count > 0)
                lines.Add("Pastas de projeto: " + string.Join(", ", plan.ProjectRenames.Select(r => $"{r.Key} → {r.Value}")));
            lines.AddRange(plan.Warnings.Select(w => "Aviso: " + w));
            _summary.Text = string.Join("\n\n", lines);
            _summary.ForeColor = plan.Warnings.Count > 0 ? Theme.Warn : Theme.Text;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowSummaryError(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task Restore()
    {
        if (_plan is not { } plan || _planOptions is not { } options) return;

        if (ClaudeDesktopProcess.IsRunning())
        {
            if (!ConfirmClose()) return;
            SetBusy(true);
            _summary.Text = "Fechando o Claude Desktop…";
            var closed = await Task.Run(() => ClaudeDesktopProcess.TryClose(TimeSpan.FromSeconds(30)));
            if (!closed)
            {
                SetBusy(false);
                ShowSummaryError("O Claude Desktop não fechou em 30 segundos. Feche manualmente e tente de novo.");
                return;
            }
        }

        SetBusy(true);
        var total = plan.Count(RestoreAction.Create) + plan.Count(RestoreAction.Overwrite);
        var done = 0;
        var progress = new Progress<string>(_ =>
        {
            if (++done % 20 == 0 || done == total) _summary.Text = $"Restaurando {done} de {total} arquivo(s)…";
        });
        _summary.ForeColor = Theme.Text;
        _summary.Text = "Conferindo o backup antes de gravar…";

        try
        {
            var result = await Task.Run(() => RestoreService.Execute(_archive, plan, options, progress));
            ResultMessage = $"Restaurado: {plan.Count(RestoreAction.Create)} arquivo(s) novo(s), "
                            + $"{plan.Count(RestoreAction.Overwrite)} substituído(s)."
                            + (result.RollbackZip is null ? "" : $" Estado anterior em {Path.GetFileName(result.RollbackZip)}.");
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetBusy(false);
            ShowSummaryError(ex.Message);
        }
    }

    private bool ConfirmClose()
    {
        using var dialog = new DarkDialog(this, "Fechar o Claude Desktop", 460, 250);
        dialog.SetHeader("Fechar o Claude Desktop?", "Ele regrava as sessões ao sair e desfaria a restauração.", danger: true);
        dialog.Body.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Font = Theme.Body,
            ForeColor = Theme.TextMuted,
            Text = "O app é fechado como se você clicasse no X. Conversas em andamento são interrompidas. "
                 + "Abra o Claude de novo depois da restauração.",
        });
        var cancel = new FlatButton("Cancelar");
        var confirm = new FlatButton("Fechar e restaurar", FlatButton.Kind.Danger);
        cancel.Click += (_, _) => { dialog.DialogResult = DialogResult.Cancel; dialog.Close(); };
        confirm.Click += (_, _) => { dialog.DialogResult = DialogResult.OK; dialog.Close(); };
        dialog.SetFooterButtons(cancel, confirm);
        return dialog.ShowDialog(this) == DialogResult.OK;
    }

    private void ShowSummaryError(string message)
    {
        _summary.ForeColor = Theme.DangerText;
        _summary.Text = message;
    }

    private void SetBusy(bool busy)
    {
        UseWaitCursor = busy;
        _analyze.Enabled = !busy;
        _cancel.Enabled = !busy;
        _restore.Enabled = !busy && _plan is { } p && p.Count(RestoreAction.Create) + p.Count(RestoreAction.Overwrite) > 0;
        Body.Enabled = !busy;
    }

    /// <summary>
    /// Uma troca de pasta em duas linhas, para caber na coluna: em cima a pasta de origem (com o ×
    /// para remover), embaixo a pasta neste computador (com o seletor).
    /// </summary>
    private sealed class MapRow : Panel
    {
        private const int Arrow = 26;

        private readonly DarkTextBox _from = new() { Placeholder = "Pasta no computador de origem" };
        private readonly DarkTextBox _to = new() { Placeholder = "Pasta neste computador" };
        private readonly FlatButton _browse = new("…");
        private readonly FlatButton _remove = new("×", FlatButton.Kind.Ghost);

        public event EventHandler? Changed;
        public event EventHandler? RemoveRequested;

        public string From => _from.Inner.Text.Trim().Trim('"');
        public string To => _to.Inner.Text.Trim().Trim('"');

        public MapRow(string from, string to)
        {
            Height = (Theme.HeightControl + 2) * 2 + Theme.GapXs;
            BackColor = Theme.Card;
            Margin = new Padding(0, 0, 0, Theme.GapMd);
            _from.Inner.Text = from;
            _from.Inner.Font = Theme.Mono;
            _to.Inner.Text = to;
            _to.Inner.Font = Theme.Mono;
            _browse.Padding = _remove.Padding = Padding.Empty;

            _from.Inner.TextChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            _to.Inner.TextChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            _browse.Click += (_, _) => Browse();
            _remove.Click += (_, _) => RemoveRequested?.Invoke(this, EventArgs.Empty);

            Controls.AddRange([_from, _to, _browse, _remove]);
            Resize += (_, _) => LayoutRow();
            Paint += (_, e) =>
            {
                var arrow = new Rectangle(0, _to.Top, Arrow, _to.Height);
                TextRenderer.DrawText(e.Graphics, "↳", Theme.Body, arrow, Theme.TextFaint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
        }

        private void LayoutRow()
        {
            var line = Theme.HeightControl + 2;
            const int button = 40;
            _remove.SetBounds(ClientSize.Width - button, 1, button, Theme.HeightControl);
            _from.SetBounds(0, 0, Math.Max(80, _remove.Left - Theme.GapXs), line);
            var top = line + Theme.GapXs;
            _browse.SetBounds(ClientSize.Width - button, top + 1, button, Theme.HeightControl);
            _to.SetBounds(Arrow, top, Math.Max(80, _browse.Left - Theme.GapXs - Arrow), line);
            Invalidate();
        }

        private void Browse()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = $"Onde está {From} neste computador?",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
            };
            if (Directory.Exists(To)) dialog.InitialDirectory = To;
            if (dialog.ShowDialog(FindForm()) == DialogResult.OK) _to.Inner.Text = dialog.SelectedPath;
        }
    }
}
