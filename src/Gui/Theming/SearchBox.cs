using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ClaudeChatBackup.Gui.Theming;

/// <summary>Campo de busca escuro: input sem borda com lupa desenhada à esquerda.</summary>
internal sealed class SearchBox : SurfacePanel
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

    public SearchBox()
    {
        Radius = Theme.RadiusControl;
        Surface = Theme.Field;
        SurfaceHover = Theme.Field;
        BorderColor = Theme.BorderInput;
        Width = 240;
        Height = Theme.HeightControl + 2;
        Padding = new Padding(34, 0, 13, 0);
        Inner.GotFocus += (_, _) => { BorderColor = Theme.Accent; Invalidate(); };
        Inner.LostFocus += (_, _) => { BorderColor = Theme.BorderInput; Invalidate(); };
        Inner.TextChanged += (_, _) => Invalidate();
        Controls.Add(Inner);
        Resize += (_, _) => CenterInner();
        CenterInner();
    }

    /// <summary>
    /// O TextBox é centrado à mão (e não com <c>Dock=Fill</c>) para não cobrir a borda
    /// arredondada nem a lupa que a superfície desenha.
    /// </summary>
    private void CenterInner()
        => Inner.SetBounds(
            Padding.Left,
            Math.Max(0, (Height - Inner.PreferredHeight) / 2),
            Math.Max(0, Width - Padding.Horizontal),
            Inner.PreferredHeight);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Theme.TextFaint, 1.6f);
        g.DrawEllipse(pen, 13, (Height - 15) / 2 + 1, 9, 9);
        g.DrawLine(pen, 20, (Height - 15) / 2 + 9, 24, (Height - 15) / 2 + 13);
    }
}