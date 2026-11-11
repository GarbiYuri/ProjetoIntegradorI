using System.Collections.Generic;
using System.Threading.Tasks;
using Models;
using Repository.Postgress;

namespace Service;

/// <summary>
/// Serviços dos jogos internos (conexões criadas pelos alunos dentro do lobby).
///
/// Regra do projeto: o aluno NÃO cria sala (lobby) — quem cria sala é o
/// professor. Dentro do lobby, o aluno pode abrir uma "conexão interna" para
/// criar o jogo online, e é isso que este service cuida.
/// </summary>
public static class JogoService
{
    /// <summary>Registra um novo jogo interno criado por um aluno.</summary>
    public static async Task<Jogo> Criar(string nome, int? idSala, int? idAluno,
        string criador, string hostIp, int porta)
    {
        var jogo = new Jogo(nome, idSala, idAluno, criador, hostIp, porta);
        await JogoDB.Create(jogo);
        return jogo;
    }

    public static async Task<List<Jogo>> ListarPorSala(int idSala)
    {
        return await JogoDB.ListarPorSala(idSala);
    }

    public static async Task<List<Jogo>> Listar()
    {
        return await JogoDB.Listar();
    }

    /// <summary>Encerra um jogo interno (o professor ou o próprio criador).</summary>
    public static async Task Fechar(int id)
    {
        await JogoDB.AtualizarStatus(id, "ENCERRADO");
    }
}
