using System.Drawing.Drawing2D;

namespace ClaudeChatBackup.Gui.Theming;

/// <summary>
/// Aviso de processo no rodapé da barra lateral: seguro (ponto verde) ou "Claude Desktop aberto"
/// (fundo accent translúcido, ponto laranja). Reavaliar a cada refresh da lista.
/// </summary>
internal sealed class GuardCard : SurfacePanel
{
    private bool _safe;

    public GuardCard()
    {
        Radius = 10;
        Surface = Theme.Card;
        SurfaceHover = Theme.Card;
        BorderColor = Theme.Border;
        Height = 96;
        Padding = new Padding(13, 12, 13, 12);
        UpdateState(safe: true);
    }

    public void UpdateState(bool safe)
    {
        _safe = safe;
        Surface = safe ? Theme.Card : Theme.Mix(Theme.Accent, Theme.Card, 0.08);
        BorderColor = safe ? Theme.Border : Theme.Mix(Theme.Accent, Theme.Card, 0.30);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var dot = _safe ? Theme.Ok : Theme.Warn;
        var title = _safe ? Theme.GuardText : Theme.Accent;

        // Ponto 7px com halo de 3px.
        var center = new Point(17, Padding.Top + 8);
        using (var halo = new SolidBrush(Theme.Mix(dot, Surface, 0.35)))
            g.FillEllipse(halo, center.X - 5, center.Y - 5, 15, 15);
        using (var fill = new SolidBrush(dot))
            g.FillEllipse(fill, center.X - 3, center.Y - 3, 7, 7);

        var textLeft = 34;
        TextRenderer.DrawText(g, _safe ? "Pronto para restaurar" : "Claude Desktop aberto", Theme.BodyBold,
            new Point(textLeft, Padding.Top - 1), title);

        var body = _safe
            ? "Nenhum processo do Claude Desktop em execução."
            : "O backup funciona. Para restaurar, feche o app.";
        var bodyRect = Rectangle.FromLTRB(textLeft, Padding.Top + 20, Width - Padding.Right, Height - Padding.Bottom);
        TextRenderer.DrawText(g, body, Theme.Small, bodyRect, Theme.TextFaint,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }
}