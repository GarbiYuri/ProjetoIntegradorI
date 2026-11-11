using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Models;
using Repository.Exceptions;

namespace Repository.Postgress;

/// <summary>
/// Acesso à tabela de jogos (conexões internas criadas pelos alunos dentro
/// do lobby). O professor pode consultar e encerrar esses jogos.
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
            @"INSERT INTO jogos (name, id_sala, id_aluno, creator, host_ip, port, status, created_at)
              VALUES ($1, $2, $3, $4, $5, $6, $7, $8) RETURNING id;");

        cmd.Parameters.AddWithValue(jogo.nome);
        cmd.Parameters.AddWithValue(jogo.id_sala ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(jogo.id_aluno ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(jogo.criador ?? string.Empty);
        cmd.Parameters.AddWithValue(jogo.host_ip ?? string.Empty);
        cmd.Parameters.AddWithValue(jogo.porta);
        cmd.Parameters.AddWithValue(jogo.status ?? "ABERTO");
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
            "SELECT id, name, id_sala, id_aluno, creator, host_ip, port, status, created_at " +
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
            "SELECT id, name, id_sala, id_aluno, creator, host_ip, port, status, created_at " +
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

    private static Jogo Ler(System.Data.Common.DbDataReader reader)
    {
        return new Jogo
        {
            id = reader.GetInt32(0),
            nome = reader.GetString(1),
            id_sala = reader.IsDBNull(2) ? null : reader.GetInt32(2),
            id_aluno = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            criador = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            host_ip = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
            porta = reader.GetInt32(6),
            status = reader.IsDBNull(7) ? "ABERTO" : reader.GetString(7),
            createdAt = reader.GetDateTime(8)
        };
    }
}
