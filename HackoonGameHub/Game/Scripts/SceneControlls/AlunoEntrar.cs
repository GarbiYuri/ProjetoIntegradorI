using Godot;
using System;
using System.Text.Json;
using Game.Scripts;
using Models;
using Repository.Postgress;
using Service;

public partial class AlunoEntrar : Control
{
	private VBoxContainer _sessoes;
	private Label _status;
	private Button _retryButton;
	private Button _backButton;

	private SessionManager _session;

	private bool _buscando;

	private string _sessaoIp = "127.0.0.1";
	private string _sessaoProfessor = "Professor";
	private string _sessaoSala = "Sala";

	public override void _Ready()
	{
		_sessoes = GetNode<VBoxContainer>("%Sessoes");
		_status = GetNode<Label>("%StatusLabel");
		_retryButton = GetNode<Button>("%RetryButton");
		_backButton = GetNode<Button>("%BackButton");

		_session = GetNode<SessionManager>("/root/SessionManager");

		_retryButton.Pressed += OnRetryPressed;
		_backButton.Pressed += OnBackPressed;

		_ = BuscarSessoes();
	}

	private async void OnRetryPressed()
	{
		await BuscarSessoes();
	}

	private void OnBackPressed()
	{
		GetTree().ChangeSceneToFile("res://Scenes/InitialScene.tscn");
	}

	private async System.Threading.Tasks.Task BuscarSessoes()
	{
		if (_buscando)
		{
			return;
		}

		_buscando = true;
		DefinirCarregando(true);
		LimparSessoes();
		_status.Text = "Procurando sessões na rede local...";

		NetworkPacket pacote;

		try
		{
			pacote = await AlunoService.enterGame();
		}
		catch (Exception e)
		{
			GD.PrintErr("[ALUNO] Falha ao procurar sessão: " + e.Message);

			if (IsInstanceValid(this) && IsInsideTree())
			{
				_status.Text = "Não foi possível conectar ao servidor do professor. " +
							   "Confira se o banco está ativo e tente novamente.";
				DefinirCarregando(false);
			}

			_buscando = false;
			return;
		}

		if (!IsInstanceValid(this) || !IsInsideTree())
		{
			_buscando = false;
			return;
		}

		if (pacote == null)
		{
			_status.Text = "Nenhuma sessão encontrada. Peça ao professor para abrir a sala e tente novamente.";
			DefinirCarregando(false);
			_buscando = false;
			return;
		}

		try
		{
			LerSessao(pacote);
			CriarCartaoSessao();
			_status.Text = "Sessão encontrada! Clique em ENTRAR para escolher seu personagem.";
			GD.Print($"[ALUNO] Sessão recebida: sala '{_sessaoSala}', professor '{_sessaoProfessor}', IP {_sessaoIp}.");
		}
		catch (Exception e)
		{
			GD.PrintErr("[ALUNO] Sessão recebida, mas os dados não puderam ser lidos: " + e.Message);
			_status.Text = "A sessão foi encontrada, mas os dados recebidos estão incompletos. Tente novamente.";
		}

		DefinirCarregando(false);
		_buscando = false;
	}

	private void LerSessao(NetworkPacket pacote)
	{
		_sessaoIp = pacote.senderIP;
		_sessaoProfessor = "Professor";
		_sessaoSala = "Sala sem nome";

		using JsonDocument docExterior = JsonDocument.Parse(pacote.DadosJson);
		JsonElement raiz = docExterior.RootElement;

		if (!raiz.TryGetProperty("DadosJson", out JsonElement dadosElement))
		{
			return;
		}

		string jsonInterno = dadosElement.GetString();

		if (string.IsNullOrWhiteSpace(jsonInterno))
		{
			return;
		}

		var opcoes = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
		Dados dados = JsonSerializer.Deserialize<Dados>(jsonInterno, opcoes);

		if (!string.IsNullOrWhiteSpace(dados.Message))
		{
			_sessaoProfessor = dados.Message.Trim();
		}

		if (!string.IsNullOrWhiteSpace(dados.Room))
		{
			_sessaoSala = dados.Room.Trim();
		}
		else if (!string.IsNullOrWhiteSpace(_session?.roomName))
		{
			_sessaoSala = _session.roomName;
		}
	}

	private void CriarCartaoSessao()
	{
		var cartao = new PanelContainer();
		var linha = new HBoxContainer();
		linha.AddThemeConstantOverride("separation", 18);

		var informacoes = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		informacoes.AddThemeConstantOverride("separation", 3);

		var sala = new Label
		{
			Text = _sessaoSala,
			TooltipText = "Sala criada pelo professor"
		};
		sala.AddThemeFontSizeOverride("font_size", 26);
		sala.AddThemeColorOverride("font_color", new Color(1f, 0.89f, 0.5f));

		var professor = new Label
		{
			Text = $"Professor: {_sessaoProfessor}"
		};
		professor.AddThemeColorOverride("font_color", new Color(0.86f, 0.9f, 0.98f));

		var ip = new Label
		{
			Text = $"Servidor: {_sessaoIp}"
		};
		ip.AddThemeFontSizeOverride("font_size", 14);
		ip.AddThemeColorOverride("font_color", new Color(0.62f, 0.68f, 0.8f));

		informacoes.AddChild(sala);
		informacoes.AddChild(professor);
		informacoes.AddChild(ip);

		var entrar = new Button
		{
			Text = "ENTRAR",
			CustomMinimumSize = new Vector2(150, 0),
			SizeFlagsVertical = SizeFlags.ShrinkCenter
		};
		entrar.Pressed += OnEntrarPressed;

		linha.AddChild(informacoes);
		linha.AddChild(entrar);
		cartao.AddChild(linha);
		_sessoes.AddChild(cartao);
	}

	private void OnEntrarPressed()
	{
		if (_session == null)
		{
			return;
		}

		var professor = new Professor { ip = _sessaoIp };
		_session.usuariosNaRede.Add(professor);
		_session.lobbyIp = _sessaoIp;
		_session.hostName = _sessaoProfessor;
		_session.roomName = _sessaoSala;
		_session.lobbyMode = LobbyMode.Client;

		Error resultado = GetTree().ChangeSceneToFile("res://Scenes/Forms/AlunoConta.tscn");

		if (resultado != Error.Ok)
		{
			GD.PrintErr("Erro ao abrir a tela de conta: " + resultado);
		}
	}

	private void DefinirCarregando(bool carregando)
	{
		_retryButton.Disabled = carregando;
		_retryButton.Text = carregando ? "PROCURANDO..." : "PROCURAR NOVAMENTE";
	}

	private void LimparSessoes()
	{
		foreach (Node filho in _sessoes.GetChildren())
		{
			filho.QueueFree();
		}
	}

	public override void _ExitTree()
	{
		BroadcastUDP.StopListening();
	}

	private struct Dados
	{
		public string Status { get; set; }
		public string Message { get; set; }
		public string Room { get; set; }
	}
}
