#nullable enable
using System;

namespace Game.Scripts.Games;

/// <summary>
/// Contexto entregue pelo lobby ao abrir um jogo.
///
/// O jogo não conhece o lobby: recebe o que precisa por aqui e conversa com
/// os outros jogadores pelos RPCs da porta (API de multiplayer isolada).
/// </summary>
public sealed class GameContexto
{
    public required GameInfo Jogo { get; init; }

    /// <summary>Dono da porta (quem abriu o jogo).</summary>
    public bool SouHost { get; init; }

    /// <summary>Porta ENet da partida (7778+).</summary>
    public int Porta { get; init; }

    /// <summary>Nome do jogador local.</summary>
    public string Jogador { get; init; } = "Jogador";

    /// <summary>Nick do jogador local (pode ser vazio para convidados).</summary>
    public string Nick { get; init; } = string.Empty;

    /// <summary>Nome da sala do professor (para exibir no jogo).</summary>
    public string Sala { get; init; } = string.Empty;

    /// <summary>Sala do banco (0 quando o jogador entrou como convidado).</summary>
    public int SalaId { get; init; }

    /// <summary>Chamado pelo jogo quando o jogador quer sair da partida.</summary>
    public Action? Sair { get; init; }

    /// <summary>Chamado pelo jogo para publicar uma mensagem no HUD do lobby.</summary>
    public Action<string>? Avisar { get; init; }
}
