using System.ComponentModel;

namespace ClaudeChatBackup.Gui.Theming;

/// <summary>
/// Empilha os filhos de cima para baixo na ORDEM DE INSERÇÃO, respeitando o
/// <see cref="Control.Margin"/> de cada um.
/// </summary>
/// <remarks>
/// Existe porque o layout de Dock do WinForms percorre a coleção do fim para o começo — o último
/// controle adicionado fica no topo — e ignora <c>Margin</c> em controle ancorado. Empilhar as
/// telas com <c>Dock=Top</c> inverte a ordem visual e cola os elementos.
/// Filhos com <see cref="Control.AutoSize"/> mantêm a largura natural; os demais são esticados
/// até a largura útil do painel.
/// </remarks>
internal sealed class VStack : Panel
{
    private bool _laying;

    /// <summary>Ajusta a altura do painel ao conteúdo empilhado (usado nas caixas de seção).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AutoHeight { get; set; }

    /// <summary>Altura ocupada pelo conteúdo, já incluindo o padding vertical.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ContentHeight { get; private set; }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);

        if (_laying) return;
        _laying = true;
        try
        {
            var available = ClientSize.Width - Padding.Horizontal;
            var y = Padding.Top;

            foreach (Control child in Controls)
            {
                // Com o painel oculto, TODO filho responde Visible == false (a propriedade
                // considera a cadeia de pais). Nesse caso a informação não serve para nada e
                // pular os filhos zeraria a medição de uma tela montada antes de aparecer.
                if (Visible && !child.Visible) continue;

                y += child.Margin.Top;
                var width = child.AutoSize
                    ? child.Width
                    : Math.Max(0, available - child.Margin.Horizontal);
                child.SetBounds(Padding.Left + child.Margin.Left, y, width, child.Height);
                y += child.Height + child.Margin.Bottom;
            }

            ContentHeight = y + Padding.Bottom;
            if (AutoHeight && Height != ContentHeight)
            {
                Height = ContentHeight;
            }
        }
        finally
        {
            _laying = false;
        }
    }
}
