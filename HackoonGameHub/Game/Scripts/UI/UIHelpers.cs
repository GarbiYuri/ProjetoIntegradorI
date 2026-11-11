#nullable enable
using Godot;

namespace Game.Scripts;

/// <summary>
/// Kit de peças de interface compartilhado pelas telas do jogo.
///
/// Tudo aqui usa o tema HackoonTheme.tres e as mesmas cores, então quando
/// quiser mudar o visual (ex.: trocar a cor dos cartões) basta alterar os
/// valores em UM lugar e todas as telas acompanham.
///
/// Uso típico (dentro de um script de tela):
///   var cartao = UIHelpers.Cartao();
///   var linha  = UIHelpers.Linha();
///   linha.AddChild(UIHelpers.Texto(aluno.name));
///   cartao.AddChild(linha);
///   container.AddChild(cartao);
/// </summary>
public static class UIHelpers
{
    // ------------------------------------------------------------------
    // Paleta (uma única fonte de verdade para todas as telas)
    // ------------------------------------------------------------------
    public static readonly Color Amarelo = new(0.949f, 0.765f, 0.302f);
    public static readonly Color AmareloClaro = new(1f, 0.886f, 0.502f);
    public static readonly Color FundoEscuro = new(0.09f, 0.11f, 0.16f);
    public static readonly Color BordaSuave = new(0.2f, 0.25f, 0.35f);
    public static readonly Color TextoSuave = new(0.561f, 0.612f, 0.718f);
    public static readonly Color TextoClaro = new(0.741f, 0.788f, 0.886f);
    public static readonly Color VerdeOk = new(0.47f, 0.86f, 0.61f);
    public static readonly Color VermelhoErro = new(1f, 0.478f, 0.478f);

    /// <summary>Cores que as crianças podem escolher para o personagem.</summary>
    public static readonly Color[] CoresPersonagem =
    {
        Colors.White,
        new(0.949f, 0.765f, 0.302f), // amarelo Hackoon
        new(0.388f, 0.784f, 0.580f), // verde
        new(0.427f, 0.686f, 0.949f), // azul
        new(0.949f, 0.478f, 0.478f), // vermelho
        new(0.741f, 0.518f, 0.949f), // roxo
    };

    // ------------------------------------------------------------------
    // Peças reutilizáveis
    // ------------------------------------------------------------------

    /// <summary>Estilo de cartão (fundo escuro, borda, cantos arredondados).</summary>
    public static StyleBoxFlat EstiloCartao(Color? fundo = null, Color? borda = null,
        int margem = 12, int raio = 9)
    {
        return new StyleBoxFlat
        {
            BgColor = fundo ?? FundoEscuro,
            BorderColor = borda ?? BordaSuave,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = raio,
            CornerRadiusTopRight = raio,
            CornerRadiusBottomLeft = raio,
            CornerRadiusBottomRight = raio,
            ContentMarginLeft = margem,
            ContentMarginTop = margem / 2 + 2,
            ContentMarginRight = margem,
            ContentMarginBottom = margem / 2 + 2
        };
    }

    /// <summary>Painel de cartão pronto para receber linhas de conteúdo.</summary>
    public static PanelContainer Cartao(StyleBoxFlat? estilo = null)
    {
        var cartao = new PanelContainer();
        cartao.AddThemeStyleboxOverride("panel", estilo ?? EstiloCartao());
        return cartao;
    }

    /// <summary>Linha horizontal com espaçamento padrão.</summary>
    public static HBoxContainer Linha(int separacao = 14)
    {
        var linha = new HBoxContainer();
        linha.AddThemeConstantOverride("separation", separacao);
        return linha;
    }

    /// <summary>Coluna vertical com espaçamento padrão.</summary>
    public static VBoxContainer Coluna(int separacao = 8)
    {
        var coluna = new VBoxContainer();
        coluna.AddThemeConstantOverride("separation", separacao);
        return coluna;
    }

    /// <summary>Texto normal.</summary>
    public static Label Texto(string conteudo, int tamanho = 18)
    {
        var label = new Label { Text = conteudo };
        label.AddThemeFontSizeOverride("font_size", tamanho);
        return label;
    }

    /// <summary>Texto que cresce para ocupar a linha (usar dentro de uma Linha).</summary>
    public static Label TextoExpandido(string conteudo, int tamanho = 18)
    {
        Label label = Texto(conteudo, tamanho);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return label;
    }

    /// <summary>Texto secundário (cinza-azulado, discreto).</summary>
    public static Label TextoSuaveLabel(string conteudo, int tamanho = 16)
    {
        Label label = Texto(conteudo, tamanho);
        label.AddThemeColorOverride("font_color", TextoSuave);
        return label;
    }

    /// <summary>Título grande amarelo de seção.</summary>
    public static Label TituloSecao(string conteudo, int tamanho = 20)
    {
        var label = new Label { Text = conteudo };
        label.AddThemeColorOverride("font_color", Amarelo);
        label.AddThemeFontSizeOverride("font_size", tamanho);
        return label;
    }

    /// <summary>Selo pequeno "CONECTADO" / "ONLINE" em verde.</summary>
    public static Label SeloStatus(string conteudo, bool alinhadoDireita = true)
    {
        var label = new Label { Text = conteudo };
        if (alinhadoDireita)
        {
            label.HorizontalAlignment = HorizontalAlignment.Right;
        }
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", VerdeOk);
        return label;
    }

    /// <summary>Mensagem de erro em vermelho (quebra de linha automática).</summary>
    public static Label Erro()
    {
        var label = new Label();
        label.AddThemeColorOverride("font_color", VermelhoErro);
        label.AddThemeFontSizeOverride("font_size", 16);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.Visible = false;
        return label;
    }

    /// <summary>
    /// Botão redondo de cor (paleta do personagem). O selecionado fica com
    /// borda amarela — passe o "destaque" de volta em MarcarSelecionado.
    /// </summary>
    public static Button BotaoCor(Color cor, int tamanho = 40)
    {
        var botao = new Button
        {
            CustomMinimumSize = new Vector2(tamanho, tamanho),
            TooltipText = $"Cor: {cor.ToHtml(false).ToUpperInvariant()}"
        };

        var estilo = new StyleBoxFlat
        {
            BgColor = cor,
            CornerRadiusTopLeft = tamanho / 2,
            CornerRadiusTopRight = tamanho / 2,
            CornerRadiusBottomLeft = tamanho / 2,
            CornerRadiusBottomRight = tamanho / 2,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            BorderColor = BordaSuave
        };

        var estiloSelecionado = (StyleBoxFlat)estilo.Duplicate();
        estiloSelecionado.BorderColor = AmareloClaro;
        estiloSelecionado.BorderWidthLeft = 3;
        estiloSelecionado.BorderWidthTop = 3;
        estiloSelecionado.BorderWidthRight = 3;
        estiloSelecionado.BorderWidthBottom = 3;

        botao.AddThemeStyleboxOverride("normal", estilo);
        botao.AddThemeStyleboxOverride("hover", estilo);
        botao.AddThemeStyleboxOverride("pressed", estiloSelecionado);
        botao.AddThemeStyleboxOverride("focus", estiloSelecionado);

        botao.SetMeta("estilo_normal", estilo);
        botao.SetMeta("estilo_selecionado", estiloSelecionado);
        return botao;
    }

    /// <summary>Marca um botão de cor como o escolhido (borda amarela).</summary>
    public static void MarcarSelecionado(Button? selecionado, params Button[] todos)
    {
        foreach (Button botao in todos)
        {
            var normal = (StyleBoxFlat)botao.GetMeta("estilo_normal").As<StyleBoxFlat>();
            botao.AddThemeStyleboxOverride("normal", normal);
            botao.AddThemeStyleboxOverride("hover", normal);
        }

        if (selecionado != null)
        {
            var destaque = (StyleBoxFlat)selecionado.GetMeta("estilo_selecionado").As<StyleBoxFlat>();
            selecionado.AddThemeStyleboxOverride("normal", destaque);
            selecionado.AddThemeStyleboxOverride("hover", destaque);
        }
    }
}
