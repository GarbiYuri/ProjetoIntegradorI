using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Game.Scripts;
using Game.Scripts.Games;
using Models;
using Service;

/// <summary>
/// Lobby principal (top-down) do Hackoon Game Hub.
///
/// Professor (host): não entra como personagem; vê o painel de gerenciamento
/// com os alunos conectados e pode encerrar a sessão.
/// Aluno (cliente): entra com seu personagem, que aparece para todos.
/// </summary>
public partial class LobbyPrincipal : Node2D
{
	private const int Port = 7777;
	private const int MaxPlayers = 16;

	[Export] public PackedScene playerScene { get; set; }
	[Export] public MultiplayerSpawner multiplayerSpawner { get; set; }
	[Export] public Rect2 playerBounds { get; set; } = new Rect2(-560, -300, 1120, 600);

	private ENetMultiplayerPeer _peer;
	private SessionManager _session;
	private Node _playersRoot;
	private Marker2D _spawnPoint;

	private bool _started;
	private bool _autoRetry;
	private string _joinIp = "127.0.0.1";

	private Control _hud;
	private Control _startOverlay;
	private Control _dashboard;
	private Label _roomLabel;
	private Label _statusLabel;
	private Label _playersLabel;
	private Label _messageLabel;
	private Label _hintLabel;
	private Control _interactPrompt;
	private Label _interactLabel;
	private LineEdit _ipEdit;
	private Timer _retryTimer;

	private Camera2D _hostCamera;
	private Button _painelButton;
	private Label _dashboardRoom;
	private Label _dashboardStatus;
	private Label _dashboardCount;
	private Label _dashboardEmpty;
	private VBoxContainer _dashboardPlayers;
	private VBoxContainer _dashboardJogos;
	private Label _dashboardJogosEmpty;
	private VBoxContainer _dashboardCatalogo;
	private Label _dashboardResumo;
	private Node _jogosRoot;

	// ---------------------------------------------------------------------
	// Ciclo de jogos (catálogo + portas + overlay)
	// ---------------------------------------------------------------------
	private GameOverlay _gameOverlay;
	private Control _gameSessionRoot;
	private Label _jogoStatus;
	private string _placarJogoAtual = string.Empty;
	private int _ultimoJogadoresPublicados = -1;
	private float _timerPublicacaoJogo;

	// ---------------------------------------------------------------------
	// Casinhas físicas no lobby (uma por jogo do catálogo)
	// ---------------------------------------------------------------------
	private readonly List<CasaLobby> _casas = new();
	private GameInfo? _casaProxima;

	// ---------------------------------------------------------------------
	// HUD de alunos online (Tab)
	// ---------------------------------------------------------------------
	private Control _onlineOverlay;
	private VBoxContainer _onlineList;
	private Label _onlineTitle;
	private bool _onlineVisivel;
	private bool _onlineFixo;
	private float _timerOnline;

	private readonly List<Jogo> _jogos = new();
	private List<AlunoOnline> _alunosConectados = new();

	// Conexão interna: API de multiplayer separada, aplicada só na raiz do
	// overlay de jogos (GameSessionRoot) — o lobby continua na conexão principal.
	private MultiplayerApi? _apiJogo;
	private ENetMultiplayerPeer? _peerJogo;
	private int _minhaPortaJogo;
	private int _ultimoCountJogo = -1;
	private Jogo? _jogoConectado;

	private bool _jogoIniciado;
	private float _timerAlunos;
	private string _dashboardSignature = string.Empty;

	public override void _Ready()
	{
		_session = GetNodeOrNull<SessionManager>("/root/SessionManager");
		_playersRoot = GetNode<Node>("World/Players");
		_spawnPoint = GetNode<Marker2D>("World/SpawnPoint");
		_retryTimer = GetNode<Timer>("RetryTimer");
		_hostCamera = GetNode<Camera2D>("HostCamera");

		_hud = GetNode<Control>("%Hud");
		_startOverlay = GetNode<Control>("%StartOverlay");
		_dashboard = GetNode<Control>("%Dashboard");
		_painelButton = GetNode<Button>("%PainelButton");
		_roomLabel = GetNode<Label>("%RoomLabel");
		_statusLabel = GetNode<Label>("%StatusLabel");
		_playersLabel = GetNode<Label>("%PlayersLabel");
		_messageLabel = GetNode<Label>("%MessageLabel");
		_hintLabel = GetNode<Label>("%HintLabel");
		_interactPrompt = GetNode<Control>("%InteractPrompt");
		_interactLabel = GetNode<Label>("%InteractLabel");
		_ipEdit = GetNode<LineEdit>("%IpEdit");

		_dashboardRoom = GetNode<Label>("%DashboardRoom");
		_dashboardStatus = GetNode<Label>("%DashboardStatus");
		_dashboardCount = GetNode<Label>("%DashboardCount");
		_dashboardEmpty = GetNode<Label>("%DashboardEmpty");
		_dashboardPlayers = GetNode<VBoxContainer>("%DashboardPlayers");
		_dashboardJogos = GetNode<VBoxContainer>("%DashboardJogos");
		_dashboardJogosEmpty = GetNode<Label>("%DashboardJogosEmpty");
		_dashboardCatalogo = GetNode<VBoxContainer>("%DashboardCatalogo");
		_dashboardResumo = GetNode<Label>("%DashboardResumo");

		// As mensagens de jogo usam a linha de status discreta do HUD.
		_jogoStatus = _statusLabel;
		_interactPrompt.Visible = false;
		_onlineOverlay = GetNode<Control>("%OnlineOverlay");
		_onlineList = GetNode<VBoxContainer>("%OnlineList");
		_onlineTitle = GetNode<Label>("%OnlineTitle");
		_onlineOverlay.Visible = false;

		// Overlay que hospeda os minigames em tela cheia.
		_gameOverlay = GetNode<GameOverlay>("%GameOverlay");
		_gameOverlay.Fechado += OnJogoFechado;
		_gameSessionRoot = _gameOverlay.GetNode<Control>("GameSessionRoot");

		GetNode<Button>("%SoloButton").Pressed += OnSoloPressed;
		GetNode<Button>("%HostButton").Pressed += OnHostPressed;
		GetNode<Button>("%JoinButton").Pressed += OnJoinPressed;
		GetNode<Button>("%LeaveButton").Pressed += OnLeavePressed;
		GetNode<Button>("%BackButton").Pressed += OnLeavePressed;
		GetNode<Button>("%EndSessionButton").Pressed += OnEndSessionPressed;
		GetNode<Button>("%DashboardLeaveButton").Pressed += OnLeavePressed;
		GetNode<Button>("%IniciarJogoButton").Pressed += OnIniciarJogoPressed;
		GetNode<Button>("%PainelButton").Pressed += OnPainelPressed;

		// Raiz onde os minigames entram: fica dentro do overlay e recebe uma
		// API de multiplayer própria por partida.
		_jogosRoot = _gameSessionRoot;

		// Câmera única de mapa inteiro (o personagem não liga a câmera dele).
		AjustarCameraMapa();
		GetViewport().SizeChanged += AjustarCameraMapa;
		AtualizarCatalogo();
		ConstruirCasasDoLobby();

		if (multiplayerSpawner != null)
		{
			multiplayerSpawner.SpawnFunction = Callable.From<Variant, Node>(CreatePlayer);
		}
		else
		{
			GD.PrintErr("[LOBBY] MultiplayerSpawner não foi ligado no Inspetor!");
		}

		Multiplayer.PeerConnected += OnPeerConnected;
		Multiplayer.PeerDisconnected += OnPeerDisconnected;
		Multiplayer.ConnectedToServer += OnConnectedToServer;
		Multiplayer.ConnectionFailed += OnConnectionFailed;
		Multiplayer.ServerDisconnected += OnServerDisconnected;
		_retryTimer.Timeout += OnRetryTimeout;

		ApplySessionInfo();

		// Atalhos de desenvolvimento (sem rede/banco para o solo).
		// Uso:
		//   godot --path . res://Scenes/Personagem/LobbyPrincipal.tscn -- --lobby-solo
		//   godot --path . res://Scenes/Personagem/LobbyPrincipal.tscn -- --lobby-host
		//   godot --path . res://Scenes/Personagem/LobbyPrincipal.tscn -- --lobby-join=127.0.0.1
		foreach (string argument in OS.GetCmdlineUserArgs())
		{
			if (argument == "--lobby-solo")
			{
				_startOverlay.Visible = false;
				StartSolo();
				ProcessarAtalhosDeJogo();
				return;
			}

			if (argument == "--lobby-host")
			{
				_startOverlay.Visible = false;
				StartHost();

				// Atalho de teste: abre o lobby e dispara o INICIAR JOGO.
				// "--lobby-iniciar" dispara na hora; "--lobby-iniciar=3" espera 3 s.
				foreach (string extra in OS.GetCmdlineUserArgs())
				{
					if (extra.StartsWith("--lobby-iniciar"))
					{
						ExecutarComAtraso(LerAtraso(extra), OnIniciarJogoPressed);
					}
				}

				ProcessarAtalhosDeJogo();
				return;
			}

			if (argument.StartsWith("--lobby-join="))
			{
				_startOverlay.Visible = false;
				_autoRetry = true;
				StartClient(argument.Substring("--lobby-join=".Length));
				ProcessarAtalhosDeJogo();
				return;
			}
		}

		HandleAutoStart();
	}

	/// <summary>
	/// Atalhos de teste compartilhados por solo/host/cliente:
	/// "--lobby-criar-jogo[=SEGUNDOS]" abre uma porta com o primeiro jogo,
	/// "--lobby-entrar-jogo[=SEGUNDOS]" entra na primeira porta aberta e
	/// "--lobby-jogos[=SEGUNDOS]" abre o painel de jogos do HUD.
	/// </summary>
	private void ProcessarAtalhosDeJogo()
	{
		foreach (string extra in OS.GetCmdlineUserArgs())
		{
			if (extra.StartsWith("--lobby-criar-jogo"))
			{
				ExecutarComAtraso(LerAtraso(extra), OnCriarJogoPressed);
			}
			else if (extra.StartsWith("--lobby-entrar-jogo"))
			{
				ExecutarComAtraso(LerAtraso(extra), EntrarNoPrimeiroJogo);
			}
			else if (extra.StartsWith("--lobby-online") || extra.StartsWith("--lobby-jogos"))
			{
				ExecutarComAtraso(LerAtraso(extra), MostrarOnlineFixo);
			}
			else if (extra.StartsWith("--lobby-perto-casa"))
			{
				ExecutarComAtraso(LerAtraso(extra), IrParaPrimeiraCasa);
			}
			else if (extra.StartsWith("--lobby-casinha"))
			{
				ExecutarComAtraso(LerAtraso(extra), EntrarNaPrimeiraCasa);
			}
		}
	}

	// ---------------------------------------------------------------------
	// Entrada / sessão
	// ---------------------------------------------------------------------

	private void ApplySessionInfo()
	{
		if (_session == null) return;

		if (_session.sala != null && !string.IsNullOrWhiteSpace(_session.sala.name))
		{
			_session.roomName = _session.sala.name;
		}
		else if (!string.IsNullOrWhiteSpace(_session.hostName))
		{
			_session.roomName = $"Lobby de {_session.hostName}";
		}

		if (string.IsNullOrWhiteSpace(_session.playerName) || _session.playerName == "Jogador")
		{
			if (_session.usuario != null && !string.IsNullOrWhiteSpace(_session.usuario.name))
			{
				_session.playerName = _session.usuario.name;
			}
		}

		_roomLabel.Text = string.IsNullOrWhiteSpace(_session.roomName) ? "Lobby" : _session.roomName;
		_dashboardRoom.Text = _roomLabel.Text;
		_ipEdit.Text = string.IsNullOrWhiteSpace(_session.lobbyIp) ? "127.0.0.1" : _session.lobbyIp;
		_joinIp = _ipEdit.Text;
	}

	private void HandleAutoStart()
	{
		LobbyMode mode = _session?.lobbyMode ?? LobbyMode.None;

		switch (mode)
		{
			case LobbyMode.Solo:
				_startOverlay.Visible = false;
				CallDeferred(nameof(StartSolo));
				break;

			case LobbyMode.Host:
				_startOverlay.Visible = false;
				CallDeferred(nameof(StartHost));
				break;

			case LobbyMode.Client:
				_startOverlay.Visible = false;
				_autoRetry = true;
				CallDeferred(nameof(StartClientFromSession));
				break;

			default:
				_startOverlay.Visible = true;
				_hud.Visible = false;
				_dashboard.Visible = false;
				SetMessage("Escolha como entrar no lobby.");
				break;
		}
	}

	// ---------------------------------------------------------------------
	// Botões
	// ---------------------------------------------------------------------

	private void OnSoloPressed()
	{
		StartSolo();
	}

	private void OnHostPressed()
	{
		StartHost();
	}

	private void OnJoinPressed()
	{
		_autoRetry = false;
		StartClient(_ipEdit.Text);
	}

	private void OnLeavePressed()
	{
		LeaveLobby();
	}

	private void OnEndSessionPressed()
	{
		// Fecha a conexão com todos os alunos e volta ao menu.
		LeaveLobby();
	}

	private void OnIniciarJogoPressed()
	{
		// Só o professor (servidor) inicia o jogo; todos recebem o estado
		// pelo RPC abaixo (CallLocal inclui o próprio servidor).
		if (!Multiplayer.IsServer())
		{
			return;
		}

		Rpc(nameof(AplicarModoJogo));
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, CallLocal = true)]
	private void AplicarModoJogo()
	{
		_jogoIniciado = true;

		// Fecha o painel: todos (professor incluso, pela câmera do host)
		// ficam no lobby, onde o catálogo de jogos abre as portas.
		ShowHud();
		SetStatus("JOGO INICIADO • abra uma porta no catálogo 🎮");
		UpdatePlayersLabel();

		GD.Print("[LOBBY] Jogo iniciado. Portas de jogo são abertas pelo catálogo.");
	}

	/// <summary>Executa uma ação depois de um tempo (atalhos de teste).</summary>
	private async void ExecutarComAtraso(double atraso, Action acao)
	{
		if (atraso > 0)
		{
			await ToSignal(GetTree().CreateTimer(atraso), SceneTreeTimer.SignalName.Timeout);
		}

		if (!IsInstanceValid(this) || !IsInsideTree())
		{
			return;
		}

		acao();
	}

	/// <summary>Lê o atraso em segundos de um argumento "--algo=3".</summary>
	private static double LerAtraso(string argumento)
	{
		string[] partes = argumento.Split('=', 2);

		if (partes.Length == 2 && double.TryParse(partes[1], out double segundos))
		{
			return segundos;
		}

		return 0;
	}

	private void OnPainelPressed()
	{
		// Volta para o painel de gerenciamento (professor apenas).
		if (Multiplayer.IsServer())
		{
			ShowHostDashboard();
		}
	}

	// ---------------------------------------------------------------------
	// Inicialização dos modos
	// ---------------------------------------------------------------------

	private void StartSolo()
	{
		if (_started) return;

		Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
		_started = true;
		AjustarCameraMapa();

		if (_session != null) _session.lobbyMode = LobbyMode.Solo;

		ShowHud();
		SetStatus("Modo solo • sem conexão");
		SpawnPlayer(1);
	}

	private void StartHost()
	{
		if (_started) return;

		_autoRetry = false;
		_retryTimer.Stop();

		_peer = new ENetMultiplayerPeer();
		Error error = _peer.CreateServer(Port, MaxPlayers);
		GD.Print($"[LOBBY] CreateServer({Port}) => {error}");

		if (error != Error.Ok)
		{
			_peer = null;
			ShowStartOverlay($"Não foi possível hospedar a sala (erro {error}). A porta {Port} já está em uso?");
			return;
		}

		Multiplayer.MultiplayerPeer = _peer;
		_started = true;
		AjustarCameraMapa();

		if (_session != null) _session.lobbyMode = LobbyMode.Host;

		// Sessão sempre visível para os alunos: o broadcast continua enquanto
		// o professor estiver no lobby (painel ou jogo), então os alunos podem
		// entrar a qualquer momento e já aparecem no lobby.
		// O anúncio também leva a lista de alunos conectados (a tela do aluno
		// mostra quem está na sala) e o id da sala (para vincular a conta).
		ProfessorService.SendMessage(
			_session?.usuario?.name ?? "Professor",
			string.IsNullOrWhiteSpace(_session?.roomName) ? "Lobby" : _session.roomName,
			_session?.sala?.id ?? _session?.salaId ?? 0,
			ListarAlunosConectados);

		ShowHostDashboard();
		AtualizarDashboard(true);
	}

	private void StartClientFromSession()
	{
		StartClient(string.IsNullOrWhiteSpace(_session?.lobbyIp) ? "127.0.0.1" : _session.lobbyIp);
	}

	private void StartClient(string ip)
	{
		if (_started) return;

		if (string.IsNullOrWhiteSpace(ip))
		{
			ip = "127.0.0.1";
		}

		_joinIp = ip.Trim();

		_peer = new ENetMultiplayerPeer();
		Error error = _peer.CreateClient(_joinIp, Port);

		if (error != Error.Ok)
		{
			_peer = null;
			if (_autoRetry)
			{
				SetStatus("Falha ao iniciar a conexão. Tentando novamente...");
				_retryTimer.Start();
			}
			else
			{
				ShowStartOverlay($"Não foi possível iniciar a conexão (erro {error}).");
			}
			return;
		}

		Multiplayer.MultiplayerPeer = _peer;
		_started = true;
		AjustarCameraMapa();

		if (_session != null)
		{
			_session.lobbyMode = LobbyMode.Client;
			_session.lobbyIp = _joinIp;
		}

		ShowHud();
		SetStatus($"Conectando em {_joinIp}:{Port}...");
	}

	private void LeaveLobby()
	{
		_autoRetry = false;
		_retryTimer.Stop();
		AlunoService.StopSendingPersona();
		ProfessorService.StopMessage();

		foreach (Node player in _playersRoot.GetChildren())
		{
			player.QueueFree();
		}

		ResetPeer();
		GetTree().ChangeSceneToFile("res://Scenes/InitialScene.tscn");
	}

	private void ResetPeer()
	{
		_started = false;
		Multiplayer.MultiplayerPeer = null;

		if (_peer != null)
		{
			_peer.Close();
			_peer = null;
		}
	}

	// ---------------------------------------------------------------------
	// Eventos de rede
	// ---------------------------------------------------------------------

	private void OnPeerConnected(long id)
	{
		GD.Print($"[LOBBY] Peer conectado: {id}");

		if (Multiplayer.IsServer())
		{
			SpawnPlayer(id);

			// Quem entrar depois de o jogo ter começado também cai
			// direto no modo jogo (sincroniza o estado para o recém-chegado).
			if (_jogoIniciado)
			{
				CallDeferred(nameof(EnviarModoJogoPara), id);
			}

			// Quem entra depois recebe a lista de jogos que já existem.
			CallDeferred(nameof(EnviarJogosPara), id);
		}
	}

	private void EnviarJogosPara(long id)
	{
		if (!Multiplayer.IsServer())
		{
			return;
		}

		RpcId(id, nameof(ReceberJogos), JsonSerializer.Serialize(_jogos));
	}

	private void EnviarModoJogoPara(long id)
	{
		if (_jogoIniciado && Multiplayer.IsServer())
		{
			RpcId(id, nameof(AplicarModoJogo));
		}
	}

	private void OnPeerDisconnected(long id)
	{
		GD.Print($"[LOBBY] Peer desconectado: {id}");

		string nome = ObterNomeDoPeer(id);

		Node player = _playersRoot.GetNodeOrNull(id.ToString());
		player?.QueueFree();

		// Quem caiu do lobby não pode deixar uma porta órfã na sala.
		if (Multiplayer.IsServer())
		{
			List<int> portas = _jogos
				.Where(j => j.criador == nome)
				.Select(j => j.porta)
				.ToList();

			foreach (int porta in portas)
			{
				_jogos.RemoveAll(j => j.porta == porta);
				Rpc(nameof(ReceberJogoEncerrado), porta);
			}

			if (portas.Count > 0)
			{
				DivulgarJogos();
			}
		}

		AtualizarDashboard(true);
	}

	private void OnConnectedToServer()
	{
		_autoRetry = false;
		_retryTimer.Stop();
		AlunoService.StopSendingPersona();
		SetStatus("Conectado • aguardando personagem...");
	}

	private void OnConnectionFailed()
	{
		ResetPeer();

		if (_autoRetry)
		{
			SetStatus("O professor ainda não abriu o lobby. Tentando novamente...");
			_retryTimer.Start();
		}
		else
		{
			ShowStartOverlay("Não foi possível conectar. Confira o IP e se a sala foi hospedada.");
		}
	}

	private void OnServerDisconnected()
	{
		_autoRetry = false;
		_retryTimer.Stop();
		// Professor fechou a conexão: para o envio do persona e volta à tela inicial.
		AlunoService.StopSendingPersona();
		ResetPeer();
		ShowStartOverlay("O professor encerrou a conexão.");
	}

	private void OnRetryTimeout()
	{
		if (!_autoRetry || _started) return;

		SetStatus($"Tentando conectar em {_joinIp}:{Port}...");
		StartClient(_joinIp);
	}

	// ---------------------------------------------------------------------
	// Criação e atualização do personagem
	// ---------------------------------------------------------------------

	private void SpawnPlayer(long id)
	{
		if (multiplayerSpawner == null) return;

		if (!Multiplayer.IsServer())
		{
			return;
		}

		var data = new Godot.Collections.Dictionary { { "id", id } };
		Node spawnedPlayer = multiplayerSpawner.Spawn(data);

		if (spawnedPlayer == null)
		{
			GD.PrintErr($"[LOBBY] Falha ao criar personagem {id}.");
		}
	}

	/// <summary>
	/// Roda no servidor e em todos os clientes no momento em que o personagem entra na rede.
	/// A autoridade precisa ser definida antes do nó entrar na árvore.
	/// </summary>
	private Node CreatePlayer(Variant data)
	{
		if (playerScene == null)
		{
			GD.PrintErr("[LOBBY] playerScene não foi atribuída no Inspetor!");
			return null;
		}

		long id = 1;
		if (data.VariantType == Variant.Type.Dictionary)
		{
			Godot.Collections.Dictionary dictionary = data.AsGodotDictionary();
			if (dictionary.TryGetValue("id", out Variant idValue))
			{
				id = (long)idValue;
			}
		}

		var player = playerScene.Instantiate<Personagem>();
		player.Name = id.ToString();
		player.SetMultiplayerAuthority((int)id);
		player.MovementBounds = playerBounds;

		// No lobby todos usam a câmera de mapa inteiro.
		player.UserCamera = false;

		MultiplayerSynchronizer synchronizer = player.GetNodeOrNull<MultiplayerSynchronizer>("MultiplayerSynchronizer");
		if (synchronizer != null)
		{
			synchronizer.SetMultiplayerAuthority((int)id);
		}

		// Distribui os jogadores em volta do ponto de nascimento para não ficarem
		// exatamente um em cima do outro.
		int column = (int)(id % 5) - 2;
		int row = (int)((id / 5) % 3);
		player.Position = _spawnPoint.Position + new Vector2(column * 44, row * 36);

		return player;
	}

	// ---------------------------------------------------------------------
	// Portas de jogo: cada partida é uma conexão ENet separada (7778+)
	//
	// O jogador abre a porta pelo catálogo; o servidor do lobby registra a
	// porta e divulga a lista para a sala. O professor acompanha pelo painel.
	// ---------------------------------------------------------------------

	/// <summary>
	/// Alunos conectados (copia pronta para o anúncio). É montada na thread
	/// principal e só trocada — nunca alterada por fora — porque o anúncio
	/// roda em uma thread de rede.
	/// </summary>
	private List<AlunoOnline> ListarAlunosConectados()
	{
		return new List<AlunoOnline>(_alunosConectados);
	}

	/// <summary>Atualiza a lista de alunos conectados (thread principal).</summary>
	private void AtualizarAlunosConectados()
	{
		var lista = new List<AlunoOnline>();

		foreach (Node filho in _playersRoot.GetChildren())
		{
			if (filho is not Personagem personagem)
			{
				continue;
			}

			lista.Add(new AlunoOnline(personagem.PlayerName, personagem.PlayerNick));
		}

		_alunosConectados = lista;
	}

	private void OnCriarJogoPressed()
	{
		// Atalho de desenvolvimento e botão "ABRIR JOGO": abre uma porta com o
		// primeiro jogo do catálogo (o aluno escolhe o jogo nos cartões).
		GameInfo? info = GameRegistry.Jogos.FirstOrDefault();

		if (info == null)
		{
			_jogoStatus.Text = "Nenhum jogo cadastrado no catálogo.";
			return;
		}

		AbrirJogoDoCatalogo(info);
	}

	/// <summary>
	/// Sobe o servidor da partida em uma porta livre (7778 em diante)
	/// e devolve a porta usada (0 = nenhuma porta livre).
	/// </summary>
	private int CriarServidorInterno()
	{
		if (_jogosRoot == null || !_jogosRoot.IsInsideTree())
		{
			return 0;
		}

		for (int tentativa = 0; tentativa < 10; tentativa++)
		{
			int porta = 7778 + tentativa;
			var peer = new ENetMultiplayerPeer();
			Error erro = peer.CreateServer(porta, 16);

			if (erro != Error.Ok)
			{
				peer.Dispose();
				continue;
			}

			var api = MultiplayerApi.CreateDefaultInterface();
			api.MultiplayerPeer = peer;

			// A API do jogo vale só para a raiz do overlay: a conexão do
			// lobby (principal) continua intacta.
			GetTree().SetMultiplayer(api, _jogosRoot.GetPath());

			_apiJogo = api;
			_peerJogo = peer;
			_minhaPortaJogo = porta;
			_ultimoJogadoresPublicados = 1;
			_placarJogoAtual = string.Empty;

			GD.Print($"[JOGO] Porta aberta na {porta}.");
			return porta;
		}

		return 0;
	}

	/// <summary>
	/// Abre uma porta para o jogo escolhido no catálogo. Quem abre vira o
	/// dono da porta (host da partida); o lobby registra e divulga a porta.
	/// O jogador NÃO precisa de conta: convidados também podem abrir.
	/// </summary>
	private void AbrirJogoDoCatalogo(GameInfo info)
	{
		if (!_started)
		{
			_jogoStatus.Text = "Entre no lobby antes de abrir um jogo.";
			return;
		}

		if (_gameOverlay.Aberto || _apiJogo != null)
		{
			_jogoStatus.Text = "Você já está em uma partida. Feche-a antes de abrir outra.";
			return;
		}

		if (!GameRegistry.CenaExiste(info))
		{
			_jogoStatus.Text = $"A cena do jogo \"{info.Nome}\" não foi encontrada.";
			GD.PrintErr($"[JOGO] Cena ausente: {info.Cena}");
			return;
		}

		string nomePorta = info.Nome;

		int porta = CriarServidorInterno();

		if (porta == 0)
		{
			_jogoStatus.Text = "Não foi possível abrir a porta do jogo (portas 7778+ ocupadas).";
			return;
		}

		var jogo = new Jogo(
			nomePorta,
			_session?.sala?.id ?? (_session?.salaId > 0 ? _session.salaId : null),
			_session?.usuario?.id,
			_session?.playerName ?? "Jogador",
			MeuIp(),
			porta,
			info.Id);

		jogo.jogadores = 1;
		_jogoConectado = jogo;

		if (Multiplayer.IsServer())
		{
			RegistrarJogoServidor(jogo);
		}
		else
		{
			// O servidor do lobby divulga a porta para toda a sala.
			RpcId(1, nameof(RequisitarJogo), jogo.nome, porta, jogo.host_ip, info.Id);
		}

		_ = RegistrarJogoNoBanco(jogo);

		_jogoStatus.Text = $"Porta {porta} aberta para {info.Nome}. Os colegas já podem entrar.";
		AbrirCenaDoJogo(info, porta, souHost: true);
	}

	/// <summary>Grava a porta no banco (quando o banco está disponível).</summary>
	private async System.Threading.Tasks.Task RegistrarJogoNoBanco(Jogo jogo)
	{
		try
		{
			await JogoService.Criar(
				jogo.nome,
				jogo.id_sala,
				jogo.id_aluno,
				jogo.criador,
				jogo.host_ip,
				jogo.porta,
				jogo.gameKey);
		}
		catch (Exception e)
		{
			GD.PrintErr("[JOGO] Não foi possível registrar a porta no banco: " + e.Message);
		}
	}

	/// <summary>
	/// Instancia a cena do jogo e entrega o GameContexto. O overlay cuida de
	/// mostrar em tela cheia e devolver o controle ao lobby ao fechar.
	/// </summary>
	private void AbrirCenaDoJogo(GameInfo info, int porta, bool souHost)
	{
		if (!ResourceLoader.Exists(info.Cena))
		{
			_jogoStatus.Text = $"Cena do jogo \"{info.Nome}\" não encontrada.";
			FecharConexaoJogo();
			return;
		}

		PackedScene? cena = GD.Load<PackedScene>(info.Cena);

		if (cena == null || cena.Instantiate() is not MinigameBase jogo)
		{
			_jogoStatus.Text = $"O jogo \"{info.Nome}\" não segue o contrato de minigame.";
			GD.PrintErr($"[JOGO] A cena {info.Cena} não herda de MinigameBase.");
			FecharConexaoJogo();
			return;
		}

		jogo.StatusMudou += mensagem => SetStatus(mensagem);
		jogo.JogoTerminou += resumo => PublicarResultadoJogo(porta, resumo);
		jogo.PlacarMudou += resumo => PublicarResultadoJogo(porta, resumo);

		var contexto = new GameContexto
		{
			Jogo = info,
			SouHost = souHost,
			Porta = porta,
			Jogador = _session?.playerName ?? "Jogador",
			Nick = _session?.playerNick ?? string.Empty,
			Sala = _session?.roomName ?? string.Empty,
			SalaId = _session?.salaId ?? 0,
			Sair = () => _gameOverlay.Fechar(),
			Avisar = mensagem => SetStatus(mensagem)
		};

		TravarPersonagem(true);
		_gameOverlay.Abrir(info, jogo, contexto);
	}

	/// <summary>
	/// Publica o placar (parcial ou final) no painel do professor. Só o dono
	/// da porta envia, e apenas quando o resumo muda de verdade.
	/// </summary>
	private void PublicarResultadoJogo(int porta, string resumo)
	{
		if (porta <= 0 || porta != _minhaPortaJogo)
		{
			return;
		}

		string texto = resumo ?? string.Empty;

		if (texto == _placarJogoAtual)
		{
			return;
		}

		_placarJogoAtual = texto;
		int jogadores = Math.Max(1, (_apiJogo?.GetPeers().Length ?? 0) + 1);
		PublicarProgressoJogo(porta, jogadores, _placarJogoAtual);
	}

	/// <summary>Manda para o servidor do lobby quantos jogadores/placar a porta tem.</summary>
	private void PublicarProgressoJogo(int porta, int jogadores, string placar)
	{
		if (porta <= 0)
		{
			return;
		}

		if (Multiplayer.IsServer())
		{
			AtualizarPortaJogoServidor(porta, jogadores, placar);
		}
		else
		{
			RpcId(1, nameof(AtualizarPortaJogo), porta, jogadores, placar ?? string.Empty);
		}
	}

	/// <summary>Servidor do lobby: atualiza a porta e divulga a lista nova.</summary>
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void AtualizarPortaJogo(int porta, int jogadores, string placar)
	{
		if (Multiplayer.IsServer())
		{
			AtualizarPortaJogoServidor(porta, jogadores, placar);
		}
	}

	private void AtualizarPortaJogoServidor(int porta, int jogadores, string placar)
	{
		Jogo? jogo = _jogos.FirstOrDefault(j => j.porta == porta);

		if (jogo == null)
		{
			return;
		}

		jogo.jogadores = Math.Max(1, jogadores);
		jogo.placar = placar ?? string.Empty;
		DivulgarJogos();
	}

	private void RegistrarJogoServidor(Jogo jogo)
	{
		_jogos.RemoveAll(j => j.porta == jogo.porta || j.criador == jogo.criador);
		_jogos.Add(jogo);
		DivulgarJogos();
	}

	private void RemoverJogoServidor(int porta)
	{
		if (_jogos.RemoveAll(j => j.porta == porta) > 0)
		{
			DivulgarJogos();
		}
	}

	// ---------------------------------------------------------------------
	// Catálogo de jogos e HUD
	// ---------------------------------------------------------------------

	/// <summary>Preenche o catálogo do professor a partir do registro de jogos.</summary>
	private void AtualizarCatalogo()
	{
		PreencherCatalogo(_dashboardCatalogo, professor: true);
	}

	private void PreencherCatalogo(VBoxContainer? alvo, bool professor)
	{
		if (alvo == null)
		{
			return;
		}

		LimparFilhos(alvo);

		foreach (GameInfo info in GameRegistry.Jogos)
		{
			var cartao = UIHelpers.Cartao();
			var linha = UIHelpers.Linha(10);

			var textos = new VBoxContainer
			{
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
			};
			textos.AddThemeConstantOverride("separation", 2);

			Label titulo = UIHelpers.Texto($"{info.Emblema}  {info.Nome}", professor ? 14 : 15);
			titulo.AddThemeColorOverride("font_color", info.Cor.Lightened(0.2f));
			textos.AddChild(titulo);

			textos.AddChild(UIHelpers.TextoSuaveLabel(info.Descricao, 11));

			string faixa = info.MinJogadores == info.MaxJogadores
				? $"{info.MaxJogadores} jogador(es)"
				: $"{info.MinJogadores}-{info.MaxJogadores} jogadores";
			textos.AddChild(UIHelpers.TextoSuaveLabel($"{info.Categoria} • {faixa}", 10));

			linha.AddChild(textos);

			var abrir = new Button
			{
				Text = professor ? "ABRIR" : "ABRIR PORTA",
				FocusMode = Control.FocusModeEnum.None,
				CustomMinimumSize = new Vector2(professor ? 78 : 112, 0)
			};
			abrir.AddThemeFontSizeOverride("font_size", professor ? 12 : 13);
			abrir.Disabled = !GameRegistry.CenaExiste(info);
			abrir.Pressed += () => AbrirJogoDoCatalogo(info);
			linha.AddChild(abrir);

			cartao.AddChild(linha);
			alvo.AddChild(cartao);
		}
	}

	// ---------------------------------------------------------------------
	// Casinhas físicas dos jogos
	// ---------------------------------------------------------------------

	private sealed class CasaLobby
	{
		public GameInfo Info { get; }
		public Node2D Raiz { get; }
		public Line2D Destaque { get; }

		public CasaLobby(GameInfo info, Node2D raiz, Line2D destaque)
		{
			Info = info;
			Raiz = raiz;
			Destaque = destaque;
		}
	}

	/// <summary>
	/// Constrói uma casinha no salão para cada jogo registrado. Adicionar um
	/// jogo novo no <see cref="GameRegistry"/> já cria a casa automaticamente.
	/// </summary>
	private void ConstruirCasasDoLobby()
	{
		Node2D? raiz = GetNodeOrNull<Node2D>("World/Jogos");

		if (raiz == null)
		{
			return;
		}

		LimparFilhos(raiz);
		_casas.Clear();

		int indice = 0;

		foreach (GameInfo info in GameRegistry.Jogos)
		{
			if (!GameRegistry.CenaExiste(info))
			{
				continue;
			}

			Vector2 posicao = info.PosicaoNoLobby ?? PosicaoPadraoCasa(indice);
			Node2D casa = CriarCasa(info, posicao, out Line2D destaque);
			raiz.AddChild(casa);
			_casas.Add(new CasaLobby(info, casa, destaque));
			indice++;
		}
	}

	private static Vector2 PosicaoPadraoCasa(int indice)
	{
		Vector2[] posicoes =
		{
			new(-400, 96),   // esquerda do salão
			new(400, 96),    // direita do salão
			new(-395, -165), // canto superior esquerdo
			new(395, -165),  // canto superior direito
			new(-205, -225),
			new(205, -225),
			new(-205, 235),
			new(205, 235)
		};

		return posicoes[indice % posicoes.Length];
	}

	private static Node2D CriarCasa(GameInfo info, Vector2 posicao, out Line2D destaque)
	{
		var casa = new Node2D
		{
			Name = $"Casa_{info.Id}",
			Position = posicao,
			ZIndex = 2
		};

		Color parede = new(0.93f, 0.87f, 0.73f);
		Color paredeSombra = parede.Darkened(0.14f);
		Color telhado = info.Cor.Darkened(0.28f);
		Color telhadoClaro = info.Cor.Lightened(0.12f);
		Color madeira = new(0.38f, 0.24f, 0.15f);
		Color madeiraClara = new(0.52f, 0.34f, 0.21f);
		Color janela = new(0.68f, 0.88f, 0.98f);
		Color janelaBrilho = new(0.88f, 0.97f, 1f);

		// Anel de destaque: aparece quando o aluno chega perto da porta.
		var anel = new Vector2[40];

		for (int i = 0; i < anel.Length; i++)
		{
			float angulo = Mathf.Tau * i / anel.Length;
			anel[i] = new Vector2(Mathf.Cos(angulo) * 104f, 34f + Mathf.Sin(angulo) * 54f);
		}

		destaque = new Line2D
		{
			Points = anel,
			Closed = true,
			Width = 4f,
			DefaultColor = new Color(1f, 0.886f, 0.502f, 0.95f),
			Visible = false,
			ZIndex = -1
		};
		casa.AddChild(destaque);

		casa.AddChild(Retangulo(-90, -66, 90, 86, new Color(0f, 0f, 0f, 0.24f), new Vector2(8, 8)));
		casa.AddChild(Retangulo(-86, 64, 86, 80, new Color(0.42f, 0.45f, 0.5f)));
		casa.AddChild(Retangulo(-82, -52, 82, 72, parede));
		casa.AddChild(Retangulo(62, -52, 82, 72, paredeSombra));

		// Telhado em perspective simples, com uma faixa iluminada no topo.
		casa.AddChild(Poligono(new[]
		{
			new Vector2(-96, -86), new Vector2(96, -86),
			new Vector2(82, -34), new Vector2(-82, -34)
		}, telhado));

		casa.AddChild(Poligono(new[]
		{
			new Vector2(-96, -86), new Vector2(96, -86),
			new Vector2(90, -72), new Vector2(-90, -72)
		}, telhadoClaro));

		var cumeeira = new Line2D
		{
			Points = new[] { new Vector2(0, -86), new Vector2(0, -34) },
			Width = 3f,
			DefaultColor = telhadoClaro.Lightened(0.15f)
		};
		casa.AddChild(cumeeira);

		// Porta, batente e maçaneta.
		casa.AddChild(Retangulo(-30, 16, 30, 72, madeiraClara));
		casa.AddChild(Retangulo(-24, 21, 24, 72, madeira));
		casa.AddChild(Circulo(new Vector2(14, 48), 4.2f, new Color(1f, 0.84f, 0.32f)));

		// Janelas.
		CriarJanela(casa, new Vector2(-66, 4), new Vector2(28, 28), janela, janelaBrilho);
		CriarJanela(casa, new Vector2(38, 4), new Vector2(28, 28), janela, janelaBrilho);

		// Placa com o nome do jogo e a tecla de interação.
		Label placa = TextoMundo(
			$"{info.Emblema}  {info.Nome.ToUpperInvariant()}",
			17,
			info.Cor.Lightened(0.35f),
			new Vector2(-160, -160),
			new Vector2(320, 28));
		placa.ZIndex = 4;
		casa.AddChild(placa);

		Label instrucao = TextoMundo(
			"Pressione  E  para entrar",
			12,
			UIHelpers.AmareloClaro,
			new Vector2(-160, -132),
			new Vector2(320, 22));
		instrucao.ZIndex = 4;
		casa.AddChild(instrucao);

		return casa;
	}

	private static void CriarJanela(Node2D casa, Vector2 posicao, Vector2 tamanho,
		Color vidro, Color brilho)
	{
		casa.AddChild(Retangulo(
			posicao.X - 3, posicao.Y - 3,
			posicao.X + tamanho.X + 3, posicao.Y + tamanho.Y + 3,
			new Color(0.33f, 0.21f, 0.13f)));

		casa.AddChild(Retangulo(
			posicao.X, posicao.Y,
			posicao.X + tamanho.X, posicao.Y + tamanho.Y,
			vidro));

		casa.AddChild(Retangulo(
			posicao.X + 2, posicao.Y + 2,
			posicao.X + 10, posicao.Y + tamanho.Y - 2,
			brilho));
	}

	private static Polygon2D Retangulo(float esquerda, float topo, float direita,
		float baseY, Color cor, Vector2? deslocamento = null)
	{
		var no = new Polygon2D
		{
			Polygon = new[]
			{
				new Vector2(esquerda, topo), new Vector2(direita, topo),
				new Vector2(direita, baseY), new Vector2(esquerda, baseY)
			},
			Color = cor
		};

		if (deslocamento.HasValue)
		{
			no.Position = deslocamento.Value;
		}

		return no;
	}

	private static Polygon2D Poligono(Vector2[] pontos, Color cor)
	{
		return new Polygon2D { Polygon = pontos, Color = cor };
	}

	private static Polygon2D Circulo(Vector2 centro, float raio, Color cor)
	{
		var pontos = new Vector2[20];

		for (int i = 0; i < pontos.Length; i++)
		{
			float angulo = Mathf.Tau * i / pontos.Length;
			pontos[i] = centro + new Vector2(Mathf.Cos(angulo), Mathf.Sin(angulo)) * raio;
		}

		return new Polygon2D { Polygon = pontos, Color = cor };
	}

	private static Label TextoMundo(string texto, int tamanho, Color cor,
		Vector2 posicao, Vector2 caixa)
	{
		var label = new Label
		{
			Text = texto,
			Position = posicao,
			Size = caixa,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};

		label.AddThemeFontSizeOverride("font_size", tamanho);
		label.AddThemeColorOverride("font_color", cor);
		label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.92f));
		label.AddThemeConstantOverride("outline_size", 6);
		return label;
	}

	/// <summary>Mostra a dica da casinha mais próxima do personagem local.</summary>
	private void AtualizarCasaProxima()
	{
		if (_gameOverlay.Aberto || _startOverlay.Visible || !_hud.Visible)
		{
			EsconderPromptCasa();
			return;
		}

		Personagem? eu = ObterMeuPersonagem();

		if (eu == null)
		{
			EsconderPromptCasa();
			return;
		}

		CasaLobby? maisPerto = null;
		float menorDistancia = 125f;

		foreach (CasaLobby casa in _casas)
		{
			casa.Destaque.Visible = false;

			// A interação acontece na porta, um pouco abaixo do centro da casa.
			Vector2 porta = casa.Raiz.GlobalPosition + new Vector2(0, 36);
			float distancia = eu.GlobalPosition.DistanceTo(porta);

			if (distancia <= menorDistancia)
			{
				menorDistancia = distancia;
				maisPerto = casa;
			}
		}

		if (maisPerto == null)
		{
			EsconderPromptCasa();
			return;
		}

		maisPerto.Destaque.Visible = true;
		_casaProxima = maisPerto.Info;
		_interactLabel.Text = $"Pressione  E  para entrar em {maisPerto.Info.Nome}";
		_interactPrompt.Visible = true;
	}

	private void EsconderPromptCasa()
	{
		_casaProxima = null;
		_interactPrompt.Visible = false;

		foreach (CasaLobby casa in _casas)
		{
			casa.Destaque.Visible = false;
		}
	}

	private Personagem? ObterMeuPersonagem()
	{
		foreach (Node filho in _playersRoot.GetChildren())
		{
			if (filho is Personagem personagem && personagem.IsMultiplayerAuthority())
			{
				return personagem;
			}
		}

		return null;
	}

	/// <summary>
	/// Entrar na casinha: se já existe uma partida desse jogo na sala, o aluno
	/// entra nela; caso contrário, ele abre a porta e convida a turma.
	/// </summary>
	private void EntrarNaCasinha(GameInfo info)
	{
		if (_gameOverlay.Aberto || _apiJogo != null)
		{
			return;
		}

		if (!_started)
		{
			SetStatus("Entre no lobby antes de usar a casinha.");
			return;
		}

		string meuNome = _session?.playerName ?? "Jogador";

		Jogo? partidaAberta = _jogos.FirstOrDefault(j =>
			j.gameKey == info.Id &&
			j.porta != _minhaPortaJogo &&
			!string.Equals(j.criador, meuNome, StringComparison.OrdinalIgnoreCase));

		if (partidaAberta != null)
		{
			EntrarNoJogo(partidaAberta);
			return;
		}

		AbrirJogoDoCatalogo(info);
	}

	// ---------------------------------------------------------------------
	// HUD de alunos online (Tab)
	// ---------------------------------------------------------------------

	private void MostrarOnline()
	{
		if (_startOverlay.Visible || _gameOverlay.Aberto)
		{
			return;
		}

		_onlineVisivel = true;
		_onlineOverlay.Visible = true;
		AtualizarOnline();
	}

	private void EsconderOnline()
	{
		if (_onlineFixo)
		{
			return;
		}

		_onlineVisivel = false;
		_onlineOverlay.Visible = false;
	}

	/// <summary>Atalho de desenvolvimento: deixa o HUD do Tab aberto.</summary>
	private void MostrarOnlineFixo()
	{
		_onlineFixo = true;
		MostrarOnline();
	}

	/// <summary>Atalho de desenvolvimento: leva o aluno para a frente da casinha.</summary>
	private void IrParaPrimeiraCasa()
	{
		if (_casas.Count == 0)
		{
			return;
		}

		Personagem? eu = ObterMeuPersonagem();

		if (eu == null)
		{
			return;
		}

		eu.GlobalPosition = _casas[0].Raiz.GlobalPosition + new Vector2(0, 128);
		AtualizarCasaProxima();
	}

	/// <summary>Atalho de desenvolvimento: entra na primeira casinha.</summary>
	private void EntrarNaPrimeiraCasa()
	{
		IrParaPrimeiraCasa();

		if (_casaProxima != null)
		{
			EntrarNaCasinha(_casaProxima);
		}
	}

	private void AtualizarOnline()
	{
		if (_onlineList == null)
		{
			return;
		}

		LimparFilhos(_onlineList);

		List<Personagem> alunos = _playersRoot.GetChildren()
			.OfType<Personagem>()
			.OrderBy(p => p.PlayerName)
			.ThenBy(p => p.Name.ToString())
			.ToList();

		if (_onlineTitle != null)
		{
			_onlineTitle.Text = alunos.Count == 1
				? "ALUNOS ONLINE • 1"
				: $"ALUNOS ONLINE • {alunos.Count}";
		}

		if (alunos.Count == 0)
		{
			_onlineList.AddChild(UIHelpers.TextoSuaveLabel(
				"Nenhum aluno conectado ainda.", 16));
			return;
		}

		foreach (Personagem aluno in alunos)
		{
			bool souEu = aluno.IsMultiplayerAuthority();
			var cartao = UIHelpers.Cartao(UIHelpers.EstiloCartao(
				new Color(0.12f, 0.15f, 0.21f),
				souEu ? UIHelpers.Amarelo : UIHelpers.BordaSuave,
				10, 8));

			var linha = UIHelpers.Linha(12);

			var ponto = new PanelContainer
			{
				CustomMinimumSize = new Vector2(16, 16),
				SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
			};
			var estiloPonto = new StyleBoxFlat
			{
				BgColor = aluno.SyncColor.A <= 0.01f ? UIHelpers.AmareloClaro : aluno.SyncColor,
				CornerRadiusTopLeft = 8,
				CornerRadiusTopRight = 8,
				CornerRadiusBottomLeft = 8,
				CornerRadiusBottomRight = 8
			};
			ponto.AddThemeStyleboxOverride("panel", estiloPonto);
			linha.AddChild(ponto);

			var textos = UIHelpers.Coluna(1);
			textos.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

			string nome = string.IsNullOrWhiteSpace(aluno.PlayerName) ||
						  aluno.PlayerName == "Jogador"
				? $"Aluno {aluno.Name}"
				: aluno.PlayerName;
			textos.AddChild(UIHelpers.Texto(nome, 17));

			string nick = aluno.PlayerNick?.Trim() ?? string.Empty;

			if (!string.IsNullOrWhiteSpace(nick) &&
				!string.Equals(nick.TrimStart('@'), nome, StringComparison.OrdinalIgnoreCase))
			{
				textos.AddChild(UIHelpers.TextoSuaveLabel("@" + nick.TrimStart('@'), 12));
			}

			linha.AddChild(textos);
			linha.AddChild(UIHelpers.SeloStatus(souEu ? "VOCÊ" : "ONLINE"));

			cartao.AddChild(linha);
			_onlineList.AddChild(cartao);
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey tecla && tecla.Keycode == Key.Tab)
		{
			if (_startOverlay.Visible || _gameOverlay.Aberto)
			{
				return;
			}

			if (tecla.Pressed && !tecla.Echo)
			{
				MostrarOnline();
			}
			else if (!tecla.Pressed)
			{
				EsconderOnline();
			}

			GetViewport().SetInputAsHandled();
			return;
		}

		if (_gameOverlay.Aberto || _startOverlay.Visible)
		{
			return;
		}

		if (@event is InputEventKey acao && acao.Pressed && !acao.Echo &&
			(acao.Keycode == Key.E || acao.Keycode == Key.Space) &&
			_casaProxima != null)
		{
			EntrarNaCasinha(_casaProxima);
			GetViewport().SetInputAsHandled();
		}
	}

	// ---------------------------------------------------------------------
	// Câmera de mapa inteiro e trava do personagem
	// ---------------------------------------------------------------------

	/// <summary>Enquadra o lobby inteiro, sem o zoom do personagem.</summary>
	private void AjustarCameraMapa()
	{
		if (_hostCamera == null)
		{
			return;
		}

		Vector2 tamanho = GetViewportRect().Size;

		if (tamanho.X <= 1 || tamanho.Y <= 1)
		{
			return;
		}

		float zoom = Mathf.Min(tamanho.X / 1320f, tamanho.Y / 820f);
		zoom = Mathf.Clamp(zoom, 0.62f, 1.0f);

		_hostCamera.Zoom = new Vector2(zoom, zoom);
		_hostCamera.Position = new Vector2(0, 8);
		_hostCamera.Enabled = true;
	}

	/// <summary>Impede o personagem de andar enquanto o jogo está aberto.</summary>
	private void TravarPersonagem(bool travado)
	{
		foreach (Node filho in _playersRoot.GetChildren())
		{
			if (filho is Personagem personagem && personagem.IsMultiplayerAuthority())
			{
				personagem.InputLocked = travado;
			}
		}
	}


	/// <summary>Aluno pede para abrir uma porta (o servidor do lobby divulga).</summary>
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequisitarJogo(string nome, int porta, string ip, string gameKey)
	{
		if (!Multiplayer.IsServer())
		{
			return;
		}

		long remetente = Multiplayer.GetRemoteSenderId();
		string criador = ObterNomeDoPeer(remetente);

		var jogo = new Jogo(
			string.IsNullOrWhiteSpace(nome) ? $"Jogo de {criador}" : nome,
			_session?.sala?.id ?? (_session?.salaId > 0 ? _session.salaId : null),
			null,
			criador,
			string.IsNullOrWhiteSpace(ip) ? ObterIpDoPeer(remetente) : ip,
			porta,
			gameKey);

		// Um jogo por aluno: a porta anterior é substituída.
		_jogos.RemoveAll(j => j.criador == criador);
		_jogos.Add(jogo);

		DivulgarJogos();
	}

	/// <summary>Fecha uma porta (pedido do dono do jogo ou do professor).</summary>
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void PedirFecharJogo(string nome, int porta)
	{
		if (!Multiplayer.IsServer())
		{
			return;
		}

		_jogos.RemoveAll(j => j.porta == porta);
		DivulgarJogos();

		// Avisa quem está dentro da partida para voltar ao lobby.
		Rpc(nameof(ReceberJogoEncerrado), porta);
	}

	/// <summary>O servidor do lobby avisa que uma porta foi encerrada.</summary>
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReceberJogoEncerrado(int porta)
	{
		if (!_gameOverlay.Aberto)
		{
			return;
		}

		if (porta == _minhaPortaJogo || porta == _jogoConectado?.porta)
		{
			_jogoStatus.Text = "A porta desta partida foi encerrada.";
			_gameOverlay.Fechar();
		}
	}


	/// <summary>O servidor do lobby manda a lista de jogos para todos.</summary>
	private void DivulgarJogos()
	{
		string json = JsonSerializer.Serialize(_jogos);
		Rpc(nameof(ReceberJogos), json);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReceberJogos(string json)
	{
		try
		{
			_jogos.Clear();
			_jogos.AddRange(JsonSerializer.Deserialize<List<Jogo>>(json) ?? new List<Jogo>());
		}
		catch (Exception e)
		{
			GD.PrintErr("[JOGO] Lista de jogos inválida: " + e.Message);
			return;
		}

		AtualizarListaJogos();

		GD.Print($"[JOGO] Lista de jogos recebida: {_jogos.Count} jogo(s).");
	}

	private void EntrarNoJogo(Jogo jogo)
	{
		if (_gameOverlay.Aberto || _apiJogo != null)
		{
			_jogoStatus.Text = "Você já está em uma partida. Feche-a antes de entrar em outra.";
			return;
		}

		GameInfo? info = GameRegistry.PorId(jogo.gameKey) ?? GameRegistry.Jogos.FirstOrDefault();

		if (info == null)
		{
			_jogoStatus.Text = "O jogo desta porta não está mais no catálogo.";
			return;
		}

		var peer = new ENetMultiplayerPeer();
		Error erro = peer.CreateClient(jogo.host_ip, jogo.porta);

		if (erro != Error.Ok)
		{
			peer.Dispose();
			_jogoStatus.Text = $"Não foi possível entrar na porta {jogo.porta} (erro {erro}).";
			return;
		}

		var api = MultiplayerApi.CreateDefaultInterface();
		api.MultiplayerPeer = peer;
		GetTree().SetMultiplayer(api, _jogosRoot.GetPath());

		_apiJogo = api;
		_peerJogo = peer;
		_jogoConectado = jogo;
		_minhaPortaJogo = 0;
		_ultimoJogadoresPublicados = -1;
		_placarJogoAtual = string.Empty;

		_jogoStatus.Text = $"Entrando na porta {jogo.porta} • {info.Nome} • criada por {jogo.criador}.";
		GD.Print($"[JOGO] Conectando em {jogo.host_ip}:{jogo.porta} ({info.Id}).");

		AbrirCenaDoJogo(info, jogo.porta, souHost: false);
	}

	private void EntrarNoPrimeiroJogo()
	{
		// Atalho de desenvolvimento: entra na primeira porta aberta.
		if (_jogos.Count == 0)
		{
			GD.Print("[JOGO] Nenhuma porta disponível para entrar.");
			return;
		}

		EntrarNoJogo(_jogos[0]);
	}

	private void FecharMeuJogo(Jogo jogo)
	{
		bool envolvido = jogo.porta == _minhaPortaJogo || jogo.porta == _jogoConectado?.porta;

		if (Multiplayer.IsServer())
		{
			// O professor (ou o dono no modo solo) encerra a porta para todos.
			RemoverJogoServidor(jogo.porta);
			Rpc(nameof(ReceberJogoEncerrado), jogo.porta);
		}
		else
		{
			// O dono pede ao servidor do lobby para tirar a porta da lista.
			RpcId(1, nameof(PedirFecharJogo), jogo.nome, jogo.porta);
		}

		if (envolvido)
		{
			_gameOverlay.Fechar(); // dispara OnJogoFechado (limpeza)
		}

		_jogoStatus.Text = $"Porta \"{jogo.nome}\" encerrada.";
	}

	/// <summary>
	/// Encerra só a conexão da partida (peer/API). A limpeza de interface fica
	/// em <see cref="OnJogoFechado"/>, chamado pelo overlay.
	/// </summary>
	private void FecharConexaoJogo()
	{
		if (_peerJogo != null)
		{
			_peerJogo.Close();
			_peerJogo = null;
		}

		if (_apiJogo != null && _jogosRoot != null && IsInstanceValid(_jogosRoot) && _jogosRoot.IsInsideTree())
		{
			GetTree().SetMultiplayer(null, _jogosRoot.GetPath());
			_apiJogo = null;
		}

		_apiJogo = null;
		_jogoConectado = null;
		_minhaPortaJogo = 0;
		_ultimoJogadoresPublicados = -1;
		_placarJogoAtual = string.Empty;
	}

	/// <summary>Chamado quando o overlay fecha (Esc, botão ou fim de partida).</summary>
	private void OnJogoFechado()
	{
		bool eraDono = _minhaPortaJogo > 0;
		int porta = _minhaPortaJogo;
		Jogo? jogo = _jogoConectado;

		FecharConexaoJogo();
		TravarPersonagem(false);

		if (eraDono && porta > 0)
		{
			if (Multiplayer.IsServer())
			{
				RemoverJogoServidor(porta);

				// Quem estava na partida volta ao lobby junto com o dono.
				Rpc(nameof(ReceberJogoEncerrado), porta);
			}
			else
			{
				RpcId(1, nameof(PedirFecharJogo), jogo?.nome ?? string.Empty, porta);
			}

			_jogoStatus.Text = "Você fechou a porta e voltou ao lobby.";
		}
		else if (jogo != null)
		{
			_jogoStatus.Text = "Você saiu da partida e voltou ao lobby.";
		}

		AtualizarListaJogos();
	}

	private void AtualizarListaJogos()
	{
		// ---- Lista do professor (painel de gerenciamento) --------------
		if (_dashboardJogos != null)
		{
			LimparFilhos(_dashboardJogos);

			foreach (Jogo jogo in _jogos)
			{
				GameInfo? info = GameRegistry.PorId(jogo.gameKey);
				string titulo = info?.Nome ?? jogo.nome;
				string emblema = info?.Emblema ?? "★";

				var cartao = UIHelpers.Cartao();
				var coluna = UIHelpers.Coluna(3);

				var header = UIHelpers.Linha(8);
				Label nomeJogo = UIHelpers.TextoExpandido($"{emblema}  {titulo}", 15);
				if (info != null)
				{
					nomeJogo.AddThemeColorOverride("font_color", info.Cor.Lightened(0.2f));
				}
				header.AddChild(nomeJogo);

				var encerrar = new Button { Text = "ENCERRAR", FocusMode = Control.FocusModeEnum.None };
				encerrar.AddThemeFontSizeOverride("font_size", 11);
				Jogo jogoAlvo = jogo;
				encerrar.Pressed += () => FecharMeuJogo(jogoAlvo);
				header.AddChild(encerrar);

				coluna.AddChild(header);
				coluna.AddChild(UIHelpers.TextoSuaveLabel(
					$"\"{jogo.nome}\" • {jogo.criador} • porta {jogo.porta}", 11));
				coluna.AddChild(UIHelpers.TextoSuaveLabel(
					$"{Math.Max(1, jogo.jogadores)} jogador(es) • {jogo.status} • sala {jogo.id_sala?.ToString() ?? "—"}", 10));

				if (!string.IsNullOrWhiteSpace(jogo.placar))
				{
					coluna.AddChild(UIHelpers.TextoSuaveLabel("Placar: " + jogo.placar, 10));
				}

				cartao.AddChild(coluna);
				_dashboardJogos.AddChild(cartao);
			}

			_dashboardJogosEmpty.Visible = _jogos.Count == 0;
		}

		if (_dashboardResumo != null)
		{
			int emPartida = _jogos.Sum(j => Math.Max(1, j.jogadores));
			_dashboardResumo.Text = _jogos.Count == 0
				? "Nenhuma porta aberta • os alunos entram pela casinha no lobby."
				: $"{_jogos.Count} porta(s) aberta(s) • {emPartida} jogador(es) em partidas";
		}

		// Resumo discreto para o aluno (quando não está dentro de um jogo).
		if (_jogoConectado == null && _minhaPortaJogo == 0 && _jogos.Count > 0 &&
			!_gameOverlay.Aberto && _jogoStatus != null)
		{
			_jogoStatus.Text = _jogos.Count == 1
				? "1 partida aberta. Vá até a casinha e pressione E para entrar."
				: $"{_jogos.Count} partidas abertas. Vá até uma casinha e pressione E.";
		}
	}

	private static void LimparFilhos(Node pai)
	{
		foreach (Node filho in pai.GetChildren())
		{
			pai.RemoveChild(filho);
			filho.QueueFree();
		}
	}

	/// <summary>IP desta máquina (usado como endereço da porta de jogo).</summary>
	private string MeuIp()
	{
		try
		{
			foreach (IPAddress endereco in Dns.GetHostAddresses(Dns.GetHostName()))
			{
				if (endereco.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(endereco))
				{
					return endereco.ToString();
				}
			}
		}
		catch (Exception e)
		{
			GD.PrintErr("[JOGO] Não foi possível descobrir o IP local: " + e.Message);
		}

		return "127.0.0.1";
	}

	/// <summary>
	/// IP informado pelo próprio aluno (o servidor não consegue ler o IP do
	/// peer pela API do Godot, então quem cria o jogo manda o seu).
	/// </summary>
	private string ObterIpDoPeer(long id)
	{
		return "127.0.0.1";
	}

	private string ObterNomeDoPeer(long id)
	{
		if (_playersRoot.GetNodeOrNull(id.ToString()) is Personagem personagem &&
			!string.IsNullOrWhiteSpace(personagem.PlayerName))
		{
			return personagem.PlayerName;
		}

		return "Aluno";
	}

	// ---------------------------------------------------------------------
	// Interface
	// ---------------------------------------------------------------------

	private void ShowHud()
	{
		_startOverlay.Visible = false;
		_dashboard.Visible = false;
		_hud.Visible = true;

		// O botão de voltar ao painel só existe no computador do professor.
		bool professor = _session?.lobbyMode == LobbyMode.Host && Multiplayer.IsServer();
		_painelButton.Visible = professor;

		_hintLabel.Visible = true;
		_hintLabel.Text = professor
			? "Tab: alunos online"
			: "WASD para andar  •  Tab: alunos online  •  E: entrar na casinha";

		_interactPrompt.Visible = false;
		_casaProxima = null;

		AtualizarCatalogo();
		AtualizarListaJogos();
		UpdatePlayersLabel();
	}

	private void ShowHostDashboard()
	{
		_startOverlay.Visible = false;
		_hud.Visible = false;
		_dashboard.Visible = true;
		_interactPrompt.Visible = false;
		_casaProxima = null;
		EsconderOnline();
		_dashboardRoom.Text = string.IsNullOrWhiteSpace(_session?.roomName) ? "Lobby" : _session.roomName;
		AtualizarCatalogo();
		AtualizarListaJogos();
		AtualizarDashboard(true);
	}

	private void ShowStartOverlay(string message)
	{
		_hud.Visible = false;
		_dashboard.Visible = false;
		_interactPrompt.Visible = false;
		_casaProxima = null;
		_onlineVisivel = false;
		_onlineOverlay.Visible = false;
		_startOverlay.Visible = true;
		_messageLabel.Text = message;
	}

	private void SetStatus(string message)
	{
		_statusLabel.Text = message;
	}

	private void SetMessage(string message)
	{
		_messageLabel.Text = message;
	}

	private void UpdatePlayersLabel()
	{
		int count = _playersRoot?.GetChildCount() ?? 0;
		_playersLabel.Text = count == 1 ? "1 online" : $"{count} online";
	}

	private void AtualizarDashboard(bool forcar = false)
	{
		if (_dashboard == null || !_dashboard.Visible || !Multiplayer.IsServer())
		{
			return;
		}

		var assinatura = new StringBuilder();
		int total = 0;

		foreach (Node filho in _playersRoot.GetChildren())
		{
			if (filho is not Personagem personagem)
			{
				continue;
			}

			total++;
			assinatura.Append(personagem.Name).Append('|')
				.Append(personagem.PlayerName).Append(';');
		}

		string atual = assinatura.ToString();

		if (!forcar && atual == _dashboardSignature)
		{
			return;
		}

		_dashboardSignature = atual;

		foreach (Node linha in _dashboardPlayers.GetChildren())
		{
			_dashboardPlayers.RemoveChild(linha);
			linha.QueueFree();
		}

		foreach (Node filho in _playersRoot.GetChildren())
		{
			if (filho is not Personagem personagem)
			{
				continue;
			}

			string nome = string.IsNullOrWhiteSpace(personagem.PlayerName) ||
						  personagem.PlayerName == "Jogador"
				? $"Aluno {personagem.Name}"
				: personagem.PlayerName;

			CriarLinhaDashboard(nome, personagem.Name);
		}

		_dashboardCount.Text = total.ToString();
		_dashboardEmpty.Visible = total == 0;
		string estado = _jogoIniciado ? "Jogo iniciado" : "Sessão aberta";
		_dashboardStatus.Text = total switch
		{
			0 => $"{estado} • aguardando alunos",
			1 => $"{estado} • 1 aluno conectado",
			_ => $"{estado} • {total} alunos conectados"
		};
	}

	private void CriarLinhaDashboard(string nome, string id)
	{
		var linha = UIHelpers.Cartao();
		var conteudo = UIHelpers.Linha(10);

		conteudo.AddChild(UIHelpers.TextoExpandido(nome, 17));
		conteudo.AddChild(UIHelpers.SeloStatus("ONLINE"));

		linha.AddChild(conteudo);
		_dashboardPlayers.AddChild(linha);
	}

	public override void _Process(double delta)
	{
		if (!_started)
		{
			return;
		}

		if (_dashboard.Visible && Multiplayer.IsServer())
		{
			AtualizarDashboard();
		}
		else if (_hud.Visible)
		{
			UpdatePlayersLabel();
			AtualizarCasaProxima();
		}
		else
		{
			EsconderPromptCasa();
		}

		if (_onlineVisivel)
		{
			_timerOnline += (float)delta;

			if (_timerOnline >= 0.4f)
			{
				_timerOnline = 0f;
				AtualizarOnline();
			}
		}
		else
		{
			_timerOnline = 0f;
		}

		AtualizarStatusJogo();

		// O dono da porta publica no painel do professor quantos jogadores
		// estão na partida (e o placar final quando o jogo termina).
		if (_minhaPortaJogo > 0 && _gameOverlay.Aberto)
		{
			_timerPublicacaoJogo += (float)delta;

			if (_timerPublicacaoJogo >= 1f)
			{
				_timerPublicacaoJogo = 0f;
				int jogadores = Math.Max(1, (_apiJogo?.GetPeers().Length ?? 0) + 1);

				if (jogadores != _ultimoJogadoresPublicados)
				{
					_ultimoJogadoresPublicados = jogadores;
					PublicarProgressoJogo(_minhaPortaJogo, jogadores, _placarJogoAtual);
				}
			}
		}

		// A lista de alunos da sala (vai no anúncio para a tela do aluno)
		// é refeita de tempos em tempos, na thread principal.
		_timerAlunos += (float)delta;

		if (_timerAlunos >= 0.5f)
		{
			_timerAlunos = 0f;
			AtualizarAlunosConectados();
		}
	}

	/// <summary>
	/// Mostra quantas pessoas estão na porta da partida e atualiza a lista de
	/// jogos (o servidor pode mudar a qualquer momento).
	/// </summary>
	private void AtualizarStatusJogo()
	{
		if (_jogoStatus == null)
		{
			return;
		}

		if (_peerJogo != null && _minhaPortaJogo > 0)
		{
			// Sou o dono da porta: mostra quantos colegas entraram.
			int conectados = (_apiJogo?.GetPeers().Length ?? 0) + 1;

			if (conectados != _ultimoCountJogo)
			{
				_ultimoCountJogo = conectados;
				GD.Print($"[JOGO] Jogadores na porta {_minhaPortaJogo}: {conectados}.");
			}

			_jogoStatus.Text = $"Sua porta {_minhaPortaJogo} • {conectados} jogador(es) • " +
							   (_gameOverlay.Aberto ? "em partida" : "no lobby") +
							   (string.IsNullOrWhiteSpace(_placarJogoAtual) ? string.Empty : $" • {_placarJogoAtual}");
		}
		else if (_jogoConectado != null)
		{
			_jogoStatus.Text = $"Na porta {_jogoConectado.porta} • {_jogoConectado.nome} • dono: {_jogoConectado.criador}.";
		}
	}

	public override void _ExitTree()
	{
		Multiplayer.PeerConnected -= OnPeerConnected;
		Multiplayer.PeerDisconnected -= OnPeerDisconnected;
		Multiplayer.ConnectedToServer -= OnConnectedToServer;
		Multiplayer.ConnectionFailed -= OnConnectionFailed;
		Multiplayer.ServerDisconnected -= OnServerDisconnected;

		if (_retryTimer != null)
		{
			_retryTimer.Timeout -= OnRetryTimeout;
		}

		if (_gameOverlay != null)
		{
			_gameOverlay.Fechado -= OnJogoFechado;
		}

		if (IsInsideTree())
		{
			GetViewport().SizeChanged -= AjustarCameraMapa;
		}

		_autoRetry = false;
		AlunoService.StopSendingPersona();
		ProfessorService.StopMessage();
		FecharConexaoJogo();
		ResetPeer();
	}
}
