using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ClaudeChatBackup.Gui.Theming;

/// <summary>Ícones da navegação lateral, desenhados com traço fino (stroke 1.8).</summary>
internal enum NavIcon { Sessions, Backups }

/// <summary>
/// Item da navegação lateral: altura 40, raio 9, ícone 16x16 à esquerda e contador opcional
/// à direita. Ativo: fundo cheio <c>NavActive</c> + texto <c>Text</c> (sem barra lateral colorida).
/// </summary>
internal sealed class NavItem : Control
{
    private bool _hover;
    private bool _active;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public NavIcon Glyph { get; set; }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Counter { get; set; }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Active
    {
        get => _active;
        set { _active = value; Invalidate(); }
    }

    public NavItem(string text, NavIcon glyph)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
               | ControlStyles.SupportsTransparentBackColor, true);
        Text = text;
        Glyph = glyph;
        Font = Theme.Body;
        Height = Theme.HeightRow;
        Width = 190;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        Padding = new Padding(10, 0, 10, 0);
        TabStop = true;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var back = new SolidBrush(Parent?.BackColor ?? Theme.Rail)) g.FillRectangle(back, ClientRectangle);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        if (Active || _hover)
        {
            using var path = Theme.RoundRect(r, Theme.RadiusControl);
            using var fill = new SolidBrush(Active ? Theme.NavActive : Theme.CardHover);
            g.FillPath(fill, path);
        }

        var fg = Active || _hover ? Theme.Text : Theme.TextMuted;
        using (var pen = new Pen(fg, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            var icon = new Rectangle(Padding.Left, (Height - 16) / 2, 16, 16);
            DrawGlyph(g, pen, Glyph, icon);
        }

        var textX = Padding.Left + 16 + Theme.GapSm;
        TextRenderer.DrawText(g, Text, Font,
            Rectangle.FromLTRB(textX, 0, Width - 40, Height), fg,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (Counter is not null)
        {
            TextRenderer.DrawText(g, Counter, Theme.MonoSmall,
                Rectangle.FromLTRB(Width - 34, 0, Width - Padding.Right, Height),
                Theme.TextFaint, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }
    }

    private static void DrawGlyph(Graphics g, Pen pen, NavIcon glyph, Rectangle r)
    {
        switch (glyph)
        {
            case NavIcon.Sessions:
                // Balão de conversa: corpo arredondado + rabicho embaixo à esquerda.
                using (var bubble = Theme.RoundRect(new Rectangle(r.X + 1, r.Y + 2, 14, 10), 3))
                    g.DrawPath(pen, bubble);
                g.DrawLine(pen, r.X + 4, r.Y + 12, r.X + 4, r.Y + 15);
                g.DrawLine(pen, r.X + 4, r.Y + 15, r.X + 8, r.Y + 12);
                break;
            case NavIcon.Backups:
                g.DrawRectangle(pen, r.X + 1, r.Y + 4, 14, 3);
                g.DrawLine(pen, r.X + 2, r.Y + 7, r.X + 2, r.Y + 14);
                g.DrawLine(pen, r.X + 14, r.Y + 7, r.X + 14, r.Y + 14);
                g.DrawLine(pen, r.X + 2, r.Y + 14, r.X + 14, r.Y + 14);
                g.DrawLine(pen, r.X + 6, r.Y + 10, r.X + 10, r.Y + 10);
                break;
        }
    }
}