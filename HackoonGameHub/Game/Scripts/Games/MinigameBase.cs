#nullable enable
using System;
using Godot;

namespace Game.Scripts.Games;

/// <summary>
/// Base para qualquer jogo do Hackoon Game Hub.
///
/// Ciclo de vida:
///   1. o lobby cria a porta (ENet) e instancia a cena;
///   2. chama <see cref="Iniciar"/> com o <see cref="GameContexto"/>;
///   3. o jogo roda normalmente (solo ou com os colegas na mesma porta);
///   4. ao fechar, o lobby chama <see cref="Encerrar"/> e libera a porta.
///
/// O jogo também pode publicar eventos para o HUD (status e resumo final)
/// sem conhecer a implementação da interface.
/// </summary>
public abstract partial class MinigameBase : Control
{
    /// <summary>Identificador do jogo (deve bater com o <see cref="GameInfo.Id"/>).</summary>
    public abstract string GameId { get; }

    public GameContexto? Contexto { get; private set; }

    /// <summary>Mensagem curta mostrada no HUD do lobby.</summary>
    public event Action<string>? StatusMudou;

    /// <summary>Resumo final (placar) para o painel do professor.</summary>
    public event Action<string>? JogoTerminou;

    /// <summary>Placar parcial (ao vivo) para o painel do professor.</summary>
    public event Action<string>? PlacarMudou;

    /// <summary>Pede ao lobby para fechar o jogo (o jogador clicou em sair).</summary>
    public event Action? SairSolicitado;

    public bool Iniciado { get; private set; }

    public override void _Ready()
    {
        // A cena pode ser aberta no editor; só inicializa com contexto.
        if (Contexto != null)
        {
            return;
        }
    }

    /// <summary>Chamado pelo lobby depois de instanciar a cena.</summary>
    public virtual void Iniciar(GameContexto contexto)
    {
        Contexto = contexto;
        Iniciado = true;
    }

    /// <summary>Chamado pelo lobby antes de liberar a porta/nó.</summary>
    public virtual void Encerrar()
    {
        Iniciado = false;
    }

    protected void AvisarStatus(string mensagem)
    {
        StatusMudou?.Invoke(mensagem);
    }

    protected void AvisarFim(string resumo)
    {
        JogoTerminou?.Invoke(resumo);
    }

    /// <summary>Publica o placar parcial (o painel do professor mostra ao vivo).</summary>
    protected void AvisarPlacar(string resumo)
    {
        PlacarMudou?.Invoke(resumo);
    }

    protected void PedirSaida()
    {
        SairSolicitado?.Invoke();
    }

    /// <summary>Nome de exibição do jogador local.</summary>
    protected string NomeLocal =>
        string.IsNullOrWhiteSpace(Contexto?.Jogador) ? "Jogador" : Contexto!.Jogador;

    /// <summary>Se este nó é o dono da porta do jogo.</summary>
    protected bool SouServidor => Contexto?.SouHost == true || Multiplayer.IsServer();
}
