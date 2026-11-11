using System;

namespace Models;

/// <summary>
/// Jogo online criado por um aluno DENTRO do lobby (conexão interna).
///
/// O aluno não cria sala (isso é do professor): ele abre uma "conexão
/// interna" com o próprio servidor ENet, que fica registrada aqui para que
/// os outros alunos da sala possam entrar.
///
/// Observação: o professor pode ver e encerrar esses jogos pelo painel.
/// </summary>
public class Jogo
{
    public int id { get; set; }
    public string nome { get; set; } = string.Empty;
    public int? id_sala { get; set; }
    public int? id_aluno { get; set; }
    public string criador { get; set; } = string.Empty;
    public string host_ip { get; set; } = string.Empty;
    public int porta { get; set; } = 7778;
    public string status { get; set; } = "ABERTO";
    public DateTime createdAt { get; set; } = DateTime.Now;

    public Jogo()
    {
    }

    public Jogo(string nome, int? id_sala, int? id_aluno, string criador, string hostIp, int porta)
    {
        this.nome = nome;
        this.id_sala = id_sala;
        this.id_aluno = id_aluno;
        this.criador = criador;
        host_ip = hostIp;
        this.porta = porta;
        status = "ABERTO";
        createdAt = DateTime.Now;
    }
}
