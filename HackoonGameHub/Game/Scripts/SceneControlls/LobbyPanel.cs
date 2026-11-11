using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Game.Scripts;
using Models;
using Repository.Postgress;
using Service;

public partial class LobbyPanel : Control
{
	private Label _nomeSalaLabel;
	private Label _statusLabel;
	private Label _emptyLabel;
	private VBoxContainer _alunosList;
	private Button _abrirLobbyButton;
	private Button _voltarButton;

	private SessionManager _session;

	private readonly HashSet<int> _alunosAdicionados = new();
	private bool _encerrando;

	public override void _Ready()
	{
		_nomeSalaLabel = GetNode<Label>("%NomeSalaLabel");
		_statusLabel = GetNode<Label>("%StatusLabel");
		_emptyLabel = GetNode<Label>("%EmptyLabel");
		_alunosList = GetNode<VBoxContainer>("%AlunosList");
		_abrirLobbyButton = GetNode<Button>("%AbrirLobbyButton");
		_voltarButton = GetNode<Button>("%VoltarButton");

		_session = GetNode<SessionManager>("/root/SessionManager");

		_nomeSalaLabel.Text = _session.sala?.name ?? _session.roomName;

		_abrirLobbyButton.Pressed += OnAbrirLobbyPressed;
		_voltarButton.Pressed += OnVoltarPressed;

		// Anuncia a sessão (com o nome da sala) enquanto esta tela estiver aberta.
		ProfessorService.SendMessage(
			_session.usuario?.name ?? "Professor",
			_nomeSalaLabel.Text);

		AtualizarStatus();

		_ = OuvirAlunos();
	}

	private async Task OuvirAlunos()
	{
		while (!_encerrando && IsInsideTree())
		{
			NetworkPacket pacote;

			try
			{
				pacote = await ProfessorService.carregarAlunos();
			}
			catch (Exception e)
			{
				GD.PrintErr("[LOBBY] Falha ao ouvir alunos: " + e.Message);
				break;
			}

			if (_encerrando || !IsInstanceValid(this) || !IsInsideTree())
			{
				return;
			}

			if (pacote == null)
			{
				// Timeout: continua aguardando novos alunos em segundo plano.
				continue;
			}

			int id = LerIdAluno(pacote);

			if (id <= 0 || !_alunosAdicionados.Add(id))
			{
				continue;
			}

			try
			{
				Aluno aluno = await AlunoService.Read(id);

				if (_encerrando || !IsInstanceValid(this) || !IsInsideTree())
				{
					return;
				}

				if (aluno != null)
				{
					AdicionarAluno(aluno);
					AtualizarStatus();
				}
			}
			catch (Exception e)
			{
				GD.PrintErr("[LOBBY] Não foi possível carregar o aluno: " + e.Message);
			}
		}
	}

	private static int LerIdAluno(NetworkPacket pacote)
	{
		try
		{
			using JsonDocument docExterior = JsonDocument.Parse(pacote.DadosJson);
			JsonElement raiz = docExterior.RootElement;

			if (!raiz.TryGetProperty("DadosJson", out JsonElement dadosElement))
			{
				return -1;
			}

			string jsonInterno = dadosElement.GetString();

			if (string.IsNullOrWhiteSpace(jsonInterno))
			{
				return -1;
			}

			var opcoes = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
			Dados dados = JsonSerializer.Deserialize<Dados>(jsonInterno, opcoes);
			return dados.ID;
		}
		catch (Exception e)
		{
			GD.PrintErr("[LOBBY] Pacote de entrada inválido: " + e.Message);
			return -1;
		}
	}

	private void AdicionarAluno(Aluno aluno)
	{
		_emptyLabel.Visible = false;

		var linha = UIHelpers.Cartao();
		var conteudo = UIHelpers.Linha();

		conteudo.AddChild(UIHelpers.TextoExpandido(aluno.name));
		conteudo.AddChild(UIHelpers.TextoExpandido(aluno.username ?? string.Empty));
		conteudo.AddChild(UIHelpers.SeloStatus("CONECTADO"));

		linha.AddChild(conteudo);
		_alunosList.AddChild(linha);
	}

	private void AtualizarStatus()
	{
		int total = _alunosAdicionados.Count;

		_statusLabel.Text = total switch
		{
			0 => "Sessão aberta. Os alunos podem entrar pela tela inicial.",
			1 => "1 aluno conectado. Você já pode abrir o lobby.",
			_ => $"{total} alunos conectados. Você já pode abrir o lobby."
		};
	}

	private void OnAbrirLobbyPressed()
	{
		_encerrando = true;
		ProfessorService.StopMessage();
		BroadcastUDP.StopListening();

		_session.roomName = _session.sala?.name ?? _session.roomName;
		_session.playerName = _session.usuario?.name ?? "Professor";
		_session.lobbyMode = LobbyMode.Host;
		_session.lobbyIp = "127.0.0.1";

		Error resultado = GetTree().ChangeSceneToFile("res://Scenes/Personagem/LobbyPrincipal.tscn");

		if (resultado != Error.Ok)
		{
			GD.PrintErr("Erro ao abrir o lobby: " + resultado);
		}
	}

	private void OnVoltarPressed()
	{
		_encerrando = true;
		ProfessorService.StopMessage();
		BroadcastUDP.StopListening();
		GetTree().ChangeSceneToFile("res://Scenes/Forms/FormsSalas.tscn");
	}

	public override void _ExitTree()
	{
		_encerrando = true;
		ProfessorService.StopMessage();
		BroadcastUDP.StopListening();
	}

	private struct Dados
	{
		public int ID { get; set; }
		public string Status { get; set; }
	}
}
