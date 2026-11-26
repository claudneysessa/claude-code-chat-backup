using System.Drawing.Drawing2D;

namespace ClaudeChatBackup.Gui.Theming;

/// <summary>
/// Paleta e métricas únicas da interface. Tema ESCURO FIXO — não existe modo claro,
/// não ler cores do sistema, não usar SystemColors em nenhum controle.
/// Todo valor visual da GUI vem daqui; nenhum literal de cor fora desta classe.
/// </summary>
internal static class Theme
{
    // ---- Superfícies (do mais escuro ao mais claro) -------------------------
    public static readonly Color Desk        = FromHex("#0B0D10"); // fundo fora da janela / área morta
    public static readonly Color Shell       = FromHex("#0F1215"); // fundo da janela
    public static readonly Color Rail        = FromHex("#0D1013"); // barra lateral e barra de status
    public static readonly Color TitleBar    = FromHex("#12161A");
    public static readonly Color Card        = FromHex("#12161A"); // cards e caixas de seção
    public static readonly Color CardHover   = FromHex("#171C22");
    public static readonly Color Field       = FromHex("#0D1013"); // fundo de input
    public static readonly Color Control     = FromHex("#1C222A"); // botão secundário
    public static readonly Color ControlHover= FromHex("#232B34");
    public static readonly Color NavActive   = FromHex("#1A2027");

    // ---- Traços ------------------------------------------------------------
    public static readonly Color Border      = FromHex("#1E242B"); // padrão de card
    public static readonly Color BorderSoft  = FromHex("#1A1F25"); // divisórias de cabeçalho/rodapé
    public static readonly Color BorderInput = FromHex("#262E37");
    public static readonly Color BorderHover = FromHex("#38414C");
    public static readonly Color RowDivider  = FromHex("#171C21"); // linha entre linhas de tabela

    // ---- Texto -------------------------------------------------------------
    public static readonly Color Text        = FromHex("#E7EBF0"); // primário
    public static readonly Color TextStrong  = FromHex("#FFFFFF"); // hover de botão
    public static readonly Color TextMuted   = FromHex("#8A95A3"); // secundário
    public static readonly Color TextFaint   = FromHex("#6B7583"); // caminhos, metadados
    public static readonly Color TextLabel   = FromHex("#AAB3BE"); // rótulo de campo

    // ---- Marca e semântica -------------------------------------------------
    public static readonly Color Accent      = FromHex("#D97757"); // laranja Claude
    public static readonly Color AccentHover = FromHex("#E58B6D");
    public static readonly Color AccentText  = FromHex("#1F0E08"); // texto SOBRE o accent
    public static readonly Color Ok          = FromHex("#3FB87A");
    public static readonly Color OkText      = FromHex("#5FCB95");
    public static readonly Color Warn        = FromHex("#E8A33D");
    public static readonly Color Danger      = FromHex("#D94B43");
    public static readonly Color DangerHover = FromHex("#E65A52");
    public static readonly Color DangerText  = FromHex("#F27A72");
    public static readonly Color GuardText   = FromHex("#8FD8AE"); // título do card "Pronto para restaurar"
    public static readonly Color EmptyIcon   = FromHex("#2B333D"); // ícone do estado vazio
    public static readonly Color AccentDeep  = FromHex("#B5533A"); // gradiente profundo da marca
    public static readonly Color RailPath    = FromHex("#59626E"); // caminho do arquivo na barra lateral

    // ---- Tipografia --------------------------------------------------------
    // UI: Segoe UI Variable Display -> Segoe UI. Caminhos/hashes: Cascadia Mono -> Consolas.
    public static readonly Font H1        = UiFont(15.5f, FontStyle.Bold);   // título de tela
    public static readonly Font H2        = UiFont(13.5f, FontStyle.Bold);   // título de card
    public static readonly Font Body      = UiFont(9.75f);                   // texto padrão
    public static readonly Font BodyBold  = UiFont(9.75f, FontStyle.Bold);
    public static readonly Font Small     = UiFont(8.75f);                   // metadados
    public static readonly Font Caps      = UiFont(8.0f, FontStyle.Bold);    // rótulo de seção (desenhar em MAIÚSCULAS)
    public static readonly Font Mono      = MonoFont(8.75f);                 // caminhos
    public static readonly Font MonoSmall = MonoFont(8.0f);

    private static Font UiFont(float size, FontStyle style = FontStyle.Regular)
        => Pick(["Segoe UI Variable Display", "Segoe UI Variable Text", "Segoe UI"], size, style);

    private static Font MonoFont(float size, FontStyle style = FontStyle.Regular)
        => Pick(["Cascadia Mono", "Consolas", "Courier New"], size, style);

    private static Font Pick(string[] families, float size, FontStyle style)
    {
        foreach (var family in families)
        {
            try
            {
                var font = new Font(family, size, style);
                if (font.Name.Equals(family, StringComparison.OrdinalIgnoreCase)) return font;
                font.Dispose();
            }
            catch (ArgumentException) { }
        }
        return new Font(SystemFonts.MessageBoxFont!.FontFamily, size, style);
    }

    // ---- Métricas (px) -----------------------------------------------------
    public const int RadiusCard    = 12;
    public const int RadiusControl = 9;
    public const int RadiusSmall   = 7;
    public const int RadiusPill    = 999;

    public const int GapXs = 6;
    public const int GapSm = 10;
    public const int GapMd = 14;
    public const int GapLg = 18;
    public const int GapXl = 26;

    public const int PadScreenX = 30;  // respiro horizontal do conteúdo
    public const int PadScreenY = 26;
    public const int PadCard    = 20;

    public const int HeightControl = 38; // botões e inputs
    public const int HeightRow     = 40;
    public const int HeightNav     = 36; // item de navegação (padding 9/10 do protótipo)
    public const int RailWidth     = 232;
    public const int TitleBarH     = 44;
    public const int StatusBarH    = 34;
    public const int InspectorW    = 300; // coluna direita da tela de edição
    public const int CardMinWidth  = 320;

    /// <summary>Retângulo arredondado reutilizável — usar em TODO desenho de fundo.</summary>
    public static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        var d = Math.Max(1, radius * 2);
        var path = new GraphicsPath();
        if (radius <= 0) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color Mix(Color over, Color under, double alpha)
        => Color.FromArgb(
            (int)(over.R * alpha + under.R * (1 - alpha)),
            (int)(over.G * alpha + under.G * (1 - alpha)),
            (int)(over.B * alpha + under.B * (1 - alpha)));

    public static Color FromHex(string hex)
    {
        var h = hex.TrimStart('#');
        return Color.FromArgb(
            Convert.ToInt32(h[..2], 16),
            Convert.ToInt32(h.Substring(2, 2), 16),
            Convert.ToInt32(h.Substring(4, 2), 16));
    }
}
