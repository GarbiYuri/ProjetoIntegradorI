using System.Collections.Generic;
using System.Threading.Tasks;
using Models;
using Repository.Postgress;

namespace Service;

/// <summary>
/// Serviços das portas de jogo abertas dentro do lobby.
///
/// Regra do projeto: o aluno NÃO cria sala (lobby) — quem cria sala é o
/// professor. Dentro do lobby, qualquer jogador pode abrir uma porta para um
/// jogo do catálogo; os colegas entram nessa mesma porta.
/// </summary>
public static class JogoService
{
    /// <summary>Registra uma nova porta de jogo.</summary>
    public static async Task<Jogo> Criar(string nome, int? idSala, int? idAluno,
        string criador, string hostIp, int porta, string gameKey = "custom")
    {
        var jogo = new Jogo(nome, idSala, idAluno, criador, hostIp, porta, gameKey);
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

    /// <summary>Encerra uma porta de jogo (o professor ou o próprio criador).</summary>
    public static async Task Fechar(int id)
    {
        await JogoDB.AtualizarStatus(id, "ENCERRADO");
    }

    /// <summary>Atualiza jogadores/placar de uma porta aberta.</summary>
    public static async Task AtualizarProgresso(int id, int jogadores, string placar)
    {
        await JogoDB.AtualizarProgresso(id, jogadores, placar);
    }
}
