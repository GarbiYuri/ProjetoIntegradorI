#nullable enable
using Godot;

namespace Game.Scripts.Games;

/// <summary>
/// Metadados de um jogo que pode ser aberto dentro do Hackoon Game Hub.
///
/// Para adicionar um jogo novo, basta criar a cena dele (com um script que
/// herde de <see cref="MinigameBase"/>) e registrar um <see cref="GameInfo"/>
/// em <see cref="GameRegistry"/>. O catálogo do lobby, o painel do professor
/// e a lista de portas abertas passam a mostrar o jogo automaticamente.
/// </summary>
public sealed class GameInfo
{
    /// <summary>Identificador estável (usado no banco e nos RPCs).</summary>
    public string Id { get; }

    public string Nome { get; }

    public string Descricao { get; }

    /// <summary>Caminho res:// da cena do jogo.</summary>
    public string Cena { get; }

    public string Categoria { get; }

    /// <summary>Quantidade mínima/máxima de jogadores na mesma porta.</summary>
    public int MinJogadores { get; }

    public int MaxJogadores { get; }

    /// <summary>Cor de destaque do cartão no catálogo.</summary>
    public Color Cor { get; }

    /// <summary>Emblema curto mostrado no cartão (texto/emoji).</summary>
    public string Emblema { get; }

    public GameInfo(string id, string nome, string descricao, string cena,
        string categoria = "Geral", int minJogadores = 1, int maxJogadores = 8,
        Color? cor = null, string emblema = "★")
    {
        Id = id;
        Nome = nome;
        Descricao = descricao;
        Cena = cena;
        Categoria = categoria;
        MinJogadores = minJogadores;
        MaxJogadores = maxJogadores;
        Cor = cor ?? new Color(0.949f, 0.765f, 0.302f);
        Emblema = emblema;
    }
}
