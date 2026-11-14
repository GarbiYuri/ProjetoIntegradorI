using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Models;
using Repository.Exceptions;

namespace Repository.Postgress;

/// <summary>
/// Acesso à tabela de jogos (portas abertas dentro do lobby). O professor
/// pode consultar e encerrar esses jogos; cada linha guarda qual jogo do
/// catálogo está rodando, o dono da porta e o placar.
/// </summary>
public class JogoDB
{
    public static async Task Create(Jogo jogo)
    {
        DB.testConnection();

        if (string.IsNullOrWhiteSpace(jogo.nome))
        {
            throw new InvalidParameterException("O jogo precisa de um nome.");
        }

        await using var cmd = DB.dataSource.CreateCommand(
            @"INSERT INTO jogos (name, game_key, id_sala, id_aluno, creator, host_ip, port, status, players, score, created_at)
              VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11) RETURNING id;");

        cmd.Parameters.AddWithValue(jogo.nome);
        cmd.Parameters.AddWithValue(string.IsNullOrWhiteSpace(jogo.gameKey) ? "custom" : jogo.gameKey);
        cmd.Parameters.AddWithValue(jogo.id_sala ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(jogo.id_aluno ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(jogo.criador ?? string.Empty);
        cmd.Parameters.AddWithValue(jogo.host_ip ?? string.Empty);
        cmd.Parameters.AddWithValue(jogo.porta);
        cmd.Parameters.AddWithValue(jogo.status ?? "ABERTO");
        cmd.Parameters.AddWithValue(jogo.jogadores);
        cmd.Parameters.AddWithValue(jogo.placar ?? string.Empty);
        cmd.Parameters.AddWithValue(jogo.createdAt);

        var res = await cmd.ExecuteScalarAsync();

        if (res != null)
        {
            jogo.id = Convert.ToInt32(res);
        }
    }

    public static async Task<List<Jogo>> ListarPorSala(int id_sala)
    {
        DB.testConnection();

        var lista = new List<Jogo>();
        await using var cmd = DB.dataSource.CreateCommand(
            "SELECT id, name, game_key, id_sala, id_aluno, creator, host_ip, port, status, players, score, created_at " +
            "FROM jogos WHERE id_sala = $1 ORDER BY id DESC");

        cmd.Parameters.AddWithValue(id_sala);

        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            lista.Add(Ler(reader));
        }

        return lista;
    }

    public static async Task<List<Jogo>> Listar()
    {
        DB.testConnection();

        var lista = new List<Jogo>();
        await using var cmd = DB.dataSource.CreateCommand(
            "SELECT id, name, game_key, id_sala, id_aluno, creator, host_ip, port, status, players, score, created_at " +
            "FROM jogos ORDER BY id DESC");

        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            lista.Add(Ler(reader));
        }

        return lista;
    }

    public static async Task AtualizarStatus(int id, string status)
    {
        DB.testConnection();

        await using var cmd = DB.dataSource.CreateCommand(
            "UPDATE jogos SET status = $1 WHERE id = $2");

        cmd.Parameters.AddWithValue(status);
        cmd.Parameters.AddWithValue(id);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Atualiza quantidade de jogadores e o placar/resumo do jogo.</summary>
    public static async Task AtualizarProgresso(int id, int jogadores, string placar)
    {
        DB.testConnection();

        await using var cmd = DB.dataSource.CreateCommand(
            "UPDATE jogos SET players = $1, score = $2 WHERE id = $3");

        cmd.Parameters.AddWithValue(jogadores);
        cmd.Parameters.AddWithValue(placar ?? string.Empty);
        cmd.Parameters.AddWithValue(id);

        await cmd.ExecuteNonQueryAsync();
    }

    private static Jogo Ler(System.Data.Common.DbDataReader reader)
    {
        return new Jogo
        {
            id = reader.GetInt32(0),
            nome = reader.GetString(1),
            gameKey = reader.IsDBNull(2) ? "custom" : reader.GetString(2),
            id_sala = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            id_aluno = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            criador = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
            host_ip = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
            porta = reader.GetInt32(7),
            status = reader.IsDBNull(8) ? "ABERTO" : reader.GetString(8),
            jogadores = reader.IsDBNull(9) ? 1 : reader.GetInt32(9),
            placar = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
            createdAt = reader.GetDateTime(11)
        };
    }
}
