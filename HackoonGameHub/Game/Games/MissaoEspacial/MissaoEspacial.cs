#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Game.Scripts;
using Game.Scripts.Games;
using Models;

namespace Game.Games;

/// <summary>
/// Missão Espacial — porte do jogo de matemática para o ciclo do Hackoon Hub.
///
/// O jogo original (menu de planetas + operações + meteoros + boss) continua
/// sendo a autoridade das regras: vidas, pontuação, meteoros e progressão
/// ficam em <c>Main</c> e nas classes dos planetas. O que o Hub acrescenta:
///
/// - a partida roda dentro de uma porta própria (7778+), isolada do lobby;
/// - o dono da porta escolhe o planeta e todos recebem a MESMA semente, então
///   as operações são iguais (competição justa) sem mudar a jogabilidade;
/// - cada aluno joga no seu ritmo; o host monta o placar ao vivo da turma;
/// - ao concluir, o placar é salvo no sistema de pontos (histórico + XP/nível).
///
/// A interface do jogo original é 1280×720 e é embutida em um SubViewport
/// com letterbox, para caber em qualquer resolução do Hub.
/// </summary>
public partial class MissaoEspacial : MinigameBase
{
    public override string GameId => "missao-espacial";

    private const int PlanetaTerra = 0;

    private static readonly string[] NomesDosPlanetas =
    {
        "Terra", "Lua", "Marte", "Júpiter", "Boss"
    };

    // ---------------------------------------------------------------------
    // Jogo original
    // ---------------------------------------------------------------------
    private Main? _missao;
    private SubViewport? _viewport;
    private Control? _areaJogo;
    private SubViewportContainer? _containerJogo;

    // ---------------------------------------------------------------------
    // Interface do Hub
    // ---------------------------------------------------------------------
    private Control? _waiting;
    private Label? _waitingTitulo;
    private Label? _waitingDetalhe;
    private Label? _statusLabel;
    private Label? _placarLabel;

    // ---------------------------------------------------------------------
    // Rede / porta
    // ---------------------------------------------------------------------
    private bool _souHost;
    private int _porta;
    private string _nomeLocal = "Jogador";
    private int _idAluno;
    private string _partidaBase = string.Empty;
    private bool _registrado;

    private bool _missaoIniciada;
    private int _planetaAtual = -1;
    private int _semente;

    private readonly Dictionary<long, string> _nomes = new();
    private readonly Dictionary<long, int> _pontos = new();
    private readonly HashSet<long> _finalizados = new();

    /// <summary>Evita salvar duas vezes a mesma missão/planeta na mesma porta.</summary>
    private readonly HashSet<string> _missoesSalvas = new();

    private bool _fimAvisado;

    /// <summary>Usado pelos testes headless para abrir um planeta sem clique.</summary>
    private float _autoStartDelay = -1f;
    private int _autoStartPlanet = PlanetaTerra;

    public Main? Missao => _missao;

    // ---------------------------------------------------------------------
    // Ciclo de vida
    // ---------------------------------------------------------------------

    public override void Iniciar(GameContexto contexto)
    {
        base.Iniciar(contexto);

        _souHost = contexto.SouHost;
        _porta = contexto.Porta;
        _nomeLocal = string.IsNullOrWhiteSpace(contexto.Jogador) ? "Jogador" : contexto.Jogador;
        _idAluno = contexto.IdAluno;
        _partidaBase = string.IsNullOrWhiteSpace(contexto.PartidaId)
            ? $"{GameId}:{_porta}"
            : contexto.PartidaId;

        ConstruirInterface();
        MontarMissao();

        if (_missao != null)
        {
            _missao.VoltarAoHub = true;
            _missao.PermitirNavegacaoLocal = _souHost;
            _missao.MissaoIniciada += OnMissaoIniciada;
            _missao.MissaoReiniciada += OnMissaoReiniciada;
            _missao.PlanetaConcluido += OnPlanetaConcluido;
            _missao.PlanetaDerrotado += OnPlanetaDerrotado;
            _missao.MenuAberto += OnMenuAberto;
            _missao.SairSolicitado += PedirSaida;
        }

        if (_souHost)
        {
            _nomes[1] = _nomeLocal;
            _pontos[1] = 0;
            Multiplayer.PeerDisconnected += OnPeerDisconnected;
            EsconderEspera();
            AtualizarStatus($"Porta {_porta} aberta. Escolha um planeta para começar.");
            AvisarStatus($"Você abriu a porta {_porta}. A missão espera por você e pela turma.");
            PrepararAtalhoDeTeste();
        }
        else
        {
            MostrarEspera("Conectando à missão...", $"Porta {_porta}");
            Multiplayer.ConnectedToServer += OnConnectedToServer;
            Multiplayer.ConnectionFailed += OnConnectionFailed;
            Multiplayer.ServerDisconnected += OnServerDisconnected;
            CallDeferred(nameof(RegistrarNoServidor));
            AvisarStatus($"Conectando na porta {_porta}...");
        }
    }

    public override void Encerrar()
    {
        Resized -= AgendarAjusteEscala;

        if (_areaJogo != null)
        {
            _areaJogo.Resized -= AgendarAjusteEscala;
        }

        if (_missao != null)
        {
            _missao.MissaoIniciada -= OnMissaoIniciada;
            _missao.MissaoReiniciada -= OnMissaoReiniciada;
            _missao.PlanetaConcluido -= OnPlanetaConcluido;
            _missao.PlanetaDerrotado -= OnPlanetaDerrotado;
            _missao.MenuAberto -= OnMenuAberto;
            _missao.SairSolicitado -= PedirSaida;
        }

        if (_souHost)
        {
            Multiplayer.PeerDisconnected -= OnPeerDisconnected;
        }
        else
        {
            Multiplayer.ConnectedToServer -= OnConnectedToServer;
            Multiplayer.ConnectionFailed -= OnConnectionFailed;
            Multiplayer.ServerDisconnected -= OnServerDisconnected;
        }

        base.Encerrar();
    }

    // ---------------------------------------------------------------------
    // Interface
    // ---------------------------------------------------------------------

    private void ConstruirInterface()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var fundo = new ColorRect
        {
            Color = new Color(0.035f, 0.045f, 0.075f, 1f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        fundo.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(fundo);

        // Barra fina de status/placar acima do jogo.
        var barra = new PanelContainer
        {
            Name = "BarraHub",
            MouseFilter = MouseFilterEnum.Stop
        };
        barra.SetAnchorsPreset(LayoutPreset.TopWide);
        barra.OffsetBottom = 38;
        barra.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.08f, 0.13f, 0.98f),
            BorderColor = new Color(0.561f, 0.427f, 0.922f, 0.8f),
            BorderWidthBottom = 2
        });
        AddChild(barra);

        var linha = new HBoxContainer();
        linha.AddThemeConstantOverride("separation", 14);
        barra.AddChild(linha);

        var margem = new MarginContainer();
        margem.AddThemeConstantOverride("margin_left", 14);
        margem.AddThemeConstantOverride("margin_right", 14);
        margem.AddThemeConstantOverride("margin_top", 5);
        margem.AddThemeConstantOverride("margin_bottom", 5);
        margem.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        linha.AddChild(margem);

        var conteudo = new HBoxContainer();
        conteudo.AddThemeConstantOverride("separation", 14);
        margem.AddChild(conteudo);

        _statusLabel = UIHelpers.Texto("MISSÃO ESPACIAL", 13);
        _statusLabel.AddThemeColorOverride("font_color", UIHelpers.AmareloClaro);
        _statusLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _statusLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        conteudo.AddChild(_statusLabel);

        _placarLabel = UIHelpers.Texto("PLACAR —", 12);
        _placarLabel.AddThemeColorOverride("font_color", UIHelpers.VerdeOk);
        _placarLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _placarLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        conteudo.AddChild(_placarLabel);

        // A UI original é 1280×720. Mantemos o SubViewport nesse tamanho e
        // escalamos o container para caber na área disponível (letterbox).
        // Não usamos SubViewportContainer.Stretch, porque ele redimensiona o
        // SubViewport para o tamanho da tela e cortaria a UI fixa do jogo.
        _areaJogo = new Control
        {
            Name = "AreaJogo",
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Stop
        };
        _areaJogo.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _areaJogo.OffsetTop = 38;
        AddChild(_areaJogo);

        _containerJogo = new SubViewportContainer
        {
            Name = "ContainerJogo",
            Stretch = false,
            CustomMinimumSize = new Vector2(1280, 720),
            Size = new Vector2(1280, 720),
            MouseFilter = MouseFilterEnum.Stop
        };
        _areaJogo.AddChild(_containerJogo);

        _viewport = new SubViewport
        {
            Size = new Vector2I(1280, 720),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always
        };
        _containerJogo.AddChild(_viewport);

        Resized += AgendarAjusteEscala;
        _areaJogo.Resized += AgendarAjusteEscala;
        CallDeferred(nameof(AjustarEscalaDoJogo));

        ConstruirEspera();
    }

    private void AgendarAjusteEscala()
    {
        CallDeferred(nameof(AjustarEscalaDoJogo));
    }

    /// <summary>
    /// Encaixa o jogo 1280×720 na área disponível sem cortar nada. A sobra
    /// vira letterbox (barras escuras) em cima/embaixo ou nas laterais.
    /// </summary>
    private void AjustarEscalaDoJogo()
    {
        if (_areaJogo == null || _containerJogo == null)
        {
            return;
        }

        Vector2 area = _areaJogo.Size;

        if (area.X <= 1f || area.Y <= 1f)
        {
            return;
        }

        float escala = Mathf.Min(area.X / 1280f, area.Y / 720f);
        escala = Mathf.Max(escala, 0.1f);

        _containerJogo.Scale = new Vector2(escala, escala);
        _containerJogo.Position = new Vector2(
            (area.X - 1280f * escala) / 2f,
            (area.Y - 720f * escala) / 2f);

        GD.Print($"[MISSÃO] Encaixe: tela {GetViewportRect().Size}, área {area}, escala {escala:F2}.");
    }

    private void ConstruirEspera()
    {
        _waiting = new Control
        {
            MouseFilter = MouseFilterEnum.Stop
        };
        _waiting.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_waiting);

        var dim = new ColorRect
        {
            Color = new Color(0.025f, 0.035f, 0.065f, 0.94f),
            MouseFilter = MouseFilterEnum.Stop
        };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _waiting.AddChild(dim);

        var centro = new CenterContainer();
        centro.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _waiting.AddChild(centro);

        var cartao = UIHelpers.Cartao(UIHelpers.EstiloCartao(
            new Color(0.07f, 0.09f, 0.15f),
            new Color(0.561f, 0.427f, 0.922f),
            24, 16));
        cartao.CustomMinimumSize = new Vector2(520, 0);
        centro.AddChild(cartao);

        var coluna = UIHelpers.Coluna(8);
        cartao.AddChild(coluna);

        _waitingTitulo = UIHelpers.Texto("AGUARDANDO A MISSÃO", 26);
        _waitingTitulo.AddThemeColorOverride("font_color", UIHelpers.AmareloClaro);
        _waitingTitulo.HorizontalAlignment = HorizontalAlignment.Center;
        coluna.AddChild(_waitingTitulo);

        _waitingDetalhe = UIHelpers.TextoSuaveLabel(
            "O dono da porta está preparando a missão.", 15);
        _waitingDetalhe.HorizontalAlignment = HorizontalAlignment.Center;
        _waitingDetalhe.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        coluna.AddChild(_waitingDetalhe);

        Label dica = UIHelpers.TextoSuaveLabel(
            "Todos jogam no mesmo planeta e com as mesmas operações. " +
            "Use FECHAR JOGO (Esc) para voltar ao lobby.", 12);
        dica.HorizontalAlignment = HorizontalAlignment.Center;
        dica.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        coluna.AddChild(dica);
    }

    private void MontarMissao()
    {
        if (_missao != null || _viewport == null)
        {
            return;
        }

        _missao = new Main
        {
            Name = "MissaoOriginal"
        };

        _missao.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _viewport.AddChild(_missao);

        GD.Print("[MISSÃO] Jogo original embutido no SubViewport 1280x720.");
    }

    private void MostrarEspera(string titulo, string detalhe)
    {
        if (_waiting == null)
        {
            return;
        }

        _waiting.Visible = true;

        if (_waitingTitulo != null)
        {
            _waitingTitulo.Text = titulo;
        }

        if (_waitingDetalhe != null)
        {
            _waitingDetalhe.Text = detalhe;
        }
    }

    private void EsconderEspera()
    {
        if (_waiting != null)
        {
            _waiting.Visible = false;
        }
    }

    private void AtualizarStatus(string mensagem)
    {
        if (_statusLabel != null)
        {
            _statusLabel.Text = mensagem;
        }
    }

    private void AtualizarPlacar(string placar)
    {
        if (_placarLabel != null)
        {
            _placarLabel.Text = string.IsNullOrWhiteSpace(placar) ? "PLACAR —" : "PLACAR • " + placar;
        }
    }

    // ---------------------------------------------------------------------
    // Fluxo da missão (host manda; clientes seguem)
    // ---------------------------------------------------------------------

    private void OnMissaoIniciada(int planeta, int semente)
    {
        _missaoIniciada = true;
        _planetaAtual = planeta;
        _semente = semente;
        _fimAvisado = false;
        _finalizados.Clear();

        foreach (long id in _nomes.Keys.ToList())
        {
            _pontos[id] = 0;
        }

        EsconderEspera();
        AtualizarStatus($"{NomePlaneta(planeta)} • missão em andamento");
        AtualizarPlacar(MontarRanking());
        AvisarStatus($"Missão iniciada no planeta {NomePlaneta(planeta)} (semente {semente}).");
        GD.Print($"[MISSÃO] Iniciada no planeta {NomePlaneta(planeta)} (seed {semente}) na porta {_porta}.");

        if (_souHost && Multiplayer.GetPeers().Length > 0)
        {
            Rpc(nameof(ReceberMissaoIniciada), planeta, semente);
        }
    }

    private void OnMissaoReiniciada(int planeta, int semente)
    {
        if (!_souHost)
        {
            return;
        }

        _missaoIniciada = true;
        _planetaAtual = planeta;
        _semente = semente;
        _fimAvisado = false;
        _finalizados.Clear();

        foreach (long id in _nomes.Keys.ToList())
        {
            _pontos[id] = 0;
        }

        AtualizarStatus($"{NomePlaneta(planeta)} • missão reiniciada");
        PublicarRanking();

        if (Multiplayer.GetPeers().Length > 0)
        {
            Rpc(nameof(ReceberMissaoReiniciada), planeta, semente);
        }
    }

    private void OnPlanetaConcluido(int planeta, int pontuacao)
    {
        if (!_missaoIniciada || planeta != _planetaAtual)
        {
            return;
        }

        if (_souHost)
        {
            _pontos[1] = Math.Max(_pontos.GetValueOrDefault(1), pontuacao);
            _finalizados.Add(1);
            GD.Print($"[MISSÃO] Host concluiu {NomePlaneta(planeta)} com {pontuacao} pts.");
            _ = SalvarPontuacaoAsync(planeta, pontuacao);
            PublicarRanking();
            AvaliarFimDaTurma();
        }
        else
        {
            GD.Print($"[MISSÃO] Convidado concluiu {NomePlaneta(planeta)} com {pontuacao} pts.");
            _ = SalvarPontuacaoAsync(planeta, pontuacao);

            if (Multiplayer.GetPeers().Length > 0)
            {
                RpcId(1, nameof(InformarResultado), planeta, pontuacao);
            }
        }
    }

    private void OnPlanetaDerrotado(int planeta)
    {
        if (!_missaoIniciada || planeta != _planetaAtual)
        {
            return;
        }

        AtualizarStatus($"{NomePlaneta(planeta)} • você perdeu as vidas. Pode tentar de novo.");
        AvisarStatus("Você perdeu as vidas. Tente novamente!");

        if (_souHost)
        {
            _finalizados.Add(1);
            PublicarRanking();
            AvaliarFimDaTurma();
        }
        else if (Multiplayer.GetPeers().Length > 0)
        {
            RpcId(1, nameof(InformarDerrota), planeta);
        }
    }

    private void OnMenuAberto()
    {
        if (!_souHost)
        {
            MostrarEspera(
                "AGUARDANDO O DONO",
                "O dono da porta escolhe o próximo planeta. A missão abre aqui automaticamente.");
        }
    }

    // ---------------------------------------------------------------------
    // Pontuação
    // ---------------------------------------------------------------------

    private async Task SalvarPontuacaoAsync(int planeta, int pontuacao)
    {
        string chave = $"{_partidaBase}:{planeta}";

        if (!_missoesSalvas.Add(chave))
        {
            return;
        }

        try
        {
            ResultadoPontuacao? resultado = await RegistrarPontuacaoAsync(
                pontuacao,
                sufixoPartida: planeta.ToString(),
                detalhes: $"{NomePlaneta(planeta)} • {pontuacao} pontos");

            if (resultado != null)
            {
                AtualizarStatus(resultado.Mensagem);
                AvisarStatus("Missão concluída! " + resultado.Mensagem);
            }
            else if (_idAluno > 0)
            {
                AtualizarStatus("Missão concluída, mas não foi possível salvar os pontos agora.");
            }
            else
            {
                AtualizarStatus("Missão concluída! Entre com uma conta para salvar pontos.");
            }
        }
        catch (Exception e)
        {
            GD.PrintErr("[MISSÃO] Falha ao registrar os pontos: " + e.Message);
            AtualizarStatus("Missão concluída, mas houve um erro ao salvar os pontos.");
        }
    }

    // ---------------------------------------------------------------------
    // Rede: entrada e placar
    // ---------------------------------------------------------------------

    private void RegistrarNoServidor()
    {
        if (_souHost || _registrado)
        {
            return;
        }

        MultiplayerPeer? peer = Multiplayer.MultiplayerPeer;

        if (peer == null || peer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected)
        {
            return;
        }

        _registrado = true;
        RpcId(1, nameof(RegistrarJogador), _nomeLocal);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RegistrarJogador(string nome)
    {
        if (!_souHost)
        {
            return;
        }

        long id = Multiplayer.GetRemoteSenderId();

        if (id <= 0)
        {
            return;
        }

        _nomes[id] = string.IsNullOrWhiteSpace(nome) ? $"Jogador {id}" : nome.Trim();
        _pontos.TryAdd(id, 0);

        if (_missaoIniciada)
        {
            RpcId(id, nameof(ReceberMissaoIniciada), _planetaAtual, _semente);
        }
        else
        {
            RpcId(id, nameof(ReceberAguardando), _nomeLocal);
        }

        RpcId(id, nameof(ReceberRanking), MontarRanking());

        GD.Print($"[MISSÃO] Jogador '{_nomes[id]}' entrou na porta {_porta} " +
                 $"(missão iniciada: {_missaoIniciada}, planeta {_planetaAtual}, seed {_semente}).");
        AvisarStatus($"{_nomes[id]} entrou na missão.");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceberAguardando(string nomeHost)
    {
        GD.Print($"[MISSÃO] Aguardando '{nomeHost}' iniciar a missão.");
        MostrarEspera(
            "AGUARDANDO A MISSÃO",
            $"{nomeHost} ainda está escolhendo o planeta. A missão abre aqui automaticamente.");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceberMissaoIniciada(int planeta, int semente)
    {
        if (_souHost)
        {
            return;
        }

        GD.Print($"[MISSÃO] Missão recebida: {NomePlaneta(planeta)} (seed {semente}).");

        _missaoIniciada = true;
        _planetaAtual = planeta;
        _semente = semente;
        _fimAvisado = false;
        _finalizados.Clear();
        _pontos.Clear();

        EsconderEspera();
        AtualizarStatus($"{NomePlaneta(planeta)} • missão em andamento");
        AvisarStatus($"Missão recebida: {NomePlaneta(planeta)}.");

        _missao?.AbrirMissaoRecebida(planeta, semente);

        // O AbrirMissaoRecebida dispara MenuAberto no meio do caminho; fecha
        // de novo para o convidado ver o planeta que o host escolheu.
        EsconderEspera();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceberMissaoReiniciada(int planeta, int semente)
    {
        if (_souHost)
        {
            return;
        }

        _missaoIniciada = true;
        _planetaAtual = planeta;
        _semente = semente;
        _fimAvisado = false;
        _finalizados.Clear();
        _pontos.Clear();

        EsconderEspera();
        AtualizarStatus($"{NomePlaneta(planeta)} • missão reiniciada");
        _missao?.AbrirMissaoRecebida(planeta, semente);
        EsconderEspera();
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void InformarResultado(int planeta, int pontuacao)
    {
        if (!_souHost || !_missaoIniciada || planeta != _planetaAtual)
        {
            return;
        }

        long id = Multiplayer.GetRemoteSenderId();

        if (id <= 0)
        {
            return;
        }

        if (!_nomes.ContainsKey(id))
        {
            _nomes[id] = $"Jogador {id}";
        }

        _pontos[id] = Math.Max(_pontos.GetValueOrDefault(id), Math.Max(0, pontuacao));
        _finalizados.Add(id);
        PublicarRanking();
        AvaliarFimDaTurma();
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void InformarDerrota(int planeta)
    {
        if (!_souHost || !_missaoIniciada || planeta != _planetaAtual)
        {
            return;
        }

        long id = Multiplayer.GetRemoteSenderId();

        if (id <= 0)
        {
            return;
        }

        _finalizados.Add(id);
        PublicarRanking();
        AvaliarFimDaTurma();
    }

    private void OnPeerDisconnected(long id)
    {
        if (!_souHost)
        {
            return;
        }

        _nomes.Remove(id);
        _pontos.Remove(id);
        _finalizados.Remove(id);
        PublicarRanking();
        AvaliarFimDaTurma();
    }

    private void PublicarRanking()
    {
        if (!_souHost)
        {
            return;
        }

        string ranking = MontarRanking();
        AtualizarPlacar(ranking);
        AvisarPlacar(ranking);

        if (Multiplayer.GetPeers().Length > 0)
        {
            Rpc(nameof(ReceberRanking), ranking);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceberRanking(string ranking)
    {
        AtualizarPlacar(ranking);
    }

    private void AvaliarFimDaTurma()
    {
        if (!_souHost || !_missaoIniciada || _fimAvisado)
        {
            return;
        }

        if (_nomes.Count == 0 || _finalizados.Count < _nomes.Count)
        {
            return;
        }

        _fimAvisado = true;
        string ranking = MontarRanking();
        AtualizarStatus("Missão da turma concluída!");
        AtualizarPlacar(ranking);
        AvisarFim("Missão concluída! " + ranking);

        if (Multiplayer.GetPeers().Length > 0)
        {
            Rpc(nameof(ReceberFim), ranking);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceberFim(string ranking)
    {
        AtualizarPlacar(ranking);
        AtualizarStatus("Missão da turma concluída!");
    }

    private string MontarRanking()
    {
        if (_nomes.Count == 0)
        {
            return "sem jogadores";
        }

        return string.Join("   •   ", _nomes
            .OrderByDescending(p => _pontos.GetValueOrDefault(p.Key))
            .ThenBy(p => p.Value)
            .Take(6)
            .Select(p =>
            {
                string marcador = _finalizados.Contains(p.Key) ? "✔ " : string.Empty;
                return $"{marcador}{p.Value}: {_pontos.GetValueOrDefault(p.Key)} pts";
            }));
    }

    // ---------------------------------------------------------------------
    // Eventos de conexão do cliente
    // ---------------------------------------------------------------------

    private void OnConnectedToServer()
    {
        GD.Print($"[MISSÃO] Conectado à porta {_porta}.");
        AtualizarStatus("Conectado • aguardando o dono iniciar");
        AvisarStatus($"Conectado à porta {_porta}.");
        RegistrarNoServidor();
    }

    private void OnConnectionFailed()
    {
        GD.PrintErr($"[MISSÃO] Falha ao conectar na porta {_porta}.");
        MostrarEspera("NÃO FOI POSSÍVEL CONECTAR",
            "Confira se o dono da porta ainda está no lobby e tente entrar de novo.");
        AvisarStatus("Não foi possível conectar à missão.");
    }

    private void OnServerDisconnected()
    {
        GD.Print($"[MISSÃO] O dono da porta {_porta} desconectou.");
        MostrarEspera("O DONO SAIU",
            "A porta da missão foi encerrada. Feche o jogo para voltar ao lobby.");
        AvisarStatus("O dono da missão desconectou.");
    }

    private static string NomePlaneta(int planeta)
    {
        return planeta >= 0 && planeta < NomesDosPlanetas.Length
            ? NomesDosPlanetas[planeta]
            : $"Planeta {planeta}";
    }

    // ---------------------------------------------------------------------
    // Atalho de teste headless (não aparece para o jogador)
    // ---------------------------------------------------------------------

    private void PrepararAtalhoDeTeste()
    {
        foreach (string argumento in OS.GetCmdlineUserArgs())
        {
            if (argumento == "--missao-auto")
            {
                _autoStartDelay = 2.0f;
            }
            else if (argumento.StartsWith("--missao-planeta=") &&
                     int.TryParse(argumento.Substring("--missao-planeta=".Length), out int planeta))
            {
                _autoStartPlanet = Math.Clamp(planeta, 0, 4);
            }
        }
    }

    public override void _Process(double delta)
    {
        if (_autoStartDelay < 0f)
        {
            return;
        }

        _autoStartDelay -= (float)delta;

        if (_autoStartDelay > 0f || !_souHost)
        {
            return;
        }

        _autoStartDelay = -1f;
        _missao?.SelecionarPlaneta(_autoStartPlanet);
        _missao?.IniciarMissaoSelecionada();
    }
}
