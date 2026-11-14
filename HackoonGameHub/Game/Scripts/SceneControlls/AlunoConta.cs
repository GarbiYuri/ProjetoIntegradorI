#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using Game.Scripts;
using Models;
using Service;

/// <summary>
/// Tela da criança: cria a conta (nick, nome, turma) ou acessa uma existente,
/// escolhe a cor do personagem padrão (aplicada por shader) e vai para o lobby.
///
/// Estrutura de nós usada aqui:
///   %CriarButton / %AcessarButton — alternância CRIAR CONTA / JÁ TENHO CONTA
///   %FormCriar / %FormAcessar     — campos de cada modo
///   %Swatches                     — paleta de cores do personagem
///   PreviewPersonagem             — o próprio Personagem.tscn em modo preview
/// </summary>
public partial class AlunoConta : Control
{
	private SessionManager _session;

	private Button _criarButton;
	private Button _acessarButton;

	private VBoxContainer _formCriar;
	private VBoxContainer _formAcessar;

	private LineEdit _nickEdit;
	private LineEdit _nomeEdit;
	private LineEdit _turmaEdit;
	private LineEdit _senhaEdit;

	private LineEdit _nickAcessarEdit;
	private LineEdit _senhaAcessarEdit;

	private HBoxContainer _swatches;
	private Label _erroLabel;
	private Button _jogarButton;
	private Button _semContaButton;
	private Button _voltarButton;

	private Personagem _preview;
	private Label _previewNick;

	private bool _modoCriar = true;
	private Color _corEscolhida = Colors.White;
	private readonly List<Button> _botoesCor = new();

	public override void _Ready()
	{
		_session = GetNode<SessionManager>("/root/SessionManager");

		_criarButton = GetNode<Button>("%CriarButton");
		_acessarButton = GetNode<Button>("%AcessarButton");
		_formCriar = GetNode<VBoxContainer>("%FormCriar");
		_formAcessar = GetNode<VBoxContainer>("%FormAcessar");

		_nickEdit = GetNode<LineEdit>("%NickEdit");
		_nomeEdit = GetNode<LineEdit>("%NomeEdit");
		_turmaEdit = GetNode<LineEdit>("%TurmaEdit");
		_senhaEdit = GetNode<LineEdit>("%SenhaEdit");

		_nickAcessarEdit = GetNode<LineEdit>("%NickAcessarEdit");
		_senhaAcessarEdit = GetNode<LineEdit>("%SenhaAcessarEdit");

		_swatches = GetNode<HBoxContainer>("%Swatches");
		_erroLabel = GetNode<Label>("%ErroLabel");
		_jogarButton = GetNode<Button>("%JogarButton");
		_semContaButton = GetNode<Button>("%SemContaButton");
		_voltarButton = GetNode<Button>("%VoltarButton");

		_preview = GetNode<Personagem>("MarginContainer/CenterContainer/PanelContainer/VBox/CharacterRow/PreviewPanel/PreviewVBox/PreviewHolder/PreviewPersonagem");
		_previewNick = GetNode<Label>("%PreviewNick");

		GetNode<Label>("%SessaoLabel").Text = string.IsNullOrWhiteSpace(_session.roomName)
			? "Sessão do professor"
			: $"Sessão: {_session.roomName}";

		_criarButton.Pressed += OnModoCriar;
		_acessarButton.Pressed += OnModoAcessar;
		_nickEdit.TextChanged += OnNickAlterado;
		_nickAcessarEdit.TextChanged += OnNickAlterado;
		_jogarButton.Pressed += OnJogarPressed;
		_semContaButton.Pressed += OnJogarSemContaPressed;
		_voltarButton.Pressed += OnVoltarPressed;

		ConstruirPaleta();
		AtualizarPreview();
	}

	// ------------------------------------------------------------------
	// Modos: criar conta / acessar
	// ------------------------------------------------------------------

	private void OnModoCriar()
	{
		_modoCriar = true;
		_formCriar.Visible = true;
		_formAcessar.Visible = false;
		LimparErro();
	}

	private void OnModoAcessar()
	{
		_modoCriar = false;
		_formCriar.Visible = false;
		_formAcessar.Visible = true;
		LimparErro();
	}

	private void OnNickAlterado(string novoNick)
	{
		AtualizarPreview();
		LimparErro();
	}

	// ------------------------------------------------------------------
	// Paleta de cores do personagem
	// ------------------------------------------------------------------

	private void ConstruirPaleta()
	{
		Color corAtual = _session.GetPersonagemColor();

		for (int i = 0; i < UIHelpers.CoresPersonagem.Length; i++)
		{
			Color cor = UIHelpers.CoresPersonagem[i];
			Button botao = UIHelpers.BotaoCor(cor);
			int indice = i;

			botao.Pressed += () => OnCorEscolhida(indice);

			// Já marca a cor salva na sessão (ex.: voltou para esta tela).
			if (cor.IsEqualApprox(corAtual) || (i == 0 && _corEscolhida == Colors.White))
			{
				_corEscolhida = cor;
			}

			_swatches.AddChild(botao);
			_botoesCor.Add(botao);
		}

		UIHelpers.MarcarSelecionado(BotaoDaCor(_corEscolhida), _botoesCor.ToArray());
	}

	private void OnCorEscolhida(int indice)
	{
		_corEscolhida = UIHelpers.CoresPersonagem[indice];
		UIHelpers.MarcarSelecionado(_botoesCor[indice], _botoesCor.ToArray());
		AtualizarPreview();
	}

	private Button? BotaoDaCor(Color cor)
	{
		for (int i = 0; i < UIHelpers.CoresPersonagem.Length; i++)
		{
			if (UIHelpers.CoresPersonagem[i].IsEqualApprox(cor))
			{
				return _botoesCor[i];
			}
		}

		return null;
	}

	private void AtualizarPreview()
	{
		if (_preview == null)
		{
			return;
		}

		string nick = _modoCriar ? _nickEdit.Text : _nickAcessarEdit.Text;
		string nomeExibido = string.IsNullOrWhiteSpace(nick) ? "Seu personagem" : nick;

		// A label interna do personagem escala junto com ele (ficaria gigante
		// no preview), então mostramos o nick em uma label da própria tela.
		_preview.PlayerName = nomeExibido;
		_preview.GetNode<Label>("NameLabel").Visible = false;
		_preview.SyncColor = _corEscolhida;
		_previewNick.Text = nomeExibido;
	}

	// ------------------------------------------------------------------
	// Jogar: cria/acessa a conta e vai para o lobby
	// ------------------------------------------------------------------

	private async void OnJogarPressed()
	{
		LimparErro();
		_jogarButton.Disabled = true;

		try
		{
			Aluno? aluno = _modoCriar
				? await CriarConta()
				: await AcessarConta();

			if (aluno == null)
			{
				return; // mensagem de erro já foi mostrada
			}

			EntrarNoLobby(aluno);
		}
		catch (Exception e)
		{
			MostrarErro("Não foi possível preparar sua conta: " + e.Message);
		}
		finally
		{
			if (IsInstanceValid(this) && IsInsideTree())
			{
				_jogarButton.Disabled = false;
			}
		}
	}

	/// <summary>
	/// Entra como convidado: não cria conta, não usa o banco e não anuncia
	/// persona. O apelido é opcional; a cor escolhida continua valendo.
	/// </summary>
	private void OnJogarSemContaPressed()
	{
		string apelido = (_modoCriar ? _nickEdit.Text : _nickAcessarEdit.Text).Trim();

		if (string.IsNullOrWhiteSpace(apelido))
		{
			apelido = $"Convidado {GD.RandRange(10, 99)}";
		}

		_session.playerName = apelido;
		_session.playerNick = apelido;
		_session.personagemColorHex = _corEscolhida.ToHtml(false);
		_session.lobbyMode = LobbyMode.Client;

		if (string.IsNullOrWhiteSpace(_session.lobbyIp))
		{
			_session.lobbyIp = "127.0.0.1";
		}

		GD.Print($"[ALUNO] Entrando sem conta como \"{apelido}\".");

		Error resultado = GetTree().ChangeSceneToFile("res://Scenes/Personagem/LobbyPrincipal.tscn");

		if (resultado != Error.Ok)
		{
			MostrarErro("Não foi possível abrir o lobby: " + resultado);
		}
	}

	private async System.Threading.Tasks.Task<Aluno?> CriarConta()
	{
		string nick = _nickEdit.Text.Trim();
		string nome = _nomeEdit.Text.Trim();
		string turma = _turmaEdit.Text.Trim();
		string senha = _senhaEdit.Text;

		if (string.IsNullOrWhiteSpace(nick) || string.IsNullOrWhiteSpace(nome))
		{
			MostrarErro("Preencha o nick e o seu nome para criar a conta.");
			return null;
		}

		// O Aluno valida o nick (3 a 12 letras/números) no construtor; erros
		// chegam aqui como exceção e viram mensagem na tela.
		var novoAluno = new Aluno(nick, nome)
		{
			turma = string.IsNullOrWhiteSpace(turma) ? null : turma,
			password = string.IsNullOrWhiteSpace(senha) ? null : senha
		};

		if (await AlunoService.Existe(nick))
		{
			MostrarErro($"O nick \"{nick}\" já tem conta. Use JÁ TENHO CONTA para acessar.");
			return null;
		}

		await AlunoService.RegisterAluno(novoAluno);

		Aluno? criado = await AlunoService.LoginAluno(nick, string.IsNullOrWhiteSpace(senha) ? null : senha);

		if (criado == null)
		{
			MostrarErro("A conta foi criada, mas não foi possível acessar agora. Tente novamente em JÁ TENHO CONTA.");
			return null;
		}

		return criado;
	}

	private async System.Threading.Tasks.Task<Aluno?> AcessarConta()
	{
		string nick = _nickAcessarEdit.Text.Trim();
		string senha = _senhaAcessarEdit.Text;

		if (string.IsNullOrWhiteSpace(nick))
		{
			MostrarErro("Digite o seu nick para acessar.");
			return null;
		}

		Aluno? aluno = await AlunoService.LoginAluno(nick, string.IsNullOrWhiteSpace(senha) ? null : senha);

		if (aluno == null)
		{
			MostrarErro("Nick ou senha incorretos (ou essa conta não existe ainda).");
			return null;
		}

		return aluno;
	}

	private void EntrarNoLobby(Aluno aluno)
	{
		GD.Print($"[ALUNO] Conta pronta: {aluno.name} (id {aluno.id}). Indo para o lobby.");

		_session.playerName = aluno.name;
		_session.playerNick = aluno.username ?? string.Empty;
		_session.personagemColorHex = _corEscolhida.ToHtml(false);
		_session.lobbyMode = LobbyMode.Client;

		// Anuncia a entrada para o professor (descobre o IP na sessão).
		string professorIp = _session.lobbyIp;

		foreach (Usuario usuario in _session.usuariosNaRede)
		{
			if (usuario is Professor professor && !string.IsNullOrWhiteSpace(professor.ip))
			{
				professorIp = professor.ip;
				break;
			}
		}

		_session.lobbyIp = professorIp;

		// Liga a conta à sala (a sala mostra quem está vinculado a ela).
		_ = VincularContaASala(aluno.id);

		AlunoService.SendMyPersona(aluno.id);

		Error resultado = GetTree().ChangeSceneToFile("res://Scenes/Personagem/LobbyPrincipal.tscn");

		if (resultado != Error.Ok)
		{
			GD.PrintErr("[ALUNO] Erro ao abrir o lobby: " + resultado);
		}
	}

	private async System.Threading.Tasks.Task VincularContaASala(int alunoId)
	{
		try
		{
			await AlunoService.VincularAoLobby(alunoId, _session.salaId);
		}
		catch (Exception e)
		{
			GD.PrintErr("[ALUNO] Não foi possível vincular a conta à sala: " + e.Message);
		}
	}

	private void OnVoltarPressed()
	{
		GetTree().ChangeSceneToFile("res://Scenes/AlunoEntrar.tscn");
	}

	// ------------------------------------------------------------------
	// Mensagens
	// ------------------------------------------------------------------

	private void MostrarErro(string mensagem)
	{
		GD.PrintErr("[ALUNO] " + mensagem);
		_erroLabel.Text = mensagem;
		_erroLabel.Visible = true;
	}

	private void LimparErro()
	{
		_erroLabel.Visible = false;
	}
}
