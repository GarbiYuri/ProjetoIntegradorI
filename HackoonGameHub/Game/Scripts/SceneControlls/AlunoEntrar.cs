using Godot;
using System;
using System.Collections.Generic;
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
	private VBoxContainer _alunosList;
	private Label _alunosVazio;

	private SessionManager _session;

	private bool _buscando;

	private string _sessaoIp = "127.0.0.1";
	private string _sessaoProfessor = "Professor";
	private string _sessaoSala = "Sala";
	private int _sessaoSalaId;
	private int _sessaoPorta;

	// Dados que chegam da rede (thread do UDP): a interface é atualizada
	// na thread principal, no _Process.
	private readonly List<AlunoOnline> _alunos = new();
	private bool _temSessao;
	private bool _atualizarInterface;
	private bool _conectouBanco;

	public override void _Ready()
	{
		_sessoes = GetNode<VBoxContainer>("%Sessoes");
		_status = GetNode<Label>("%StatusLabel");
		_retryButton = GetNode<Button>("%RetryButton");
		_backButton = GetNode<Button>("%BackButton");
		_alunosList = GetNode<VBoxContainer>("%AlunosList");
		_alunosVazio = GetNode<Label>("%AlunosVazio");

		_session = GetNode<SessionManager>("/root/SessionManager");

		_retryButton.Pressed += OnRetryPressed;
		_backButton.Pressed += OnBackPressed;

		_status.Text = "Procurando sessões na rede local...";

		// Fica ouvindo o professor: a sessão e a lista de alunos da sala
		// são atualizadas sozinhas enquanto o aluno espera.
		_ = AlunoService.OuvirAnuncios(AoReceberAnuncio);
	}

	public override void _Process(double delta)
	{
		if (!_atualizarInterface)
		{
			return;
		}

		_atualizarInterface = false;
		AtualizarInterface();
	}

	private void OnRetryPressed()
	{
		// A escuta é contínua; o botão só força uma nova leitura da tela.
		_atualizarInterface = true;
	}

	private void OnBackPressed()
	{
		GetTree().ChangeSceneToFile("res://Scenes/InitialScene.tscn");
	}

	/// <summary>
	/// Recebe o anúncio do professor (roda fora da thread principal).
	/// </summary>
	private void AoReceberAnuncio(NetworkPacket pacote)
	{
		try
		{
			LerSessao(pacote);
			_atualizarInterface = true;

			// O banco do professor é usado para criar/acessar a conta.
			_ = ConectarBanco();
		}
		catch (Exception e)
		{
			GD.PrintErr("[ALUNO] Anúncio inválido: " + e.Message);
		}
	}

	private async System.Threading.Tasks.Task ConectarBanco()
	{
		if (_conectouBanco)
		{
			return;
		}

		_conectouBanco = true;

		try
		{
			if (_sessaoPorta > 0)
			{
				DB.DefinirPorta(_sessaoPorta);
			}

			await DB.Connect(_sessaoIp, iniciarLocal: false);
		}
		catch (Exception e)
		{
			GD.PrintErr("[ALUNO] Não foi possível conectar ao banco do professor: " + e.Message);
			_conectouBanco = false;
		}
	}

	private void LerSessao(NetworkPacket pacote)
	{
		_sessaoIp = pacote.senderIP;

		using JsonDocument doc = JsonDocument.Parse(pacote.DadosJson);
		JsonElement raiz = doc.RootElement;

		// O anúncio pode vir como payload direto ou dentro do envelope
		// antigo (que tinha um "DadosJson" dentro de outro).
		if (raiz.TryGetProperty("DadosJson", out JsonElement envelope) &&
			envelope.ValueKind == JsonValueKind.String &&
			!string.IsNullOrWhiteSpace(envelope.GetString()))
		{
			using JsonDocument docInterno = JsonDocument.Parse(envelope.GetString()!);
			raiz = docInterno.RootElement;
		}

		var opcoes = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
		Dados dados = JsonSerializer.Deserialize<Dados>(raiz.GetRawText(), opcoes);

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

		_sessaoSalaId = dados.SalaId;
		_sessaoPorta = dados.Port;

		// O IP vem no anúncio (é sempre o mesmo); o endereço de quem enviou
		// serve só de reserva.
		if (!string.IsNullOrWhiteSpace(dados.HostIp))
		{
			_sessaoIp = dados.HostIp.Trim();
		}

		// Lista de alunos que já estão na sala (enviada pelo professor).
		_alunos.Clear();

		if (dados.Alunos != null)
		{
			foreach (AlunoOnline aluno in dados.Alunos)
			{
				_alunos.Add(aluno);
			}
		}

		_temSessao = true;

		// Guarda na sessão o id da sala (liga a conta do aluno à sala).
		if (_session != null && _sessaoSalaId > 0)
		{
			_session.salaId = _sessaoSalaId;
		}
	}

	// ------------------------------------------------------------------
	// Interface
	// ------------------------------------------------------------------

	private void AtualizarInterface()
	{
		if (!IsInstanceValid(this) || !IsInsideTree())
		{
			return;
		}

		if (!_temSessao)
		{
			_status.Text = "Procurando sessões na rede local...";
			return;
		}

		_status.Text = $"Sessão de \"{_sessaoSala}\" encontrada! Entre para escolher seu personagem.";
		GD.Print($"[ALUNO] Sessão: sala '{_sessaoSala}', professor '{_sessaoProfessor}', IP {_sessaoIp}, " +
				 $"alunos na sala: {_alunos.Count}.");

		LimparSessoes();
		CriarCartaoSessao();
		AtualizarListaAlunos();
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

	/// <summary>Lista dos alunos que já estão conectados naquela sala.</summary>
	private void AtualizarListaAlunos()
	{
		foreach (Node linha in _alunosList.GetChildren())
		{
			_alunosList.RemoveChild(linha);
			linha.QueueFree();
		}

		_alunosVazio.Visible = _alunos.Count == 0;

		foreach (AlunoOnline aluno in _alunos)
		{
			var cartao = UIHelpers.Cartao();
			var linha = UIHelpers.Linha();

			linha.AddChild(UIHelpers.TextoExpandido(aluno.Nome, 17));

			if (!string.IsNullOrWhiteSpace(aluno.Nick))
			{
				linha.AddChild(UIHelpers.TextoSuaveLabel(aluno.Nick, 15));
			}

			linha.AddChild(UIHelpers.SeloStatus("ONLINE"));

			cartao.AddChild(linha);
			_alunosList.AddChild(cartao);
		}
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
		_session.salaId = _sessaoSalaId;
		_session.lobbyMode = LobbyMode.Client;

		Error resultado = GetTree().ChangeSceneToFile("res://Scenes/Forms/AlunoConta.tscn");

		if (resultado != Error.Ok)
		{
			GD.PrintErr("Erro ao abrir a tela de conta: " + resultado);
		}
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
		AlunoService.PararDeOuvirAnuncios();
		BroadcastUDP.StopListening();
	}

	private struct Dados
	{
		public string Status { get; set; }
		public string Message { get; set; }
		public string Room { get; set; }
		public int Port { get; set; }
		public int SalaId { get; set; }
		public string HostIp { get; set; }
		public List<AlunoOnline> Alunos { get; set; }
	}
}
