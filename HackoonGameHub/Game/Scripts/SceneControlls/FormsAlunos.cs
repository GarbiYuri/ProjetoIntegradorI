using Godot;
using System;
using System.Collections.Generic;
using Game.Scripts;
using Models;
using Repository.Exceptions;
using Service;

public partial class FormsAlunos : Control
{
	private VBoxContainer _alunosList;
	private Label _emptyLabel;
	private Label _sessaoLabel;
	private Button _createButton;
	private Button _backButton;

	private SessionManager _session;

	public override async void _Ready()
	{
		_alunosList = GetNode<VBoxContainer>("%AlunosList");
		_emptyLabel = GetNode<Label>("%EmptyLabel");
		_sessaoLabel = GetNode<Label>("%SessaoLabel");
		_createButton = GetNode<Button>("%CreateButton");
		_backButton = GetNode<Button>("%BackButton");

		_session = GetNode<SessionManager>("/root/SessionManager");

		_sessaoLabel.Text = string.IsNullOrWhiteSpace(_session.roomName)
			? "Sessão do professor"
			: $"Sessão: {_session.roomName}";

		_backButton.Pressed += OnBackPressed;

		List<Aluno> alunos;

		try
		{
			alunos = await AlunoService.showAll();
		}
		catch (ResourceNotFoundException)
		{
			alunos = new List<Aluno>();
		}
		catch (Exception e)
		{
			GD.PrintErr("[ALUNO] Não foi possível carregar os personagens: " + e.Message);
			alunos = new List<Aluno>();
		}

		if (!IsInstanceValid(this) || !IsInsideTree())
		{
			return;
		}

		foreach (Aluno aluno in alunos)
		{
			AdicionarAluno(aluno);
		}

		_emptyLabel.Visible = alunos.Count == 0;
	}

	private void AdicionarAluno(Aluno aluno)
	{
		var linha = UIHelpers.Cartao();
		var conteudo = UIHelpers.Linha();

		conteudo.AddChild(UIHelpers.TextoExpandido(aluno.name));
		conteudo.AddChild(UIHelpers.TextoExpandido(aluno.username ?? string.Empty));

		var entrar = new Button
		{
			Text = "ENTRAR",
			CustomMinimumSize = new Vector2(130, 0)
		};

		int idAluno = aluno.id;
		string nomeAluno = aluno.name;
		entrar.Pressed += () => EntrarNoLobby(idAluno, nomeAluno);

		conteudo.AddChild(entrar);
		linha.AddChild(conteudo);
		_alunosList.AddChild(linha);
	}

	private void EntrarNoLobby(int idAluno, string nomeAluno)
	{
		GD.Print($"[ALUNO] Entrando com o personagem {nomeAluno} (id {idAluno}).");

		AlunoService.SendMyPersona(idAluno);

		string professorIp = _session.lobbyIp;

		foreach (Usuario usuario in _session.usuariosNaRede)
		{
			if (usuario is Professor professor && !string.IsNullOrWhiteSpace(professor.ip))
			{
				professorIp = professor.ip;
				break;
			}
		}

		_session.playerName = nomeAluno;
		_session.lobbyIp = professorIp;
		_session.lobbyMode = LobbyMode.Client;

		Error resultado = GetTree().ChangeSceneToFile("res://Scenes/Personagem/LobbyPrincipal.tscn");

		if (resultado != Error.Ok)
		{
			GD.PrintErr("Erro ao abrir o lobby: " + resultado);
		}
	}

	private void OnBackPressed()
	{
		GetTree().ChangeSceneToFile("res://Scenes/AlunoEntrar.tscn");
	}
}
