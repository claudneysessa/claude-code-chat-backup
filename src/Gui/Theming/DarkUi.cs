using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ClaudeChatBackup.Gui.Theming;

/// <summary>
/// Ajustes de janela obrigatórios para o tema escuro fixo: barra de título escura,
/// scrollbars escuras e cantos arredondados no Windows 11.
/// Chamar <see cref="Apply"/> no construtor de TODO Form (principal e diálogos).
/// </summary>
internal static class DarkWindow
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subApp, string? subIdList);

    public static void Apply(Form form)
    {
        form.BackColor = Theme.Shell;
        form.ForeColor = Theme.Text;
        form.Font = Theme.Body;
        form.HandleCreated += (_, _) => ApplyToHandle(form.Handle);
        if (form.IsHandleCreated) ApplyToHandle(form.Handle);
    }

    private static void ApplyToHandle(IntPtr hwnd)
    {
        var on = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));

        var corner = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        var caption = Bgr(Theme.TitleBar);
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));

        var border = Bgr(Theme.Border);
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));

        var text = Bgr(Theme.Text);
        DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));
    }

    /// <summary>Scrollbar escura em qualquer controle que role (Panel, ListView, TextBox).</summary>
    public static void DarkScrollBars(Control control)
    {
        if (control.IsHandleCreated) SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        else control.HandleCreated += (_, _) => SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
    }

    private static int Bgr(Color c) => c.R | (c.G << 8) | (c.B << 16);
}

/// <summary>Painel com fundo arredondado, borda de 1px e hover opcional. Base de card e caixa de seção.</summary>
internal class SurfacePanel : Panel
{
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = Theme.RadiusCard;
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Surface { get; set; } = Theme.Card;
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color SurfaceHover { get; set; } = Theme.Card;
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Theme.Border;
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderHoverColor { get; set; } = Theme.Border;
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Interactive { get; set; }

    protected bool Hovered;

    public SurfacePanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
               | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        Padding = new Padding(Theme.PadCard);
    }

    /// <summary>Clipping no raio: filhos (TextBox etc.) respeitam o canto arredondado.</summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateRegion();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRegion();
    }

    private void UpdateRegion()
    {
        if (Width <= 0 || Height <= 0) return;
        using var path = Theme.RoundRect(new Rectangle(0, 0, Width, Height), Radius);
        var previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        if (!Interactive) return;
        Hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (!Interactive) return;
        Hovered = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // O container pai pinta o fundo — cards nunca são transparentes de verdade.
        using (var back = new SolidBrush(Parent?.BackColor ?? Theme.Shell)) g.FillRectangle(back, ClientRectangle);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundRect(r, Radius);
        using var fill = new SolidBrush(Hovered ? SurfaceHover : Surface);
        using var pen = new Pen(Hovered ? BorderHoverColor : BorderColor, 1f);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);

        base.OnPaint(e);
    }
}

/// <summary>Botão chapado, sem borda do sistema. Variantes: Primary (accent), Secondary, Danger, Ghost.</summary>
internal sealed class FlatButton : Control
{
    public enum Kind { Primary, Secondary, Danger, DangerOutline, Ghost }

    private bool _hover, _down;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Kind Variant { get; set; } = Kind.Secondary;
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = Theme.RadiusControl;
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Image? Glyph { get; set; }          // 16x16, já na cor certa

    public FlatButton(string text, Kind variant = Kind.Secondary)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Text = text;
        Variant = variant;
        Font = Theme.BodyBold;
        Height = Theme.HeightControl;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        AutoSize = false;
        Padding = new Padding(16, 0, 16, 0);
        TabStop = true;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private (Color Back, Color Fore, Color Border) Palette()
    {
        if (!Enabled) return (Theme.Control, Theme.TextFaint, Theme.Border);
        return Variant switch
        {
            Kind.Primary   => (_hover ? Theme.AccentHover : Theme.Accent, Theme.AccentText, Color.Transparent),
            Kind.Danger    => (_hover ? Theme.DangerHover : Theme.Danger, Color.White, Color.Transparent),
            Kind.DangerOutline => (_hover ? Theme.Mix(Theme.Danger, Theme.Control, 0.16) : Theme.Mix(Theme.Danger, Theme.Control, 0.08),
                                   Theme.DangerText,
                                   _hover ? Theme.Mix(Theme.Danger, Theme.Control, 0.50) : Theme.Mix(Theme.Danger, Theme.Control, 0.28)),
            Kind.Ghost     => (_hover ? Theme.Control : Color.Transparent, _hover ? Theme.Accent : Theme.TextMuted, Color.Transparent),
            _              => (_hover ? Theme.ControlHover : Theme.Control, _hover ? Theme.TextStrong : Theme.Text, Theme.BorderInput),
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using (var back = new SolidBrush(Parent?.BackColor ?? Theme.Shell)) g.FillRectangle(back, ClientRectangle);

        var (bg, fg, bd) = Palette();
        if (_down) bg = Theme.Mix(Color.Black, bg, 0.10);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundRect(r, Radius);
        using (var fill = new SolidBrush(bg)) g.FillPath(fill, path);
        if (bd.A > 0) using (var pen = new Pen(bd, 1f)) g.DrawPath(pen, path);

        var textRect = r;
        if (Glyph is not null)
        {
            g.DrawImage(Glyph, new Rectangle(Padding.Left, (Height - 16) / 2, 16, 16));
            textRect = Rectangle.FromLTRB(Padding.Left + 16 + Theme.GapSm, 0, r.Right - Padding.Right, r.Bottom);
        }

        TextRenderer.DrawText(g, Text, Font, textRect, fg,
            TextFormatFlags.VerticalCenter | (Glyph is null ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left)
            | TextFormatFlags.EndEllipsis);
    }

    /// <summary>Largura automática pelo texto — chamar depois de definir Text/Glyph.</summary>
    public void SizeToText()
    {
        var w = TextRenderer.MeasureText(Text, Font).Width + Padding.Horizontal + (Glyph is null ? 0 : 16 + Theme.GapSm);
        Width = Math.Max(w, 84);
    }
}

/// <summary>Input escuro: TextBox sem borda dentro de uma superfície arredondada com foco accent.</summary>
internal sealed class DarkTextBox : SurfacePanel
{
    public TextBox Inner { get; } = new()
    {
        BorderStyle = BorderStyle.None,
        BackColor = Theme.Field,
        ForeColor = Theme.Text,
        Font = Theme.Body,
    };

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Placeholder { get => Inner.PlaceholderText; set => Inner.PlaceholderText = value; }

    /// <summary>Campo de várias linhas (script de inicialização); muda o alinhamento do texto.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Multiline
    {
        get => Inner.Multiline;
        set
        {
            Inner.Multiline = value;
            Inner.ScrollBars = value ? ScrollBars.Vertical : ScrollBars.None;
            if (value) DarkWindow.DarkScrollBars(Inner);
            CenterInner();
        }
    }

    public DarkTextBox()
    {
        Radius = Theme.RadiusControl;
        Surface = Theme.Field;
        SurfaceHover = Theme.Field;
        BorderColor = Theme.BorderInput;
        Height = Theme.HeightControl + 2;
        Padding = new Padding(13, 0, 13, 0);
        Inner.GotFocus += (_, _) => { BorderColor = Theme.Accent; Invalidate(); };
        Inner.LostFocus += (_, _) => { BorderColor = Theme.BorderInput; Invalidate(); };
        Inner.TextChanged += (_, _) => Invalidate();
        Controls.Add(Inner);
        Resize += (_, _) => CenterInner();
        CenterInner();
    }

    /// <summary>
    /// O TextBox é centrado à mão (e não com <c>Dock=Fill</c>) para não cobrir a borda
    /// arredondada que a superfície desenha.
    /// </summary>
    private void CenterInner()
    {
        // Uma linha fica centrada; várias linhas ocupam a caixa com respiro em cima e embaixo.
        if (Inner.Multiline)
        {
            Inner.SetBounds(Padding.Left, 9, Math.Max(0, Width - Padding.Horizontal), Math.Max(0, Height - 18));
            return;
        }

        Inner.SetBounds(
            Padding.Left,
            Math.Max(0, (Height - Inner.PreferredHeight) / 2),
            Math.Max(0, Width - Padding.Horizontal),
            Inner.PreferredHeight);
    }
}

/// <summary>Interruptor expandido/recolhido — trilho pill + botão deslizante, animado em 6 passos.</summary>
internal sealed class ToggleSwitch : Control
{
    private bool _on;
    private float _t;
    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 16 };

    public event EventHandler? CheckedChanged;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _on;
        set { if (_on == value) return; _on = value; _anim.Start(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
    }

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
               | ControlStyles.SupportsTransparentBackColor, true);
        Size = new Size(44, 25);
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        TabStop = true;
        _anim.Tick += (_, _) =>
        {
            var target = _on ? 1f : 0f;
            _t += (target - _t) * 0.35f;
            if (Math.Abs(target - _t) < 0.01f) { _t = target; _anim.Stop(); }
            Invalidate();
        };
    }

    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            Checked = !Checked;
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var back = new SolidBrush(Parent?.BackColor ?? Theme.Card)) g.FillRectangle(back, ClientRectangle);

        var track = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundRect(track, Height / 2);
        using var fill = new SolidBrush(Theme.Mix(Theme.Accent, Theme.BorderInput, _t));
        g.FillPath(fill, path);

        var knobSize = Height - 6;
        var x = 3 + (int)((Width - knobSize - 6) * _t);
        using var knob = new SolidBrush(Theme.Mix(Theme.AccentText, Theme.TextMuted, _t));
        g.FillEllipse(knob, x, 3, knobSize, knobSize);
    }
}

/// <summary>Etiqueta pill: "expandido" (verde translúcido) / "recolhido" (cinza).</summary>
internal sealed class StatusPill : Control
{
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Positive { get; set; }

    public StatusPill()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
               | ControlStyles.SupportsTransparentBackColor, true);
        Font = Theme.Small;
        Height = 22;
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var back = new SolidBrush(Parent?.BackColor ?? Theme.Card)) g.FillRectangle(back, ClientRectangle);

        var bg = Positive ? Theme.Mix(Theme.Ok, Theme.Card, 0.12) : Theme.CardHover;
        var bd = Positive ? Theme.Mix(Theme.Ok, Theme.Card, 0.28) : Theme.BorderInput;
        var fg = Positive ? Theme.OkText : Theme.TextFaint;

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundRect(r, Height / 2);
        using (var fill = new SolidBrush(bg)) g.FillPath(fill, path);
        using (var pen = new Pen(bd, 1f)) g.DrawPath(pen, path);
        TextRenderer.DrawText(g, Text, Font, r, fg, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
    }

    public void SizeToText() => Width = TextRenderer.MeasureText(Text, Font).Width + 20;
}
