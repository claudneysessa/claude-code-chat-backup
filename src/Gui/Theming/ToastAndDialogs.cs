using System.Drawing.Drawing2D;

namespace ClaudeChatBackup.Gui.Theming;

/// <summary>
/// Aviso curto e não bloqueante no rodapé da janela. SUBSTITUI todo MessageBox de sucesso
/// ("Alterações salvas", "Backup criado", "Lista recarregada"). MessageBox nunca é escuro —
/// por isso está proibido nesta interface.
/// Uso: ToastHost.Attach(this) no construtor do Form; depois ToastHost.Show("...", ToastKind.Ok).
/// </summary>
internal enum ToastKind { Ok, Warn, Error }

internal sealed class ToastHost : Control
{
    private static ToastHost? _current;

    private readonly System.Windows.Forms.Timer _life = new() { Interval = 2600 };
    private ToastKind _kind = ToastKind.Ok;

    private ToastHost()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
               | ControlStyles.SupportsTransparentBackColor, true);
        Font = Theme.Body;
        Height = 44;
        Visible = false;
        BackColor = Color.Transparent;
        _life.Tick += (_, _) => { _life.Stop(); Visible = false; };
    }

    public static void Attach(Form form)
    {
        var host = new ToastHost();
        form.Controls.Add(host);
        host.BringToFront();
        form.Resize += (_, _) => host.Reposition();
        _current = host;
        host.Reposition();
    }

    public static void Show(string message, ToastKind kind = ToastKind.Ok)
    {
        if (_current is null) return;
        _current._kind = kind;
        _current.Text = message;
        _current.Width = TextRenderer.MeasureText(message, Theme.Body).Width + 58;
        _current.Reposition();
        _current.Visible = true;
        _current.BringToFront();
        _current.Invalidate();
        _current._life.Stop();
        _current._life.Start();
    }

    private void Reposition()
    {
        if (Parent is null) return;
        Left = (Parent.ClientSize.Width - Width) / 2;
        Top = Parent.ClientSize.Height - Height - Theme.StatusBarH - Theme.GapMd;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var back = new SolidBrush(Parent?.BackColor ?? Theme.Shell)) g.FillRectangle(back, ClientRectangle);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundRect(r, 11);
        using (var fill = new SolidBrush(Theme.NavActive)) g.FillPath(fill, path);
        using (var pen = new Pen(Theme.Mix(Theme.BorderHover, Theme.NavActive, 0.6), 1f)) g.DrawPath(pen, path);

        var dotColor = _kind switch
        {
            ToastKind.Warn => Theme.Warn,
            ToastKind.Error => Theme.DangerText,
            _ => Theme.Ok
        };
        using (var glow = new SolidBrush(Theme.Mix(dotColor, Theme.NavActive, 0.2)))
            g.FillEllipse(glow, 16, Height / 2 - 7, 14, 14);
        using (var dot = new SolidBrush(dotColor))
            g.FillEllipse(dot, 19, Height / 2 - 4, 8, 8);

        TextRenderer.DrawText(g, Text, Font,
            Rectangle.FromLTRB(38, 0, Width, Height), Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}