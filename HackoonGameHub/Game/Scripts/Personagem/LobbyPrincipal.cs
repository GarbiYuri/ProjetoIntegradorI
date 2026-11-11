using Godot;
using System.Text;
using Game.Scripts;
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
	private Node _jogosRoot;

	private bool _jogoIniciado;
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

		GetNode<Button>("%SoloButton").Pressed += OnSoloPressed;
		GetNode<Button>("%HostButton").Pressed += OnHostPressed;
		GetNode<Button>("%JoinButton").Pressed += OnJoinPressed;
		GetNode<Button>("%LeaveButton").Pressed += OnLeavePressed;
		GetNode<Button>("%BackButton").Pressed += OnLeavePressed;
		GetNode<Button>("%EndSessionButton").Pressed += OnEndSessionPressed;
		GetNode<Button>("%DashboardLeaveButton").Pressed += OnLeavePressed;
		GetNode<Button>("%IniciarJogoButton").Pressed += OnIniciarJogoPressed;
		GetNode<Button>("%PainelButton").Pressed += OnPainelPressed;

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
						string[] partes = extra.Split('=', 2);
						double atraso = partes.Length == 2 && double.TryParse(partes[1], out double segundos)
							? segundos
							: 0;
						IniciarJogoComAtraso(atraso);
					}
				}

				return;
			}

			if (argument.StartsWith("--lobby-join="))
			{
				_startOverlay.Visible = false;
				_autoRetry = true;
				StartClient(argument.Substring("--lobby-join=".Length));
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

	/// <summary>Atalho de desenvolvimento: dispara o INICIAR JOGO (com atraso opcional).</summary>
	private async void IniciarJogoComAtraso(double atraso)
	{
		if (atraso > 0)
		{
			await ToSignal(GetTree().CreateTimer(atraso), SceneTreeTimer.SignalName.Timeout);
		}

		if (!IsInstanceValid(this) || !IsInsideTree())
		{
			return;
		}

		OnIniciarJogoPressed();
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
		ProfessorService.SendMessage(
			_session?.usuario?.name ?? "Professor",
			string.IsNullOrWhiteSpace(_session?.roomName) ? "Lobby" : _session.roomName);

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
		}
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
		ResetPeer();
	}
}
