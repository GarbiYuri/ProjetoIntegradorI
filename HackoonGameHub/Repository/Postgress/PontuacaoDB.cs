using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Models;
using Npgsql;
using Repository.Exceptions;

namespace Repository.Postgress;

/// <summary>
/// Persistência do sistema de pontos do Hackoon Game Hub.
///
/// O repositório é responsável por duas coisas:
/// 1. guardar o recibo imutável da partida na tabela <c>pontuacoes</c>;
/// 2. atualizar, na MESMA transação, os totais do aluno (points/xp/level/stars).
///
/// A transação trava a linha do aluno (SELECT ... FOR UPDATE) e o índice único
/// (aluno_id, partida_id) garante que a mesma partida nunca pontue duas vezes,
/// mesmo com RPC repetido ou dois pedidos simultâneos.
/// </summary>
public class PontuacaoDB
{
    /// <summary>
    /// Aplica a recompensa e grava o histórico atomicamente.
    /// A regra de progressão (pontos → XP → nível e estrelas) vem do modelo
    /// <see cref="Aluno.RegistrarRecompensa"/>, para ficar em um único lugar.
    /// </summary>
    public static async Task<ResultadoPontuacao> RegistrarPartida(Pontuacao registro)
    {
        DB.testConnection();

        if (registro == null)
        {
            throw new InvalidParameterException("A pontuação precisa ser informada.");
        }

        if (registro.id_aluno <= 0)
        {
            throw new InvalidParameterException("A pontuação precisa de um aluno válido.");
        }

        if (string.IsNullOrWhiteSpace(registro.partida_id))
        {
            throw new InvalidParameterException("A pontuação precisa do id da partida.");
        }

        registro.pontos = Math.Max(0, registro.pontos);
        registro.estrelas = Math.Clamp(registro.estrelas, 0, 3);

        await using NpgsqlConnection conexao = await DB.dataSource.OpenConnectionAsync();
        await using NpgsqlTransaction transacao = await conexao.BeginTransactionAsync();

        int pontosAntes;
        int nivelAntes;
        int xpAntes;
        int estrelasAntes;

        await using (var selecionar = new NpgsqlCommand(
            "SELECT points, level, xp, stars FROM alunos WHERE id = $1 FOR UPDATE",
            conexao, transacao))
        {
            selecionar.Parameters.AddWithValue(registro.id_aluno);

            await using NpgsqlDataReader leitor = await selecionar.ExecuteReaderAsync();

            if (!await leitor.ReadAsync())
            {
                await transacao.RollbackAsync();
                throw new ResourceNotFoundException(
                    $"Aluno com ID {registro.id_aluno} não foi encontrado.");
            }

            pontosAntes = leitor.GetInt32(0);
            nivelAntes = leitor.GetInt32(1);
            xpAntes = leitor.GetInt32(2);
            estrelasAntes = leitor.GetInt32(3);
        }

        var aluno = new Aluno(
            registro.id_aluno,
            string.Empty,
            string.Empty,
            DateTime.Now)
        {
            points = pontosAntes,
            level = nivelAntes,
            xp = xpAntes,
            stars = estrelasAntes
        };

        ResultadoPontuacao resultado = aluno.RegistrarRecompensa(
            registro.pontos, registro.estrelas);

        registro.xp_ganho = resultado.PontosGanhos;
        registro.nivel_anterior = resultado.NivelAnterior;
        registro.nivel_novo = resultado.NivelNovo;

        await using (var inserir = new NpgsqlCommand(@"
            INSERT INTO pontuacoes
                (aluno_id, jogo_key, jogo_nome, partida_id, id_sala, pontos,
                 estrelas, xp_ganho, nivel_anterior, nivel_novo, detalhes, created_at)
            VALUES
                ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, NOW())
            ON CONFLICT (aluno_id, partida_id) DO NOTHING
            RETURNING id;",
            conexao, transacao))
        {
            inserir.Parameters.AddWithValue(registro.id_aluno);
            inserir.Parameters.AddWithValue(registro.jogo_key);
            inserir.Parameters.AddWithValue(registro.jogo_nome);
            inserir.Parameters.AddWithValue(registro.partida_id);
            inserir.Parameters.AddWithValue(
                registro.id_sala.HasValue && registro.id_sala.Value > 0
                    ? registro.id_sala.Value
                    : (object)DBNull.Value);
            inserir.Parameters.AddWithValue(registro.pontos);
            inserir.Parameters.AddWithValue(registro.estrelas);
            inserir.Parameters.AddWithValue(registro.xp_ganho);
            inserir.Parameters.AddWithValue(registro.nivel_anterior);
            inserir.Parameters.AddWithValue(registro.nivel_novo);
            inserir.Parameters.AddWithValue(
                string.IsNullOrWhiteSpace(registro.detalhes)
                    ? (object)DBNull.Value
                    : registro.detalhes);

            object? idInserido = await inserir.ExecuteScalarAsync();

            if (idInserido == null)
            {
                // A partida já tinha sido salva: não mexe nos totais.
                await transacao.RollbackAsync();

                var alunoAtual = new Aluno(
                    registro.id_aluno,
                    string.Empty,
                    string.Empty,
                    DateTime.Now)
                {
                    points = pontosAntes,
                    level = nivelAntes,
                    xp = xpAntes,
                    stars = estrelasAntes
                };

                return new ResultadoPontuacao
                {
                    Salvo = false,
                    AlunoId = registro.id_aluno,
                    PontosTotais = pontosAntes,
                    EstrelasTotais = estrelasAntes,
                    NivelAnterior = nivelAntes,
                    NivelNovo = nivelAntes,
                    XpAtual = xpAntes,
                    XpProximoNivel = alunoAtual.XpParaProximoNivel(),
                    Mensagem = "Esta partida já foi registrada. Os pontos não foram duplicados."
                };
            }

            registro.id = Convert.ToInt32(idInserido);
        }

        await using (var atualizar = new NpgsqlCommand(
            "UPDATE alunos SET points = $1, xp = $2, level = $3, stars = $4 WHERE id = $5",
            conexao, transacao))
        {
            atualizar.Parameters.AddWithValue(aluno.points);
            atualizar.Parameters.AddWithValue(aluno.xp);
            atualizar.Parameters.AddWithValue(aluno.level);
            atualizar.Parameters.AddWithValue(aluno.stars);
            atualizar.Parameters.AddWithValue(aluno.id);

            await atualizar.ExecuteNonQueryAsync();
        }

        await transacao.CommitAsync();

        resultado.Salvo = true;
        resultado.Mensagem = MontarMensagem(resultado);
        return resultado;
    }

    /// <summary>Histórico de partidas de um aluno (mais recentes primeiro).</summary>
    public static async Task<List<Pontuacao>> ListarPorAluno(int alunoId, int limite = 30)
    {
        DB.testConnection();

        var lista = new List<Pontuacao>();

        await using var comando = DB.dataSource.CreateCommand(@"
            SELECT id, aluno_id, jogo_key, jogo_nome, partida_id, id_sala, pontos,
                   estrelas, xp_ganho, nivel_anterior, nivel_novo, detalhes, created_at
            FROM pontuacoes
            WHERE aluno_id = $1
            ORDER BY created_at DESC, id DESC
            LIMIT $2;");

        comando.Parameters.AddWithValue(alunoId);
        comando.Parameters.AddWithValue(Math.Clamp(limite, 1, 200));

        await using NpgsqlDataReader leitor = await comando.ExecuteReaderAsync();

        while (await leitor.ReadAsync())
        {
            lista.Add(Ler(leitor));
        }

        return lista;
    }

    /// <summary>Ranking de pontos (por sala ou geral), usado pelo professor.</summary>
    public static async Task<List<RankingPontuacao>> Ranking(int? idSala, int limite = 10)
    {
        DB.testConnection();

        var lista = new List<RankingPontuacao>();
        string filtro = idSala.HasValue && idSala.Value > 0
            ? "WHERE a.id_sala = $1"
            : string.Empty;
        int parametroLimite = idSala.HasValue && idSala.Value > 0 ? 2 : 1;

        await using var comando = DB.dataSource.CreateCommand($@"
            SELECT a.id, a.name, COALESCE(a.username, ''), a.points, a.level, a.stars
            FROM alunos a
            {filtro}
            ORDER BY a.points DESC, a.stars DESC, a.name ASC
            LIMIT ${parametroLimite};");

        if (idSala.HasValue && idSala.Value > 0)
        {
            comando.Parameters.AddWithValue(idSala.Value);
        }

        comando.Parameters.AddWithValue(Math.Clamp(limite, 1, 100));

        await using NpgsqlDataReader leitor = await comando.ExecuteReaderAsync();

        while (await leitor.ReadAsync())
        {
            lista.Add(new RankingPontuacao
            {
                AlunoId = leitor.GetInt32(0),
                Nome = leitor.GetString(1),
                Nick = leitor.GetString(2),
                Pontos = leitor.GetInt32(3),
                Nivel = leitor.GetInt32(4),
                Estrelas = leitor.GetInt32(5)
            });
        }

        return lista;
    }

    private static string MontarMensagem(ResultadoPontuacao resultado)
    {
        string mensagem = $"+{resultado.PontosGanhos} pts • {resultado.PontosTotais} pts no total";

        if (resultado.EstrelasGanhas > 0)
        {
            mensagem += $" • +{resultado.EstrelasGanhas} ⭐";
        }

        if (resultado.SubiuNivel)
        {
            mensagem += $" • NÍVEL {resultado.NivelNovo}!";
        }

        return mensagem;
    }

    private static Pontuacao Ler(NpgsqlDataReader leitor)
    {
        return new Pontuacao
        {
            id = leitor.GetInt32(0),
            id_aluno = leitor.GetInt32(1),
            jogo_key = leitor.GetString(2),
            jogo_nome = leitor.GetString(3),
            partida_id = leitor.GetString(4),
            id_sala = leitor.IsDBNull(5) ? null : leitor.GetInt32(5),
            pontos = leitor.GetInt32(6),
            estrelas = leitor.GetInt32(7),
            xp_ganho = leitor.GetInt32(8),
            nivel_anterior = leitor.GetInt32(9),
            nivel_novo = leitor.GetInt32(10),
            detalhes = leitor.IsDBNull(11) ? null : leitor.GetString(11),
            createdAt = leitor.GetDateTime(12)
        };
    }
}
