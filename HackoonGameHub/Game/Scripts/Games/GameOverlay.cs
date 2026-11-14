#nullable enable
using System;
using Godot;
using Game.Scripts.Games;

/// <summary>
/// Camada que hospeda um minigame em tela cheia sobre o lobby.
///
/// O jogo em si não conhece o lobby: recebe um GameContexto e usa os RPCs da
/// API de multiplayer da sua própria porta. O overlay cuida de abrir/fechar e
/// de devolver o controle ao lobby.
/// </summary>
public partial class GameOverlay : Control
{
	public event Action? Fechado;

	private Control? _conteudo;
	private Label? _titulo;
	private Label? _porta;
	private MinigameBase? _jogo;

	public bool Aberto => _jogo != null;
	public MinigameBase? JogoAtual => _jogo;

	/// <summary>
	/// Raiz onde o minigame é pendurado. O lobby aplica aqui a API de
	/// multiplayer da porta do jogo (isolada da conexão principal).
	/// </summary>
	public Control? SessionRoot => _conteudo;

	public override void _Ready()
	{
		Visible = false;
		MouseFilter = MouseFilterEnum.Stop;
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		var dim = new ColorRect
		{
			Color = new Color(0.03f, 0.04f, 0.07f, 0.96f),
			MouseFilter = MouseFilterEnum.Stop
		};
		dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(dim);

		// Barra superior: identifica o jogo/porta e permite fechar.
		var barra = new ColorRect
		{
			Color = new Color(0.09f, 0.11f, 0.16f, 1f),
			MouseFilter = MouseFilterEnum.Stop
		};
		barra.SetAnchorsPreset(LayoutPreset.TopWide);
		barra.OffsetBottom = 52;
		AddChild(barra);

		var linha = new HBoxContainer();
		linha.SetAnchorsPreset(LayoutPreset.TopWide);
		linha.OffsetLeft = 20;
		linha.OffsetTop = 10;
		linha.OffsetRight = -20;
		linha.OffsetBottom = 46;
		linha.AddThemeConstantOverride("separation", 16);
		AddChild(linha);

		_titulo = new Label
		{
			Text = "JOGO",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			VerticalAlignment = VerticalAlignment.Center
		};
		_titulo.AddThemeFontSizeOverride("font_size", 18);
		_titulo.AddThemeColorOverride("font_color", new Color(1f, 0.886f, 0.502f));
		linha.AddChild(_titulo);

		_porta = new Label
		{
			Text = "PORTA —",
			VerticalAlignment = VerticalAlignment.Center
		};
		_porta.AddThemeFontSizeOverride("font_size", 13);
		_porta.AddThemeColorOverride("font_color", new Color(0.561f, 0.612f, 0.718f));
		linha.AddChild(_porta);

		var sair = new Button
		{
			Text = "✕  FECHAR JOGO",
			TooltipText = "Sair da partida e voltar ao lobby (Esc)",
			FocusMode = FocusModeEnum.None,
			CustomMinimumSize = new Vector2(170, 0)
		};
		sair.AddThemeFontSizeOverride("font_size", 14);
		sair.AddThemeColorOverride("font_color", new Color(1f, 0.72f, 0.72f));
		sair.Pressed += Fechar;
		linha.AddChild(sair);

		_conteudo = new Control
		{
			Name = "GameSessionRoot",
			MouseFilter = MouseFilterEnum.Pass
		};
		_conteudo.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_conteudo.OffsetTop = 52;
		AddChild(_conteudo);
	}

	/// <summary>Abre o jogo (a cena já deve ter sido instanciada pelo lobby).</summary>
	public void Abrir(GameInfo info, MinigameBase jogo, GameContexto contexto)
	{
		if (_jogo != null)
		{
			Fechar();
		}

		_jogo = jogo;
		_jogo.SairSolicitado += Fechar;
		_conteudo?.AddChild(_jogo);
		_jogo.Iniciar(contexto);

		if (_titulo != null)
		{
			_titulo.Text = info.Nome.ToUpperInvariant();
		}

		if (_porta != null)
		{
			_porta.Text = contexto.SouHost
				? $"PORTA {contexto.Porta} • VOCÊ É O DONO"
				: $"PORTA {contexto.Porta} • CONECTADO";
		}

		Visible = true;
		MoveToFront();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!Aberto)
		{
			return;
		}

		if (@event.IsActionPressed("ui_cancel"))
		{
			Fechar();
			GetViewport().SetInputAsHandled();
		}
	}

	public void Fechar()
	{
		if (_jogo != null)
		{
			_jogo.SairSolicitado -= Fechar;
			_jogo.Encerrar();
			_jogo.QueueFree();
			_jogo = null;
		}

		Visible = false;
		Fechado?.Invoke();
	}
}
