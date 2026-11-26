using System.Drawing.Drawing2D;

namespace ClaudeChatBackup.Gui.Theming;

/// <summary>
/// Diálogo modal escuro com fundo escurecido. Base para confirmação de exclusão e para o
/// seletor de pastas. Nunca usar MessageBox: ele ignora o tema.
/// </summary>
internal class DarkDialog : Form
{
    private readonly Form? _owner;

    internal readonly Panel Header = new() { Dock = DockStyle.Top, BackColor = Theme.Card, Height = 74, Padding = new Padding(22, 20, 22, 14) };
    internal readonly Panel Body = new() { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(22, 16, 22, 16) };
    internal readonly Panel Footer = new() { Dock = DockStyle.Bottom, BackColor = Theme.Shell, Height = 70, Padding = new Padding(22, 16, 22, 16) };

    public DarkDialog(Form? owner, string title, int width = 440, int height = 300)
    {
        _owner = owner;
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(width, height);
        DarkWindow.Apply(this);
        BackColor = Theme.Card;

        Controls.Add(Body);
        Controls.Add(Footer);
        Controls.Add(Header);

        Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border, 1f);
            e.Graphics.DrawLine(pen, 0, Header.Bottom, Width, Header.Bottom);
            e.Graphics.DrawLine(pen, 0, Footer.Top, Width, Footer.Top);
        };
    }

    /// <summary>Mostra o diálogo com a janela dona escurecida (overlay escurecido do protótipo).</summary>
    public DialogResult ShowModalDimmed()
    {
        if (_owner is null) return ShowDialog();

        using var dim = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            BackColor = Color.Black,
            Opacity = 0.55,
            Bounds = _owner.Bounds,
            Owner = _owner
        };
        dim.Show();
        var result = ShowDialog(dim);
        dim.Close();
        return result;
    }

    /// <summary>Título + subtítulo do cabeçalho, com opção de ícone de perigo.</summary>
    internal void SetHeader(string title, string subtitle, bool danger = false)
    {
        Header.Controls.Clear();
        Header.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var x = Header.Padding.Left;
            if (danger)
            {
                var badge = new Rectangle(x, 18, 34, 34);
                using var path = Theme.RoundRect(badge, Theme.RadiusControl);
                using var fill = new SolidBrush(Theme.Mix(Theme.Danger, Theme.Card, 0.14));
                g.FillPath(fill, path);
                using var pen = new Pen(Theme.DangerText, 2f);
                g.DrawLine(pen, badge.X + 17, badge.Y + 10, badge.X + 17, badge.Y + 18);
                g.DrawLine(pen, badge.X + 17, badge.Y + 23, badge.X + 17, badge.Y + 24);
                x += 34 + Theme.GapMd;
            }
            TextRenderer.DrawText(g, title, Theme.H2, new Point(x, 19), Theme.Text);
            TextRenderer.DrawText(g, subtitle, Theme.Small, new Point(x, 40), Theme.TextFaint);
        };
        Header.Invalidate();
    }

    /// <summary>Botões alinhados à direita do rodapé, na ordem informada (o último é o primário).</summary>
    internal void SetFooterButtons(params FlatButton[] buttons)
    {
        Footer.Controls.Clear();
        var right = Footer.ClientSize.Width - Footer.Padding.Right;
        for (var i = buttons.Length - 1; i >= 0; i--)
        {
            var b = buttons[i];
            b.SizeToText();
            b.Top = Footer.Padding.Top;
            b.Left = right - b.Width;
            b.Anchor = AnchorStyles.Top | AnchorStyles.Right; // acompanha diálogos redimensionáveis
            right -= b.Width + Theme.GapSm;
            Footer.Controls.Add(b);
        }
    }
}

/// <summary>Diálogo de erro escuro: cabeçalho de perigo + mensagem da exceção no corpo.</summary>
internal sealed class ErrorDialog : DarkDialog
{
    public ErrorDialog(Form owner, string message) : base(owner, "Erro", 440, 240)
    {
        SetHeader("Algo deu errado", "A operação não foi concluída.", danger: true);

        Body.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Theme.TextMuted,
            Font = Theme.Body,
            Text = message,
            AutoEllipsis = true,
            Padding = new Padding(0, 6, 0, 6)
        });

        var ok = new FlatButton("Fechar");
        ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        SetFooterButtons(ok);
    }
}