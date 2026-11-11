using Godot;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Game.Scripts;
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
	private Node _jogosRoot;

	// ---------------------------------------------------------------------
	// Jogos internos (criados pelos ALUNOS dentro do lobby)
	// ---------------------------------------------------------------------
	private Control _jogosPanel;
	private VBoxContainer _jogosList;
	private Label _jogoStatus;
	private LineEdit _jogoNomeEdit;
	private Button _criarJogoButton;
	private Button _sairJogoButton;

	private readonly List<Jogo> _jogos = new();
	private List<AlunoOnline> _alunosConectados = new();

	// Conexão interna: API de multiplayer separada, applied só no nó
	// "World/Jogos" — o lobby continua na conexão principal.
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
		_ipEdit = GetNode<LineEdit>("%IpEdit");

		_dashboardRoom = GetNode<Label>("%DashboardRoom");
		_dashboardStatus = GetNode<Label>("%DashboardStatus");
		_dashboardCount = GetNode<Label>("%DashboardCount");
		_dashboardEmpty = GetNode<Label>("%DashboardEmpty");
		_dashboardPlayers = GetNode<VBoxContainer>("%DashboardPlayers");
		_dashboardJogos = GetNode<VBoxContainer>("%DashboardJogos");
		_dashboardJogosEmpty = GetNode<Label>("%DashboardJogosEmpty");

		_jogosPanel = GetNode<Control>("%JogosPanel");
		_jogosList = GetNode<VBoxContainer>("%JogosList");
		_jogoStatus = GetNode<Label>("%JogoStatus");
		_jogoNomeEdit = GetNode<LineEdit>("%JogoNomeEdit");
		_criarJogoButton = GetNode<Button>("%CriarJogoButton");
		_sairJogoButton = GetNode<Button>("%SairJogoButton");

		GetNode<Button>("%SoloButton").Pressed += OnSoloPressed;
		GetNode<Button>("%HostButton").Pressed += OnHostPressed;
		GetNode<Button>("%JoinButton").Pressed += OnJoinPressed;
		GetNode<Button>("%LeaveButton").Pressed += OnLeavePressed;
		GetNode<Button>("%BackButton").Pressed += OnLeavePressed;
		GetNode<Button>("%EndSessionButton").Pressed += OnEndSessionPressed;
		GetNode<Button>("%DashboardLeaveButton").Pressed += OnLeavePressed;
		GetNode<Button>("%IniciarJogoButton").Pressed += OnIniciarJogoPressed;
		GetNode<Button>("%PainelButton").Pressed += OnPainelPressed;
		_criarJogoButton.Pressed += OnCriarJogoPressed;
		_sairJogoButton.Pressed += OnSairDoJogoPressed;

		// Onde os minigames vão entrar (INICIAR JOGO leva todos para cá).
		_jogosRoot = GetNodeOrNull<Node>("World/Jogos");

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

				return;
			}

			if (argument.StartsWith("--lobby-join="))
			{
				_startOverlay.Visible = false;
				_autoRetry = true;
				StartClient(argument.Substring("--lobby-join=".Length));

				// Atalho de teste: cria um jogo interno (conexão do aluno).
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
				}

				return;
			}
		}

		HandleAutoStart();
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
		// ficam no lobby, onde ficam os minigames (World/Jogos).
		ShowHud();
		SetStatus("JOGO INICIADO • todos no lobby");
		UpdatePlayersLabel();

		GD.Print("[LOBBY] Jogo iniciado. Minigames entram em World/Jogos.");
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
		_hostCamera.Enabled = false;

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
		_hostCamera.Enabled = true;

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
		_hostCamera.Enabled = false;

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
		Node player = _playersRoot.GetNodeOrNull(id.ToString());
		player?.QueueFree();
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
	// Jogos internos: conexão separada criada pelo ALUNO dentro do lobby
	//
	// O aluno não cria sala (lobby) — isso é do professor. Dentro do lobby
	// ele abre uma "conexão interna" (outro servidor ENet, em outra porta) e
	// os colegas da sala podem entrar nesse jogo.
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

	private async void OnCriarJogoPressed()
	{
		// Quem cria sala é o professor; aluno só cria jogo interno.
		if (Multiplayer.IsServer())
		{
			_jogoStatus.Text = "O professor controla os jogos da sala.";
			return;
		}

		if (_apiJogo != null)
		{
			_jogoStatus.Text = "Você já está em um jogo. Saia dele antes de criar outro.";
			return;
		}

		string nome = _jogoNomeEdit.Text.Trim();

		if (string.IsNullOrWhiteSpace(nome))
		{
			nome = $"Jogo de {_session?.playerName ?? "Aluno"}";
		}

		int porta = CriarServidorInterno();

		if (porta == 0)
		{
			_jogoStatus.Text = "Não foi possível abrir a conexão interna (portas ocupadas).";
			return;
		}

		// Registra no banco (o jogo é uma conexão interna da sala).
		try
		{
			await JogoService.Criar(
				nome,
				_session?.salaId > 0 ? _session.salaId : null,
				null,
				_session?.playerName ?? "Aluno",
				MeuIp(),
				porta);
		}
		catch (Exception e)
		{
			GD.PrintErr("[JOGO] Não foi possível registrar o jogo no banco: " + e.Message);
		}

		// Avisa o servidor do lobby, que divulga o jogo para toda a sala.
		RpcId(1, nameof(RequisitarJogo), nome, porta, MeuIp());

		_jogoNomeEdit.Text = string.Empty;
		_jogoStatus.Text = $"Jogo \"{nome}\" criado! Os colegas da sala já podem entrar.";
		AtualizarListaJogos();
	}

	/// <summary>
	/// Sobe o servidor da conexão interna em uma porta livre (7778 em diante)
	/// e devolve a porta usada (0 = nenhuma porta livre).
	/// </summary>
	private int CriarServidorInterno()
	{
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

			// A API do jogo vale só para o nó "World/Jogos": a conexão do
			// lobby (principal) continua intacta.
			GetTree().SetMultiplayer(api, _jogosRoot.GetPath());

			_apiJogo = api;
			_peerJogo = peer;
			_minhaPortaJogo = porta;
			_sairJogoButton.Visible = true;

			GD.Print($"[JOGO] Conexão interna aberta na porta {porta}.");
			return porta;
		}

		return 0;
	}

	/// <summary>Aluno pede para abrir um jogo (o servidor do lobby divulga).</summary>
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequisitarJogo(string nome, int porta, string ip)
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
			porta);

		// Um jogo por aluno: o anterior é substituído.
		_jogos.RemoveAll(j => j.criador == criador);
		_jogos.Add(jogo);

		DivulgarJogos();
	}

	/// <summary>Fecha um jogo interno (pedido do dono do jogo ou do professor).</summary>
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void PedirFecharJogo(string nome, int porta)
	{
		if (!Multiplayer.IsServer())
		{
			return;
		}

		_jogos.RemoveAll(j => j.porta == porta && (j.nome == nome || j.criador == ObterNomeDoPeer(Multiplayer.GetRemoteSenderId())));
		DivulgarJogos();
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
		if (_apiJogo != null)
		{
			_jogoStatus.Text = "Você já está em um jogo. Saia dele antes de entrar em outro.";
			return;
		}

		var peer = new ENetMultiplayerPeer();
		Error erro = peer.CreateClient(jogo.host_ip, jogo.porta);

		if (erro != Error.Ok)
		{
			peer.Dispose();
			_jogoStatus.Text = $"Não foi possível entrar no jogo \"{jogo.nome}\" (erro {erro}).";
			return;
		}

		var api = MultiplayerApi.CreateDefaultInterface();
		api.MultiplayerPeer = peer;
		GetTree().SetMultiplayer(api, _jogosRoot.GetPath());

		_apiJogo = api;
		_peerJogo = peer;
		_jogoConectado = jogo;
		_sairJogoButton.Visible = true;

		_jogoStatus.Text = $"Conectado ao jogo \"{jogo.nome}\" (criado por {jogo.criador}).";
		GD.Print($"[JOGO] Conectado ao jogo '{jogo.nome}' em {jogo.host_ip}:{jogo.porta}.");
	}

	private void EntrarNoPrimeiroJogo()
	{
		// Atalho de desenvolvimento: entra no primeiro jogo da lista.
		if (_jogos.Count == 0)
		{
			GD.Print("[JOGO] Nenhum jogo disponível para entrar.");
			return;
		}

		EntrarNoJogo(_jogos[0]);
	}

	private void FecharMeuJogo(Jogo jogo)
	{
		// Avisa o servidor do lobby para tirar o jogo da lista da sala.
		RpcId(1, nameof(PedirFecharJogo), jogo.nome, jogo.porta);

		FecharConexaoJogo();
		_jogoStatus.Text = $"Você fechou o jogo \"{jogo.nome}\".";
	}

	private void OnSairDoJogoPressed()
	{
		FecharConexaoJogo();
		_jogoStatus.Text = "Você saiu do jogo. Já pode entrar em outro.";
	}

	/// <summary>Encerra a conexão interna (jogo) e volta para o lobby.</summary>
	private void FecharConexaoJogo()
	{
		if (_peerJogo != null)
		{
			_peerJogo.Close();
			_peerJogo = null;
		}

		if (_apiJogo != null)
		{
			GetTree().SetMultiplayer(null, _jogosRoot.GetPath());
			_apiJogo = null;
		}

		_jogoConectado = null;
		_minhaPortaJogo = 0;
		_sairJogoButton.Visible = false;
	}

	private void AtualizarListaJogos()
	{
		// Lista do aluno (HUD do lobby).
		if (_jogosList != null)
		{
			foreach (Node linha in _jogosList.GetChildren())
			{
				_jogosList.RemoveChild(linha);
				linha.QueueFree();
			}

			foreach (Jogo jogo in _jogos)
			{
				var cartao = UIHelpers.Cartao();
				var linha = UIHelpers.Linha(8);

				var textos = new VBoxContainer
				{
					SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
				};
				textos.AddChild(UIHelpers.Texto(jogo.nome, 16));
				textos.AddChild(UIHelpers.TextoSuaveLabel($"criado por {jogo.criador}", 13));

				linha.AddChild(textos);

				bool souDono = _minhaPortaJogo > 0 && jogo.porta == _minhaPortaJogo;

				if (souDono)
				{
					var fechar = new Button { Text = "FECHAR" };
					Jogo jogoAlvo = jogo;
					fechar.Pressed += () => FecharMeuJogo(jogoAlvo);
					linha.AddChild(fechar);
				}
				else
				{
					var entrar = new Button { Text = "ENTRAR" };
					Jogo jogoAlvo = jogo;
					entrar.Pressed += () => EntrarNoJogo(jogoAlvo);
					linha.AddChild(entrar);
				}

				cartao.AddChild(linha);
				_jogosList.AddChild(cartao);
			}
		}

		// Lista do professor (painel de gerenciamento).
		if (_dashboardJogos != null)
		{
			foreach (Node linha in _dashboardJogos.GetChildren())
			{
				_dashboardJogos.RemoveChild(linha);
				linha.QueueFree();
			}

			foreach (Jogo jogo in _jogos)
			{
				var cartao = UIHelpers.Cartao();
				var linha = UIHelpers.Linha(8);

				linha.AddChild(UIHelpers.TextoExpandido(jogo.nome, 16));
				linha.AddChild(UIHelpers.TextoSuaveLabel(jogo.criador, 13));

				var encerrar = new Button { Text = "ENCERRAR" };
				Jogo jogoAlvo = jogo;
				encerrar.Pressed += () => FecharMeuJogo(jogoAlvo);
				linha.AddChild(encerrar);

				cartao.AddChild(linha);
				_dashboardJogos.AddChild(cartao);
			}

			_dashboardJogosEmpty.Visible = _jogos.Count == 0;
		}

		// Resumo para o aluno (quando não está dentro de um jogo).
		if (_jogoConectado == null && _minhaPortaJogo == 0 && _jogos.Count > 0)
		{
			_jogoStatus.Text = _jogos.Count == 1
				? "1 jogo disponível. Escolha um para entrar."
				: $"{_jogos.Count} jogos disponíveis. Escolha um para entrar.";
		}
	}

	/// <summary>IP desta máquina (usado para o endereço do jogo interno).</summary>
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
		_painelButton.Visible = _session?.lobbyMode == LobbyMode.Host && Multiplayer.IsServer();

		// O professor não tem personagem: a dica de andar é só dos alunos.
		_hintLabel.Visible = _session?.lobbyMode != LobbyMode.Host;

		// Criar jogo interno é exclusivo dos alunos (o professor gerencia).
		_jogosPanel.Visible = !Multiplayer.IsServer();

		UpdatePlayersLabel();
	}

	private void ShowHostDashboard()
	{
		_startOverlay.Visible = false;
		_hud.Visible = false;
		_dashboard.Visible = true;
		_dashboardRoom.Text = string.IsNullOrWhiteSpace(_session?.roomName) ? "Lobby" : _session.roomName;
		AtualizarDashboard(true);
	}

	private void ShowStartOverlay(string message)
	{
		_hud.Visible = false;
		_dashboard.Visible = false;
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
		_playersLabel.Text = $"Jogadores: {count}";
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
		}

		AtualizarStatusJogo();

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
	/// Mostra quantas pessoas estão na conexão interna do jogo e atualiza a
	/// lista de jogos (o servidor pode mudar a qualquer momento).
	/// </summary>
	private void AtualizarStatusJogo()
	{
		if (_jogoStatus == null)
		{
			return;
		}

		if (_peerJogo != null && _minhaPortaJogo > 0)
		{
			// Sou o dono do jogo: mostra quantos colegas entraram.
			int conectados = _apiJogo?.GetPeers().Length ?? 0;

			if (conectados != _ultimoCountJogo)
			{
				_ultimoCountJogo = conectados;
				GD.Print($"[JOGO] Jogadores na conexão interna: {conectados}.");
			}

			_jogoStatus.Text = $"Seu jogo está na porta {_minhaPortaJogo} • {conectados} jogador(es) conectado(s).";
		}
		else if (_jogoConectado != null)
		{
			_jogoStatus.Text = $"No jogo \"{_jogoConectado.nome}\" • criado por {_jogoConectado.criador}.";
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

		_autoRetry = false;
		AlunoService.StopSendingPersona();
		ProfessorService.StopMessage();
		FecharConexaoJogo();
		ResetPeer();
	}
}
