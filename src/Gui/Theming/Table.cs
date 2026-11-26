using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ClaudeChatBackup.Gui.Theming;

/// <summary>Coluna de tabela. Largura 0 = flexível (divide o espaço que sobra).</summary>
internal sealed record Column(string Header, int Width = 0, bool RightAlign = false, bool Mono = false);

/// <summary>
/// Tabela escura no padrão da tela de backups: cabeçalho em CAPS, linhas de 56px com divisória,
/// hover e área reservada à direita para botões de ação. Linhas marcáveis desenham uma caixa de
/// seleção na primeira coluna.
/// </summary>
internal sealed class Table : SurfacePanel
{
    public const int Pad = 18;
    public const int CheckWidth = 34;

    private readonly IReadOnlyList<Column> _columns;
    private readonly Panel _header = new() { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Card };
    private readonly Panel _scroll = new() { Dock = DockStyle.Fill, BackColor = Theme.Shell, AutoScroll = true };
    private readonly VStack _rows = new() { Dock = DockStyle.Top, BackColor = Theme.Shell, AutoHeight = true };
    private int _anchor = -1;
    private readonly ToolTip _tips = new() { AutoPopDelay = 30000, InitialDelay = 400 };

    public bool Checkable { get; }
    public int ActionWidth { get; }

    public Table(IReadOnlyList<Column> columns, bool checkable = false, int actionWidth = 0)
    {
        _columns = columns;
        Checkable = checkable;
        ActionWidth = actionWidth;
        Radius = Theme.RadiusCard;
        Surface = Theme.Shell;
        BorderColor = Theme.Border;
        Padding = new Padding(1);

        _header.Paint += PaintHeader;
        _header.Resize += (_, _) => _header.Invalidate();
        if (checkable)
        {
            // Caixa do cabeçalho: marca todas as linhas visíveis, ou desmarca se já estão todas.
            _header.MouseClick += (_, e) =>
            {
                if (e.X < Pad + CheckWidth) SetAllChecked(!Rows.Any() || Rows.Any(r => !r.Checked));
            };
            _header.MouseMove += (_, e) => _header.Cursor = e.X < Pad + CheckWidth ? Cursors.Hand : Cursors.Default;
        }
        DarkWindow.DarkScrollBars(_scroll);
        _scroll.MouseWheel += (_, e) =>
        {
            ScrollBy(e.Delta);
            if (e is HandledMouseEventArgs h) h.Handled = true;
        };
        _scroll.Controls.Add(_rows);
        Controls.Add(_scroll);
        Controls.Add(_header);
    }

    public IEnumerable<TableRow> Rows => _rows.Controls.OfType<TableRow>();

    public void SetRows(IEnumerable<TableRow> rows)
    {
        _rows.SuspendLayout();
        foreach (Control c in _rows.Controls) c.Dispose();
        _rows.Controls.Clear();
        foreach (var row in rows)
        {
            row.Owner = this;
            if (row.Cells is [{ Length: > 60 } title, ..]) _tips.SetToolTip(row, title);
            _rows.Controls.Add(row);
        }
        _rows.ResumeLayout(true);
        _scroll.AutoScrollPosition = Point.Empty;
        _anchor = -1;
        _header.Invalidate();
    }

    public void SetAllChecked(bool on)
    {
        foreach (var row in Rows) row.Checked = on;
        _header.Invalidate();
    }

    /// <summary>Clique numa linha marcável; com Shift aplica o mesmo estado a todo o intervalo desde o último clique.</summary>
    internal void OnRowClicked(TableRow row, bool shift)
    {
        var rows = Rows.ToList();
        var index = rows.IndexOf(row);
        var target = !row.Checked;
        if (shift && _anchor >= 0 && _anchor < rows.Count)
        {
            for (var i = Math.Min(_anchor, index); i <= Math.Max(_anchor, index); i++) rows[i].Checked = target;
        }
        else
        {
            row.Checked = target;
        }
        _anchor = index;
    }

    internal void OnRowCheckedChanged() => _header.Invalidate();

    /// <summary>
    /// Rola a lista pela roda do mouse. As linhas são controles próprios e recebem a roda quando o
    /// cursor está sobre elas; sem repassar para cá, a lista só rolaria com o foco no painel.
    /// </summary>
    internal void ScrollBy(int wheelDelta)
    {
        var step = wheelDelta * TableRow.RowHeight * 2 / 120;
        var max = Math.Max(0, _rows.Height - _scroll.ClientSize.Height);
        var y = Math.Clamp(-_scroll.AutoScrollPosition.Y - step, 0, max);
        _scroll.AutoScrollPosition = new Point(0, y);
    }

    /// <summary>Retângulos das colunas para uma largura de linha.</summary>
    public Rectangle[] ColumnBounds(int width)
    {
        var left = Pad + (Checkable ? CheckWidth : 0);
        var fixedWidth = _columns.Sum(c => c.Width);
        var flexibleCount = Math.Max(1, _columns.Count(c => c.Width == 0));
        var flexible = Math.Max(0, width - left - Pad - ActionWidth - fixedWidth) / flexibleCount;

        var bounds = new Rectangle[_columns.Count];
        var x = left;
        for (var i = 0; i < _columns.Count; i++)
        {
            var w = _columns[i].Width == 0 ? flexible : _columns[i].Width;
            bounds[i] = new Rectangle(x, 0, w, 0);
            x += w;
        }
        return bounds;
    }

    public Column ColumnAt(int i) => _columns[i];

    private void PaintHeader(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var pen = new Pen(Theme.Border, 1f)) g.DrawLine(pen, 0, _header.Height - 1, _header.Width, _header.Height - 1);
        if (Checkable)
        {
            var total = Rows.Count();
            var marked = Rows.Count(r => r.Checked);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            TableRow.DrawCheck(g, new Rectangle(Pad, (_header.Height - 16) / 2, 16, 16),
                total > 0 && marked == total, partial: marked > 0 && marked < total);
        }
        var bounds = ColumnBounds(_header.ClientSize.Width);
        for (var i = 0; i < _columns.Count; i++)
        {
            var r = new Rectangle(bounds[i].X, 0, bounds[i].Width - 6, _header.Height);
            TextRenderer.DrawText(g, _columns[i].Header, Theme.Caps, r, Theme.TextFaint,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                | (_columns[i].RightAlign ? TextFormatFlags.Right : TextFormatFlags.Left));
        }
    }
}

/// <summary>Linha da <see cref="Table"/>: textos por coluna, segunda linha opcional na primeira coluna.</summary>
internal sealed class TableRow : Control
{
    public const int RowHeight = 56;

    /// <summary>Títulos longos quebram em até estas linhas; o que passar disso fica no tooltip.</summary>
    public const int MaxTitleLines = 3;

    private static readonly int LineHeight = TextRenderer.MeasureText("Ág", Theme.Body).Height;

    private bool _hover;
    private bool _checked;
    private int _titleLines = 1;

    public event EventHandler? CheckedChanged;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string[] Cells { get; set; } = [];

    /// <summary>Texto menor embaixo da primeira coluna (caminho, id).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Detail { get; set; }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color? DetailColor { get; set; }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Table? Owner { get; set; }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            Owner?.OnRowCheckedChanged();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public TableRow()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Height = RowHeight;
        BackColor = Theme.Shell;
        Font = Theme.Body;
    }

    /// <summary>Coloca botões de ação alinhados à direita da linha.</summary>
    public void AddActions(params FlatButton[] buttons)
    {
        foreach (var b in buttons)
        {
            b.Height = 30;
            b.Radius = Theme.RadiusSmall;
            b.SizeToText();
            b.MouseWheel += (_, e) => Owner?.ScrollBy(e.Delta);
            Controls.Add(b);
        }
        Resize += (_, _) => LayoutActions(buttons);
        LayoutActions(buttons);
    }

    private void LayoutActions(FlatButton[] buttons)
    {
        var right = ClientSize.Width - Table.Pad;
        for (var i = buttons.Length - 1; i >= 0; i--)
        {
            var b = buttons[i];
            b.Location = new Point(right - b.Width, (Height - b.Height) / 2);
            right -= b.Width + Theme.GapXs;
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        FitTitle();
    }

    /// <summary>Altura da linha acompanha o título: quebra em até <see cref="MaxTitleLines"/> linhas na largura da coluna.</summary>
    private void FitTitle()
    {
        if (Owner is null || Cells.Length == 0) return;
        var width = Owner.ColumnBounds(ClientSize.Width)[0].Width - 8;
        if (width <= 0) return;

        var measured = TextRenderer.MeasureText(Cells[0], Theme.Body, new Size(width, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        _titleLines = Math.Clamp((int)Math.Ceiling(measured.Height / (double)LineHeight), 1, MaxTitleLines);

        var height = Math.Max(RowHeight, 9 + _titleLines * LineHeight + (Detail is null ? 0 : 21) + 10);
        if (Height != height) Height = height;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        Owner?.ScrollBy(e.Delta);
        if (e is HandledMouseEventArgs h) h.Handled = true;
        base.OnMouseWheel(e);
    }

    protected override void OnClick(EventArgs e)
    {
        if (Owner?.Checkable == true) Owner.OnRowClicked(this, (ModifierKeys & Keys.Shift) != 0);
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var back = _checked ? Theme.Mix(Theme.Accent, Theme.Shell, 0.07) : _hover ? Theme.Card : Theme.Shell;
        using (var fill = new SolidBrush(back)) g.FillRectangle(fill, ClientRectangle);
        foreach (Control c in Controls) c.BackColor = back;
        using (var pen = new Pen(Theme.RowDivider, 1f)) g.DrawLine(pen, 0, Height - 1, Width, Height - 1);

        if (Owner is null) return;

        if (Owner.Checkable) DrawCheck(g, new Rectangle(Table.Pad, (Height - 16) / 2, 16, 16), _checked);

        var bounds = Owner.ColumnBounds(ClientSize.Width);
        for (var i = 0; i < Cells.Length && i < bounds.Length; i++)
        {
            var col = Owner.ColumnAt(i);
            var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
                        | (col.RightAlign ? TextFormatFlags.Right : TextFormatFlags.Left);
            var cell = new Rectangle(bounds[i].X, 0, bounds[i].Width - 8, Height);

            if (i == 0)
            {
                var titleHeight = _titleLines * LineHeight;
                var top = Detail is null ? (Height - titleHeight) / 2 : 9;
                TextRenderer.DrawText(g, Cells[0], Theme.Body, new Rectangle(cell.X, top, cell.Width, titleHeight),
                    Theme.Text, flags | TextFormatFlags.WordBreak);
                if (Detail is not null)
                    TextRenderer.DrawText(g, Detail, Theme.MonoSmall, new Rectangle(cell.X, top + titleHeight + 3, cell.Width, 18),
                        DetailColor ?? Theme.TextFaint, flags | TextFormatFlags.PathEllipsis);
                continue;
            }

            var font = col.Mono ? Theme.MonoSmall : i == 0 ? Theme.Body : Theme.Small;
            var color = i == 0 ? Theme.Text : Theme.TextMuted;
            TextRenderer.DrawText(g, Cells[i], font, cell, color, flags | TextFormatFlags.VerticalCenter);
        }
    }

    internal static void DrawCheck(Graphics g, Rectangle r, bool on, bool partial = false)
    {
        var filled = on || partial;
        using var path = Theme.RoundRect(r, 4);
        using (var fill = new SolidBrush(filled ? Theme.Accent : Theme.Field)) g.FillPath(fill, path);
        using (var pen = new Pen(filled ? Theme.Accent : Theme.BorderHover, 1f)) g.DrawPath(pen, path);
        if (!filled) return;
        using var tick = new Pen(Theme.AccentText, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        if (partial && !on) g.DrawLine(tick, r.X + 4, r.Y + 8, r.X + 12, r.Y + 8);
        else g.DrawLines(tick, [new Point(r.X + 4, r.Y + 8), new Point(r.X + 7, r.Y + 11), new Point(r.X + 12, r.Y + 5)]);
    }
}
