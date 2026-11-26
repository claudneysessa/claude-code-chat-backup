using ClaudeChatBackup.Gui.Theming;
using ClaudeChatBackup.Gui.ViewModels;

namespace ClaudeChatBackup.Gui.Views;

/// <summary>Confirmação de exclusão de um backup: mostra o arquivo e avisa que ele vai para a Lixeira.</summary>
internal sealed class ConfirmDeleteBackupDialog : DarkDialog
{
    public ConfirmDeleteBackupDialog(Form owner, BackupEntry entry) : base(owner, "Excluir backup", 480, 300)
    {
        SetHeader("Excluir este backup?", "O arquivo vai para a Lixeira do Windows.", danger: true);

        var text = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 62,
            ForeColor = Theme.TextMuted,
            Font = Theme.Body,
            Text = entry.CanRestore
                ? $"{BackupsViewModel.DescribeContent(entry)}, de {Formatting.When(entry.When)}. "
                  + "As sessões no Claude não são tocadas; só o arquivo de backup sai da lista."
                : "Este arquivo não é um backup válido. Ele sai da lista e vai para a Lixeira.",
        };

        var path = new SurfacePanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            Radius = 8,
            Surface = Theme.Field,
            BorderColor = Theme.BorderInput,
            Padding = new Padding(12, 0, 12, 0),
        };
        path.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Font = Theme.MonoSmall,
            ForeColor = Theme.TextMuted,
            Text = entry.Path,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        });

        Body.Controls.Add(path);
        Body.Controls.Add(text);

        var cancel = new FlatButton("Cancelar");
        var confirm = new FlatButton("Excluir", FlatButton.Kind.Danger);
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        confirm.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        SetFooterButtons(cancel, confirm);
    }
}
