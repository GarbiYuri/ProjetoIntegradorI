#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Game.Scripts.Games;

/// <summary>
/// Catálogo central de jogos do Hackoon Game Hub.
///
/// Adicionar um jogo é uma linha: registre o <see cref="GameInfo"/> apontando
/// para a cena. O resto (casinha física no lobby, painel do professor, portas
/// e permissões) é automático. A casinha pode receber uma posição fixa em
/// <see cref="GameInfo.PosicaoNoLobby"/> ou usar a distribuição automática.
///
/// Regras para a cena do jogo:
/// - o nó raiz deve ter um script que herde de <see cref="MinigameBase"/>;
/// - o jogo recebe um <see cref="GameContexto"/> com porta, host e jogador;
/// - o jogo pode usar RPCs normalmente (a porta tem uma API de multiplayer
///   própria, isolada da conexão do lobby).
/// </summary>
public static class GameRegistry
{
    private static readonly List<GameInfo> _jogos = new()
    {
        // -------------------------------------------------------------
        // Jogos do Hackoon Game Hub
        // -------------------------------------------------------------
        new GameInfo(
            id: "regras-convivencia",
            nome: "Regras de Convivência",
            descricao: "Leia a atitude e escolha: é uma atitude BOA ou RUIM?",
            cena: "res://Games/RegrasDeConvivencia/RegrasDeConvivencia.tscn",
            categoria: "Cidadania",
            minJogadores: 1,
            maxJogadores: 10,
            cor: new Color(0.427f, 0.686f, 0.949f),
            emblema: "⚖"),

        // -------------------------------------------------------------
        // Próximo jogo: copie o bloco acima, troque os dados e crie a cena.
        // Nada mais precisa ser alterado no lobby.
        // -------------------------------------------------------------
    };

    public static IReadOnlyList<GameInfo> Jogos => _jogos;

    /// <summary>Registra um jogo em tempo de execução (plugins/expansões).</summary>
    public static void Registrar(GameInfo jogo)
    {
        if (jogo == null || string.IsNullOrWhiteSpace(jogo.Id))
        {
            return;
        }

        if (_jogos.Any(j => j.Id == jogo.Id))
        {
            return;
        }

        _jogos.Add(jogo);
        GD.Print($"[GAMES] Jogo registrado: {jogo.Nome} ({jogo.Id})");
    }

    public static GameInfo? PorId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return _jogos.FirstOrDefault(j => j.Id == id);
    }

    public static bool CenaExiste(GameInfo jogo)
    {
        return !string.IsNullOrWhiteSpace(jogo.Cena) && ResourceLoader.Exists(jogo.Cena);
    }
}
