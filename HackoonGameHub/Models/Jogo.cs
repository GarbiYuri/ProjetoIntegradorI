using System;

namespace Models;

/// <summary>
/// Jogo online criado dentro do lobby (uma "porta" de jogo).
///
/// O aluno não cria sala (isso é do professor): dentro do lobby ele abre uma
/// porta ENet própria para um jogo do catálogo. A porta é registrada aqui para
/// que os colegas da sala possam entrar e para o professor acompanhar.
///
/// Observação: o professor pode ver e encerrar essas portas pelo painel.
/// </summary>
public class Jogo
{
    public int id { get; set; }
    public string nome { get; set; } = string.Empty;

    /// <summary>Id do jogo no catálogo (GameRegistry). "custom" = porta livre.</summary>
    public string gameKey { get; set; } = "custom";

    public int? id_sala { get; set; }
    public int? id_aluno { get; set; }
    public string criador { get; set; } = string.Empty;
    public string host_ip { get; set; } = string.Empty;
    public int porta { get; set; } = 7778;
    public string status { get; set; } = "ABERTO";
    public int jogadores { get; set; } = 1;

    /// <summary>Resumo do placar/resultado publicado pelo jogo.</summary>
    public string placar { get; set; } = string.Empty;

    public DateTime createdAt { get; set; } = DateTime.Now;

    public Jogo()
    {
    }

    public Jogo(string nome, int? id_sala, int? id_aluno, string criador, string hostIp, int porta,
        string gameKey = "custom")
    {
        this.nome = nome;
        this.id_sala = id_sala;
        this.id_aluno = id_aluno;
        this.criador = criador;
        this.host_ip = hostIp;
        this.porta = porta;
        this.gameKey = string.IsNullOrWhiteSpace(gameKey) ? "custom" : gameKey;
        status = "ABERTO";
        jogadores = 1;
        createdAt = DateTime.Now;
    }
}
