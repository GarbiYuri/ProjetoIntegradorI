using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using Game.Scripts;
using Game.Scripts.Games;

namespace Game.Games;

/// <summary>Uma atitude do banco do jogo original (título + imagem + correta).</summary>
public sealed class Atitude
{
    public string Titulo { get; set; } = string.Empty;
    public string Imagem { get; set; } = string.Empty;
    public bool Boa { get; set; }
}

/// <summary>
/// Regras de Convivência — porte do jogo original para o ciclo do Hackoon.
///
/// O jogo mantém o conteúdo original (10 atitudes, 5 por partida, escolha
/// BOA/RUIM, feedback e placar). O que foi acrescentado é o ciclo do Hub:
/// roda dentro do lobby, em uma porta própria (7778+), com placar ao vivo e
/// sincronização entre os jogadores da mesma porta. Não precisa de conta.
/// </summary>
public partial class RegrasDeConvivencia : MinigameBase
{
    public override string GameId => "regras-convivencia";

    private const int PerguntasPorPartida = 5;
    private const float TempoDaRodada = 15.0f;

    private enum Fase
    {
        Preparando,
        Rodada,
        Resultado,
        Fim
    }

    // ---------------------------------------------------------------------
    // Dados do jogo original
    // ---------------------------------------------------------------------
    private readonly List<Atitude> _banco = new();
    private readonly Random _rand = new();

    private List<Atitude> _sequencia = new();

    // ---------------------------------------------------------------------
    // Rede (a porta do jogo tem uma API própria, isolada do lobby)
    // ---------------------------------------------------------------------
    private bool _souHost;
    private int _porta;
    private string _nomeLocal = "Jogador";
    private bool _registrado;

    private readonly Dictionary<long, string> _nomes = new();
    private readonly Dictionary<long, int> _pontos = new();
    private readonly Dictionary<long, bool> _respostas = new();
    private readonly HashSet<long> _responderam = new();

    private int _rodadaAtual;
    private bool? _minhaEscolha;
    private Fase _fase = Fase.Preparando;
    private bool _preparando;
    private Tween? _flashTween;

    // ---------------------------------------------------------------------
    // Interface (construída em código para caber em qualquer resolução)
    // ---------------------------------------------------------------------
    private Control? _areaJogo;
    private TextureRect? _imagem;
    private Label? _titulo;
    private Label? _subtitulo;
    private Label? _rodadaLabel;
    private Label? _portaLabel;
    private Label? _statusLabel;
    private Label? _tempoLabel;
    private Label? _pergunta;
    private Label? _atitude;
    private Label? _feedback;
    private Label? _rodape;
    private Label? _responderamLabel;
    private Button? _boa;
    private Button? _ruim;
    private VBoxContainer? _jogadoresList;
    private ColorRect? _flash;
    private Timer? _roundTimer;

    private Control? _fimOverlay;
    private Label? _fimTitulo;
    private Label? _fimResumo;
    private VBoxContainer? _fimPlacar;
    private Button? _reiniciarButton;

    // ---------------------------------------------------------------------
    // Ciclo de vida
    // ---------------------------------------------------------------------

    public override void Iniciar(GameContexto contexto)
    {
        base.Iniciar(contexto);

        _souHost = contexto.SouHost;
        _porta = contexto.Porta;
        _nomeLocal = string.IsNullOrWhiteSpace(contexto.Jogador) ? "Jogador" : contexto.Jogador;

        MontarBanco();
        ConstruirUi();

        AtualizarCabecalho();

        if (_souHost)
        {
            _nomes[1] = _nomeLocal;
            _pontos[1] = 0;
            Multiplayer.PeerDisconnected += OnPeerDisconnected;
            CallDeferred(nameof(PrepararPartida));
        }
        else
        {
            Multiplayer.ConnectedToServer += OnConnectedToServer;
            Multiplayer.ConnectionFailed += OnConnectionFailed;
            Multiplayer.ServerDisconnected += OnServerDisconnected;
            CallDeferred(nameof(RegistrarNoServidor));
        }

        AvisarStatus(_souHost
            ? $"Você abriu a porta {_porta}. Os colegas já podem entrar."
            : $"Conectando na porta {_porta}...");
    }

    public override void Encerrar()
    {
        if (_roundTimer != null)
        {
            _roundTimer.Stop();
            _roundTimer.Timeout -= OnRoundTimeout;
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
    // Banco de atitudes (conteúdo original do jogo, caminhos corrigidos)
    // ---------------------------------------------------------------------

    private void MontarBanco()
    {
        const string Base = "res://Games/RegrasDeConvivencia/imagens/";

        _banco.Clear();
        _banco.Add(new Atitude { Titulo = "Levantar a mão para perguntar", Imagem = Base + "levantarmao.png", Boa = true });
        _banco.Add(new Atitude { Titulo = "Dividir os brinquedos", Imagem = Base + "dividindo.png", Boa = true });
        _banco.Add(new Atitude { Titulo = "Empurrar o colega", Imagem = Base + "empurrar.png", Boa = false });
        _banco.Add(new Atitude { Titulo = "Dizer palavras feias", Imagem = Base + "palavrao.png", Boa = false });
        _banco.Add(new Atitude { Titulo = "Usar palavras mágicas", Imagem = Base + "palavras_magicas.png", Boa = true });
        _banco.Add(new Atitude { Titulo = "Respeitar e ajudar o colega", Imagem = Base + "ajudar_colega.png", Boa = true });
        _banco.Add(new Atitude { Titulo = "Jogar o lixo na lixeira", Imagem = Base + "lixo_lixeira.png", Boa = true });
        _banco.Add(new Atitude { Titulo = "Correr no corredor", Imagem = Base + "correr_corredor.png", Boa = false });
        _banco.Add(new Atitude { Titulo = "Entrar na sala empurrando", Imagem = Base + "entrar_empurrando.png", Boa = false });
        _banco.Add(new Atitude { Titulo = "Gritar no meio da sala de aula", Imagem = Base + "gritar_sala.png", Boa = false });
    }

    private List<Atitude> SortearAtitudes()
    {
        return _banco
            .OrderBy(_ => _rand.Next())
            .Take(PerguntasPorPartida)
            .ToList();
    }

    // ---------------------------------------------------------------------
    // Partida (host autoritativo)
    // ---------------------------------------------------------------------

    private async void PrepararPartida()
    {
        if (!_souHost || _preparando)
        {
            return;
        }

        _preparando = true;

        // Dá um instante para os primeiros colegas conectarem antes de
        // distribuir as atitudes.
        await ToSignal(GetTree().CreateTimer(0.7), SceneTreeTimer.SignalName.Timeout);
        _preparando = false;

        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        _sequencia = SortearAtitudes();
        _rodadaAtual = 0;
        _respostas.Clear();
        _responderam.Clear();
        _minhaEscolha = null;
        _fase = Fase.Preparando;

        foreach (long id in _nomes.Keys.ToList())
        {
            _pontos[id] = 0;
        }

        if (_pontos.Count == 0)
        {
            _pontos[1] = 0;
        }

        _areaJogo!.Visible = true;
        _fimOverlay!.Visible = false;

        Rpc(nameof(ReceberPartida),
            JsonSerializer.Serialize(_sequencia),
            JsonSerializer.Serialize(_nomes),
            JsonSerializer.Serialize(_pontos));
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceberPartida(string sequenciaJson, string nomesJson, string pontosJson)
    {
        _sequencia = Desserializar<List<Atitude>>(sequenciaJson) ?? new List<Atitude>();
        _nomes.Clear();
        AdicionarTodos(_nomes, Desserializar<Dictionary<long, string>>(nomesJson));
        _pontos.Clear();
        AdicionarTodos(_pontos, Desserializar<Dictionary<long, int>>(pontosJson));

        _responderam.Clear();
        _minhaEscolha = null;
        _fase = Fase.Preparando;

        if (_areaJogo != null) _areaJogo.Visible = true;
        if (_fimOverlay != null) _fimOverlay.Visible = false;
        if (_feedback != null) _feedback.Text = string.Empty;

        AtualizarCabecalho();
        AtualizarPlacar();

        if (_souHost)
        {
            CallDeferred(nameof(IniciarRodada));
        }
    }

    private void IniciarRodada()
    {
        if (!_souHost || _fase == Fase.Fim)
        {
            return;
        }

        if (_rodadaAtual >= _sequencia.Count)
        {
            FinalizarPartida();
            return;
        }

        _respostas.Clear();
        _responderam.Clear();
        _minhaEscolha = null;
        _fase = Fase.Rodada;

        _roundTimer?.Stop();
        _roundTimer?.Start();

        Rpc(nameof(ReceberRodada), _rodadaAtual);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceberRodada(int indice)
    {
        if (indice < 0 || indice >= _sequencia.Count)
        {
            return;
        }

        _rodadaAtual = indice;
        _minhaEscolha = null;
        _fase = Fase.Rodada;

        MostrarAtitude(indice);

        if (_boa != null) _boa.Disabled = false;
        if (_ruim != null) _ruim.Disabled = false;
        if (_feedback != null) _feedback.Text = string.Empty;

        AtualizarCabecalho();
        AtualizarPlacar();
    }

    private void MostrarAtitude(int indice)
    {
        if (indice < 0 || indice >= _sequencia.Count)
        {
            return;
        }

        Atitude atitude = _sequencia[indice];

        if (_atitude != null)
        {
            _atitude.Text = atitude.Titulo;
        }

        if (_pergunta != null)
        {
            _pergunta.Text = "Essa atitude é BOA ou RUIM?";
        }

        if (_imagem != null)
        {
            _imagem.Texture = CarregarImagem(atitude.Imagem);
        }
    }

    // ---------------------------------------------------------------------
    // Respostas
    // ---------------------------------------------------------------------

    private void Responder(bool escolha)
    {
        if (_fase != Fase.Rodada || _minhaEscolha != null)
        {
            return;
        }

        _minhaEscolha = escolha;

        if (_boa != null) _boa.Disabled = true;
        if (_ruim != null) _ruim.Disabled = true;

        if (_feedback != null)
        {
            _feedback.Text = escolha
                ? "Você escolheu: é uma atitude BOA."
                : "Você escolheu: é uma atitude RUIM.";
        }

        if (_souHost)
        {
            RegistrarResposta(Multiplayer.GetUniqueId(), escolha);
        }
        else
        {
            RpcId(1, nameof(EnviarResposta), escolha);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void EnviarResposta(bool escolha)
    {
        if (!_souHost)
        {
            return;
        }

        long id = Multiplayer.GetRemoteSenderId();

        if (id <= 0)
        {
            id = Multiplayer.GetUniqueId();
        }

        RegistrarResposta(id, escolha);
    }

    private void RegistrarResposta(long id, bool escolha)
    {
        if (!_souHost || _fase != Fase.Rodada)
        {
            return;
        }

        if (!_nomes.ContainsKey(id))
        {
            _nomes[id] = $"Jogador {id}";
        }

        if (!_pontos.ContainsKey(id))
        {
            _pontos[id] = 0;
        }

        _respostas[id] = escolha;
        _responderam.Add(id);

        Rpc(nameof(ReceberResponderam), JsonSerializer.Serialize(_responderam));
        AtualizarPlacar();

        if (_respostas.Count >= _nomes.Count)
        {
            _roundTimer?.Stop();
            CallDeferred(nameof(ResolverRodada));
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceberResponderam(string idsJson)
    {
        _responderam.Clear();

        List<long>? ids = Desserializar<List<long>>(idsJson);
        if (ids != null)
        {
            foreach (long id in ids)
            {
                _responderam.Add(id);
            }
        }

        AtualizarCabecalho();
        AtualizarPlacar();
    }

    private void OnRoundTimeout()
    {
        if (!_souHost || _fase != Fase.Rodada)
        {
            return;
        }

        ResolverRodada();
    }

    private void ResolverRodada()
    {
        if (!_souHost || _fase != Fase.Rodada)
        {
            return;
        }

        if (_rodadaAtual < 0 || _rodadaAtual >= _sequencia.Count)
        {
            return;
        }

        _fase = Fase.Resultado;
        _roundTimer?.Stop();

        bool correta = _sequencia[_rodadaAtual].Boa;
        var acertos = new Dictionary<long, bool>();

        foreach (long id in _nomes.Keys.ToList())
        {
            bool acertou = _respostas.TryGetValue(id, out bool escolha) && escolha == correta;
            acertos[id] = acertou;

            if (acertou)
            {
                _pontos[id] = _pontos.GetValueOrDefault(id) + 10;
            }
        }

        var resultado = new ResultadoRodada
        {
            Rodada = _rodadaAtual,
            Correta = correta,
            Acertos = acertos,
            Pontos = new Dictionary<long, int>(_pontos)
        };

        Rpc(nameof(ReceberResultado), JsonSerializer.Serialize(resultado));
        AguardarProximaRodada();
    }

    private async void AguardarProximaRodada()
    {
        await ToSignal(GetTree().CreateTimer(2.6), SceneTreeTimer.SignalName.Timeout);

        if (!IsInstanceValid(this) || !IsInsideTree() || _fase != Fase.Resultado)
        {
            return;
        }

        _rodadaAtual++;

        if (_rodadaAtual >= _sequencia.Count)
        {
            FinalizarPartida();
        }
        else
        {
            IniciarRodada();
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceberResultado(string resultadoJson)
    {
        ResultadoRodada? resultado = Desserializar<ResultadoRodada>(resultadoJson);

        if (resultado == null)
        {
            return;
        }

        _rodadaAtual = resultado.Rodada;
        _pontos.Clear();
        AdicionarTodos(_pontos, resultado.Pontos);
        _fase = Fase.Resultado;

        if (_boa != null) _boa.Disabled = true;
        if (_ruim != null) _ruim.Disabled = true;

        bool acertou = resultado.Acertos != null &&
                       resultado.Acertos.TryGetValue(Multiplayer.GetUniqueId(), out bool ok) && ok;

        string respostaCerta = resultado.Correta ? "atitude BOA" : "atitude RUIM";

        if (_feedback != null)
        {
            if (_minhaEscolha == null)
            {
                _feedback.Text = $"Tempo esgotado! A resposta certa é {respostaCerta}.";
                _feedback.AddThemeColorOverride("font_color", UIHelpers.AmareloClaro);
            }
            else if (acertou)
            {
                _feedback.Text = "Acertou! +10 pontos";
                _feedback.AddThemeColorOverride("font_color", UIHelpers.VerdeOk);
                Flash(new Color(0.15f, 0.8f, 0.35f, 0.35f));
            }
            else
            {
                _feedback.Text = $"Quase! A resposta certa é {respostaCerta}.";
                _feedback.AddThemeColorOverride("font_color", UIHelpers.VermelhoErro);
                Flash(new Color(0.9f, 0.15f, 0.2f, 0.35f));
            }
        }

        AtualizarCabecalho();
        AtualizarPlacar();
    }

    // ---------------------------------------------------------------------
    // Fim de partida
    // ---------------------------------------------------------------------

    private void FinalizarPartida()
    {
        if (!_souHost)
        {
            return;
        }

        _fase = Fase.Fim;
        _roundTimer?.Stop();

        var ranking = _nomes
            .Select(par => new LinhaPlacar
            {
                Nome = par.Value,
                Pontos = _pontos.GetValueOrDefault(par.Key)
            })
            .OrderByDescending(l => l.Pontos)
            .ThenBy(l => l.Nome)
            .ToList();

        Rpc(nameof(ReceberFim), JsonSerializer.Serialize(ranking));
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceberFim(string rankingJson)
    {
        _fase = Fase.Fim;
        _roundTimer?.Stop();

        if (_areaJogo != null) _areaJogo.Visible = false;
        if (_fimOverlay != null) _fimOverlay.Visible = true;

        List<LinhaPlacar> ranking = Desserializar<List<LinhaPlacar>>(rankingJson) ?? new List<LinhaPlacar>();

        if (_fimTitulo != null)
        {
            _fimTitulo.Text = "FIM DE JOGO!";
        }

        if (_fimResumo != null)
        {
            int total = PerguntasPorPartida;
            _fimResumo.Text = $"Confira o placar da turma ({total} atitudes por partida).";
        }

        if (_fimPlacar != null)
        {
            LimparFilhos(_fimPlacar);

            int posicao = 1;

            foreach (LinhaPlacar linha in ranking)
            {
                var cartao = UIHelpers.Cartao(UIHelpers.EstiloCartao(
                    new Color(0.11f, 0.14f, 0.2f),
                    posicao == 1 ? UIHelpers.Amarelo : UIHelpers.BordaSuave,
                    10, 8));

                var row = UIHelpers.Linha(10);
                row.AddChild(UIHelpers.Texto($"{posicao}º", 18));
                row.AddChild(UIHelpers.TextoExpandido(linha.Nome, 17));
                row.AddChild(UIHelpers.Texto($"{linha.Pontos} pts", 17));
                cartao.AddChild(row);
                _fimPlacar.AddChild(cartao);
                posicao++;
            }
        }

        if (_reiniciarButton != null)
        {
            _reiniciarButton.Text = _souHost ? "JOGAR NOVAMENTE" : "PEDIR NOVA PARTIDA";
        }

        AvisarStatus("Partida encerrada. Placar disponível.");
        AvisarFim(string.Join(" • ", ranking.Select(l => $"{l.Nome}: {l.Pontos}")));
    }

    private void OnReiniciarPressed()
    {
        if (_souHost)
        {
            PrepararPartida();
        }
        else
        {
            RpcId(1, nameof(PedirReinicio));
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PedirReinicio()
    {
        if (_souHost)
        {
            PrepararPartida();
        }
    }

    // ---------------------------------------------------------------------
    // Entrada/saída de jogadores e sincronização
    // ---------------------------------------------------------------------

    private void RegistrarNoServidor()
    {
        if (_souHost || _registrado)
        {
            return;
        }

        var peer = Multiplayer.MultiplayerPeer;

        if (peer == null || peer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected)
        {
            return;
        }

        _registrado = true;
        RpcId(1, nameof(RegistrarJogador), _nomeLocal);
    }

    private void OnConnectedToServer()
    {
        AvisarStatus($"Conectado à porta {_porta}.");
        RegistrarNoServidor();
    }

    private void OnConnectionFailed()
    {
        AvisarStatus("Não foi possível conectar à partida.");
        if (_rodape != null) _rodape.Text = "Não foi possível conectar. Feche o jogo e tente novamente.";
    }

    private void OnServerDisconnected()
    {
        AvisarStatus("O dono da partida saiu.");
        if (_rodape != null) _rodape.Text = "O dono da partida saiu. Feche o jogo para voltar ao lobby.";
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

        _nomes[id] = string.IsNullOrWhiteSpace(nome) ? $"Jogador {id}" : nome;
        _pontos.TryAdd(id, 0);

        Rpc(nameof(ReceberJogadores),
            JsonSerializer.Serialize(_nomes),
            JsonSerializer.Serialize(_pontos));

        // Estado completo para quem chegou no meio da partida.
        var estado = new EstadoJogo
        {
            Sequencia = _sequencia,
            Rodada = _rodadaAtual,
            Nomes = new Dictionary<long, string>(_nomes),
            Pontos = new Dictionary<long, int>(_pontos),
            Responderam = _responderam.ToList(),
            Fase = _fase.ToString()
        };

        RpcId(id, nameof(ReceberEstado), JsonSerializer.Serialize(estado));
        AtualizarCabecalho();
        AtualizarPlacar();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceberJogadores(string nomesJson, string pontosJson)
    {
        _nomes.Clear();
        AdicionarTodos(_nomes, Desserializar<Dictionary<long, string>>(nomesJson));
        _pontos.Clear();
        AdicionarTodos(_pontos, Desserializar<Dictionary<long, int>>(pontosJson));

        AtualizarCabecalho();
        AtualizarPlacar();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceberEstado(string estadoJson)
    {
        EstadoJogo? estado = Desserializar<EstadoJogo>(estadoJson);

        if (estado == null)
        {
            return;
        }

        _sequencia = estado.Sequencia ?? new List<Atitude>();
        _nomes.Clear();
        AdicionarTodos(_nomes, estado.Nomes);
        _pontos.Clear();
        AdicionarTodos(_pontos, estado.Pontos);
        _responderam.Clear();

        if (estado.Responderam != null)
        {
            foreach (long id in estado.Responderam)
            {
                _responderam.Add(id);
            }
        }

        _rodadaAtual = estado.Rodada;

        if (Enum.TryParse(estado.Fase, out Fase fase))
        {
            _fase = fase;
        }

        AtualizarCabecalho();
        AtualizarPlacar();

        if (_fase == Fase.Rodada && _rodadaAtual < _sequencia.Count)
        {
            ReceberRodada(_rodadaAtual);
        }
        else if (_fase == Fase.Resultado && _rodadaAtual < _sequencia.Count)
        {
            MostrarAtitude(_rodadaAtual);
            if (_boa != null) _boa.Disabled = true;
            if (_ruim != null) _ruim.Disabled = true;

            if (_feedback != null)
            {
                _feedback.Text = "Aguarde o resultado/ próxima rodada.";
            }
        }
        else if (_fase == Fase.Fim)
        {
            MostrarFimComPontos();
        }
    }

    private void MostrarFimComPontos()
    {
        if (_areaJogo != null) _areaJogo.Visible = false;
        if (_fimOverlay != null) _fimOverlay.Visible = true;

        if (_fimTitulo != null) _fimTitulo.Text = "PARTIDA ENCERRADA";
        if (_fimResumo != null) _fimResumo.Text = "Placar final da turma:";

        if (_fimPlacar != null)
        {
            LimparFilhos(_fimPlacar);

            int posicao = 1;

            foreach (var par in _pontos.OrderByDescending(p => p.Value))
            {
                string nome = _nomes.GetValueOrDefault(par.Key, $"Jogador {par.Key}");
                var cartao = UIHelpers.Cartao();
                var row = UIHelpers.Linha(10);
                row.AddChild(UIHelpers.Texto($"{posicao}º", 18));
                row.AddChild(UIHelpers.TextoExpandido(nome, 17));
                row.AddChild(UIHelpers.Texto($"{par.Value} pts", 17));
                cartao.AddChild(row);
                _fimPlacar.AddChild(cartao);
                posicao++;
            }
        }
    }

    private void OnPeerDisconnected(long id)
    {
        if (!_souHost)
        {
            return;
        }

        _nomes.Remove(id);
        _pontos.Remove(id);
        _respostas.Remove(id);
        _responderam.Remove(id);

        Rpc(nameof(ReceberJogadores),
            JsonSerializer.Serialize(_nomes),
            JsonSerializer.Serialize(_pontos));

        Rpc(nameof(ReceberResponderam), JsonSerializer.Serialize(_responderam));

        if (_fase == Fase.Rodada && _nomes.Count > 0 && _respostas.Count >= _nomes.Count)
        {
            _roundTimer?.Stop();
            CallDeferred(nameof(ResolverRodada));
        }
    }

    // ---------------------------------------------------------------------
    // Interface: construção
    // ---------------------------------------------------------------------

    private void ConstruirUi()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var fundo = new ColorRect
        {
            Color = new Color(0.043f, 0.055f, 0.082f, 1f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        fundo.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(fundo);

        var brilho = new ColorRect
        {
            Color = new Color(0.949f, 0.765f, 0.302f, 0.055f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        brilho.SetAnchorsPreset(LayoutPreset.TopWide);
        brilho.OffsetBottom = 210;
        AddChild(brilho);

        var margem = new MarginContainer { Name = "AreaJogo" };
        margem.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margem.AddThemeConstantOverride("margin_left", 28);
        margem.AddThemeConstantOverride("margin_top", 22);
        margem.AddThemeConstantOverride("margin_right", 28);
        margem.AddThemeConstantOverride("margin_bottom", 22);
        AddChild(margem);
        _areaJogo = margem;

        var raiz = new VBoxContainer();
        raiz.AddThemeConstantOverride("separation", 12);
        margem.AddChild(raiz);

        ConstruirCabecalho(raiz);
        raiz.AddChild(new HSeparator());
        ConstruirCorpo(raiz);
        ConstruirRodape(raiz);

        _flash = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        _flash.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_flash);

        ConstruirFimDeJogo();

        _roundTimer = new Timer
        {
            OneShot = true,
            WaitTime = TempoDaRodada
        };
        _roundTimer.Timeout += OnRoundTimeout;
        AddChild(_roundTimer);
    }

    private void ConstruirCabecalho(VBoxContainer raiz)
    {
        var topo = new HBoxContainer();
        topo.AddThemeConstantOverride("separation", 18);
        raiz.AddChild(topo);

        var titulos = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        titulos.AddThemeConstantOverride("separation", 2);
        topo.AddChild(titulos);

        var eyebrow = UIHelpers.Texto("JOGO DO HUB • CIDADANIA", 12);
        eyebrow.AddThemeColorOverride("font_color", UIHelpers.TextoSuave);
        titulos.AddChild(eyebrow);

        _titulo = UIHelpers.Texto("REGRAS DE CONVIVÊNCIA", 28);
        _titulo.AddThemeColorOverride("font_color", UIHelpers.AmareloClaro);
        titulos.AddChild(_titulo);

        _subtitulo = UIHelpers.TextoSuaveLabel("Escolha se a atitude é boa ou ruim. Todo mundo joga junto pela mesma porta.", 14);
        _subtitulo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        titulos.AddChild(_subtitulo);

        var info = new VBoxContainer
        {
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        info.AddThemeConstantOverride("separation", 2);
        topo.AddChild(info);

        _rodadaLabel = UIHelpers.Texto("PREPARANDO...", 16);
        _rodadaLabel.AddThemeColorOverride("font_color", UIHelpers.Amarelo);
        _rodadaLabel.HorizontalAlignment = HorizontalAlignment.Right;
        info.AddChild(_rodadaLabel);

        _portaLabel = UIHelpers.TextoSuaveLabel("PORTA —", 13);
        _portaLabel.HorizontalAlignment = HorizontalAlignment.Right;
        info.AddChild(_portaLabel);

        _statusLabel = UIHelpers.Texto("", 13);
        _statusLabel.AddThemeColorOverride("font_color", UIHelpers.VerdeOk);
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Right;
        info.AddChild(_statusLabel);

        _tempoLabel = UIHelpers.TextoSuaveLabel("", 13);
        _tempoLabel.HorizontalAlignment = HorizontalAlignment.Right;
        info.AddChild(_tempoLabel);
    }

    public override void _Process(double delta)
    {
        if (_tempoLabel == null)
        {
            return;
        }

        // O tempo é uma ajuda para a turma não travar em uma rodada; fica
        // visível para todo mundo saber quanto falta.
        _tempoLabel.Text = _fase == Fase.Rodada && _roundTimer != null
            ? $"TEMPO {Mathf.CeilToInt((float)_roundTimer.TimeLeft)}s"
            : string.Empty;
    }

    private void ConstruirCorpo(VBoxContainer raiz)
    {
        var corpo = new HBoxContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        corpo.AddThemeConstantOverride("separation", 22);
        raiz.AddChild(corpo);

        // Imagem da atitude (mesma arte do jogo original).
        var cardImagem = UIHelpers.Cartao(UIHelpers.EstiloCartao(
            new Color(0.96f, 0.96f, 0.94f),
            new Color(0.86f, 0.86f, 0.9f),
            14, 14));
        cardImagem.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        cardImagem.SizeFlagsVertical = SizeFlags.ExpandFill;
        cardImagem.CustomMinimumSize = new Vector2(420, 0);
        corpo.AddChild(cardImagem);

        var imagemMargem = new MarginContainer();
        imagemMargem.AddThemeConstantOverride("margin_left", 10);
        imagemMargem.AddThemeConstantOverride("margin_top", 10);
        imagemMargem.AddThemeConstantOverride("margin_right", 10);
        imagemMargem.AddThemeConstantOverride("margin_bottom", 10);
        cardImagem.AddChild(imagemMargem);

        _imagem = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        imagemMargem.AddChild(_imagem);

        // Lado direito: pergunta, respostas e placar.
        var lado = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        lado.AddThemeConstantOverride("separation", 10);
        corpo.AddChild(lado);

        _pergunta = UIHelpers.Texto("Essa atitude é BOA ou RUIM?", 20);
        _pergunta.AddThemeColorOverride("font_color", UIHelpers.TextoClaro);
        lado.AddChild(_pergunta);

        _atitude = UIHelpers.Texto("", 32);
        _atitude.AddThemeColorOverride("font_color", UIHelpers.AmareloClaro);
        _atitude.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _atitude.CustomMinimumSize = new Vector2(0, 86);
        lado.AddChild(_atitude);

        lado.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });

        var botoes = new HBoxContainer();
        botoes.AddThemeConstantOverride("separation", 14);
        lado.AddChild(botoes);

        _boa = CriarBotaoAcao("✔  ATITUDE BOA", new Color(0.18f, 0.62f, 0.36f));
        _boa.Pressed += () => Responder(true);
        botoes.AddChild(_boa);

        _ruim = CriarBotaoAcao("✘  ATITUDE RUIM", new Color(0.72f, 0.22f, 0.26f));
        _ruim.Pressed += () => Responder(false);
        botoes.AddChild(_ruim);

        _feedback = UIHelpers.Texto("", 19);
        _feedback.HorizontalAlignment = HorizontalAlignment.Center;
        _feedback.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _feedback.CustomMinimumSize = new Vector2(0, 30);
        lado.AddChild(_feedback);

        lado.AddChild(ConstruirPlacar());
    }

    private Control ConstruirPlacar()
    {
        var cartao = UIHelpers.Cartao();
        var coluna = UIHelpers.Coluna(6);
        cartao.AddChild(coluna);

        var header = UIHelpers.Linha(10);
        header.AddChild(UIHelpers.TextoExpandido("JOGADORES", 13));

        _responderamLabel = UIHelpers.TextoSuaveLabel("0/0 responderam", 12);
        _responderamLabel.HorizontalAlignment = HorizontalAlignment.Right;
        header.AddChild(_responderamLabel);
        coluna.AddChild(header);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 132),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        coluna.AddChild(scroll);

        _jogadoresList = UIHelpers.Coluna(4);
        _jogadoresList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_jogadoresList);

        return cartao;
    }

    private void ConstruirRodape(VBoxContainer raiz)
    {
        _rodape = UIHelpers.TextoSuaveLabel(
            "Cada rodada vale 10 pontos. A partida tem 5 atitudes. Use o botão FECHAR JOGO ou Esc para sair.",
            12);
        _rodape.HorizontalAlignment = HorizontalAlignment.Center;
        raiz.AddChild(_rodape);
    }

    private void ConstruirFimDeJogo()
    {
        _fimOverlay = new Control { Visible = false };
        _fimOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_fimOverlay);

        var dim = new ColorRect
        {
            Color = new Color(0.03f, 0.04f, 0.07f, 0.92f),
            MouseFilter = MouseFilterEnum.Stop
        };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _fimOverlay.AddChild(dim);

        var centro = new CenterContainer();
        centro.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _fimOverlay.AddChild(centro);

        var painel = UIHelpers.Cartao(UIHelpers.EstiloCartao(
            new Color(0.08f, 0.1f, 0.15f),
            UIHelpers.Amarelo,
            26, 18));
        painel.CustomMinimumSize = new Vector2(580, 0);
        centro.AddChild(painel);

        var coluna = UIHelpers.Coluna(12);
        painel.AddChild(coluna);

        _fimTitulo = UIHelpers.Texto("FIM DE JOGO!", 34);
        _fimTitulo.AddThemeColorOverride("font_color", UIHelpers.AmareloClaro);
        _fimTitulo.HorizontalAlignment = HorizontalAlignment.Center;
        coluna.AddChild(_fimTitulo);

        _fimResumo = UIHelpers.TextoSuaveLabel("Confira o placar da turma:", 15);
        _fimResumo.HorizontalAlignment = HorizontalAlignment.Center;
        coluna.AddChild(_fimResumo);

        _fimPlacar = UIHelpers.Coluna(6);
        coluna.AddChild(_fimPlacar);

        var botoes = UIHelpers.Linha(12);
        botoes.Alignment = BoxContainer.AlignmentMode.Center;
        coluna.AddChild(botoes);

        _reiniciarButton = new Button
        {
            Text = "JOGAR NOVAMENTE",
            CustomMinimumSize = new Vector2(240, 46)
        };
        _reiniciarButton.AddThemeFontSizeOverride("font_size", 17);
        _reiniciarButton.Pressed += OnReiniciarPressed;
        botoes.AddChild(_reiniciarButton);

        var sair = new Button
        {
            Text = "VOLTAR AO LOBBY",
            CustomMinimumSize = new Vector2(200, 46)
        };
        sair.AddThemeFontSizeOverride("font_size", 17);
        sair.Pressed += PedirSaida;
        botoes.AddChild(sair);
    }

    private static Button CriarBotaoAcao(string texto, Color cor)
    {
        var botao = new Button
        {
            Text = texto,
            CustomMinimumSize = new Vector2(0, 96),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FocusMode = FocusModeEnum.None
        };
        botao.AddThemeFontSizeOverride("font_size", 24);
        botao.AddThemeColorOverride("font_color", Colors.White);

        var normal = new StyleBoxFlat
        {
            BgColor = cor,
            CornerRadiusTopLeft = 14,
            CornerRadiusTopRight = 14,
            CornerRadiusBottomLeft = 14,
            CornerRadiusBottomRight = 14,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            BorderColor = cor.Lightened(0.25f)
        };

        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = cor.Lightened(0.12f);

        var pressed = (StyleBoxFlat)normal.Duplicate();
        pressed.BgColor = cor.Darkened(0.12f);

        botao.AddThemeStyleboxOverride("normal", normal);
        botao.AddThemeStyleboxOverride("hover", hover);
        botao.AddThemeStyleboxOverride("pressed", pressed);
        botao.AddThemeStyleboxOverride("disabled", normal);
        return botao;
    }

    // ---------------------------------------------------------------------
    // Interface: atualizações
    // ---------------------------------------------------------------------

    private void AtualizarCabecalho()
    {
        if (_rodadaLabel != null)
        {
            if (_sequencia.Count == 0)
            {
                _rodadaLabel.Text = "PREPARANDO...";
            }
            else
            {
                int exibida = Mathf.Clamp(_rodadaAtual + 1, 1, _sequencia.Count);
                _rodadaLabel.Text = $"RODADA {exibida} / {_sequencia.Count}";
            }
        }

        if (_portaLabel != null)
        {
            _portaLabel.Text = $"PORTA {_porta} • {(_souHost ? "DONO" : "CONECTADO")}";
        }

        if (_statusLabel != null)
        {
            _statusLabel.Text = _fase switch
            {
                Fase.Rodada => "Escolha uma opção",
                Fase.Resultado => "Resultado da rodada",
                Fase.Fim => "Partida encerrada",
                _ => "Preparando a partida..."
            };
        }

        if (_responderamLabel != null)
        {
            _responderamLabel.Text = $"{_responderam.Count}/{_nomes.Count} responderam";
        }
    }

    private void AtualizarPlacar()
    {
        if (_jogadoresList == null)
        {
            return;
        }

        LimparFilhos(_jogadoresList);

        long local = Multiplayer.GetUniqueId();

        foreach (var par in _pontos.OrderByDescending(p => p.Value).ThenBy(p => p.Key))
        {
            string nome = _nomes.GetValueOrDefault(par.Key, $"Jogador {par.Key}");
            bool eu = par.Key == local;
            bool respondeu = _responderam.Contains(par.Key);

            var linha = UIHelpers.Linha(8);
            linha.AddChild(UIHelpers.TextoExpandido(eu ? $"{nome} (você)" : nome, 14));

            if (_fase == Fase.Rodada && _nomes.Count > 1)
            {
                Label marca = UIHelpers.Texto(respondeu ? "✔" : "…", 13);
                marca.AddThemeColorOverride("font_color", respondeu ? UIHelpers.VerdeOk : UIHelpers.TextoSuave);
                linha.AddChild(marca);
            }

            linha.AddChild(UIHelpers.Texto($"{par.Value} pts", 14));
            _jogadoresList.AddChild(linha);
        }

        AtualizarCabecalho();

        // Placar ao vivo para o painel do professor (só o dono da porta
        // publica; o lobby ignora o evento nos demais clientes).
        if (_souHost)
        {
            var resumo = _pontos
                .OrderByDescending(p => p.Value)
                .ThenBy(p => p.Key)
                .Take(3)
                .Select(p => $"{_nomes.GetValueOrDefault(p.Key, $"Jogador {p.Key}")}: {p.Value} pts");

            AvisarPlacar(string.Join(" • ", resumo));
        }
    }

    private void Flash(Color cor)
    {
        if (_flash == null)
        {
            return;
        }

        _flash.Color = cor;
        _flashTween?.Kill();
        _flashTween = CreateTween();
        _flashTween.TweenProperty(_flash, "color:a", 0f, 0.55f);
    }

    // ---------------------------------------------------------------------
    // Utilitários
    // ---------------------------------------------------------------------

    private static Texture2D? CarregarImagem(string caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho) || !ResourceLoader.Exists(caminho))
        {
            return null;
        }

        return GD.Load<Texture2D>(caminho);
    }

    private static void AdicionarTodos<TChave, TValor>(Dictionary<TChave, TValor> destino,
        Dictionary<TChave, TValor>? origem) where TChave : notnull
    {
        if (origem == null)
        {
            return;
        }

        foreach (KeyValuePair<TChave, TValor> par in origem)
        {
            destino[par.Key] = par.Value;
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

    private static T? Desserializar<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (Exception e)
        {
            GD.PrintErr("[REGRAS] JSON inválido: " + e.Message);
            return default;
        }
    }

    // ---------------------------------------------------------------------
    // Estruturas de rede
    // ---------------------------------------------------------------------

    private sealed class ResultadoRodada
    {
        public int Rodada { get; set; }
        public bool Correta { get; set; }
        public Dictionary<long, bool>? Acertos { get; set; }
        public Dictionary<long, int>? Pontos { get; set; }
    }

    private sealed class EstadoJogo
    {
        public List<Atitude>? Sequencia { get; set; }
        public int Rodada { get; set; }
        public Dictionary<long, string>? Nomes { get; set; }
        public Dictionary<long, int>? Pontos { get; set; }
        public List<long>? Responderam { get; set; }
        public string Fase { get; set; } = "Preparando";
    }

    private sealed class LinhaPlacar
    {
        public string Nome { get; set; } = string.Empty;
        public int Pontos { get; set; }
    }
}
