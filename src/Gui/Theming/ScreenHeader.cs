namespace ClaudeChatBackup.Gui.Theming;

/// <summary>
/// Cabeçalho padrão de tela: título em H1 e subtítulo em Body/TextMuted, com o respiro de tela
/// do handoff (26 vertical, 30 horizontal). Deve ser o ÚLTIMO controle adicionado à tela, para
/// consumir o topo antes do painel <c>Dock=Fill</c>.
/// </summary>
internal static class ScreenHeader
{
    public static Panel Build(string title, string subtitle, bool divider = false)
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = divider ? 106 : 88,
            BackColor = Theme.Shell,
        };

        header.Controls.Add(new Label
        {
            Text = title,
            Font = Theme.H1,
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(Theme.PadScreenX, Theme.PadScreenY),
        });
        header.Controls.Add(new Label
        {
            Text = subtitle,
            Font = Theme.Body,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(Theme.PadScreenX, Theme.PadScreenY + 28),
        });

        if (divider)
        {
            header.Paint += (_, e) =>
            {
                using var pen = new Pen(Theme.BorderSoft, 1f);
                e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
            };
        }

        return header;
    }
}
