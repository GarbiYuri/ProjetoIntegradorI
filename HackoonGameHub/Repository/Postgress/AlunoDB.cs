using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Models;
using Repository.Exceptions;

namespace Repository.Postgress;

public class AlunoDB
{
    public static async Task Create(Aluno aluno)
    {
        DB.testConnection();

        if (aluno.name == null || aluno.username == null)
        {
            throw new InvalidParameterException("Faltam Parametros Obrigatórios para Aluno");
        }

        await using var cmd = DB.dataSource.CreateCommand(
            @"INSERT INTO alunos (username, name, turma, points, level, xp, stars, id_sala, password, created_at) 
              VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10) RETURNING id;");
        
        cmd.Parameters.AddWithValue(aluno.username);
        cmd.Parameters.AddWithValue(aluno.name);
        cmd.Parameters.AddWithValue(aluno.turma ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(aluno.points);
        cmd.Parameters.AddWithValue(aluno.level);
        cmd.Parameters.AddWithValue(aluno.xp);
        cmd.Parameters.AddWithValue(aluno.stars);
        cmd.Parameters.AddWithValue(aluno.id_sala ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(aluno.password ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(aluno.createdAt);

        var res = await cmd.ExecuteScalarAsync();
        if (res != null)
        {
            aluno.id = Convert.ToInt32(res);
        }
    }

    public static async Task<List<Aluno>> Listar()
    {
        DB.testConnection();

        var lista = new List<Aluno>();
        await using var cmd = DB.dataSource.CreateCommand(
            "SELECT id, username, name, created_at, id_sala, turma, points, level, xp, stars FROM alunos");
        await using var reader = await cmd.ExecuteReaderAsync();

        if (!reader.HasRows)
        {
            throw new ResourceNotFoundException("Nenhum aluno cadastrado");
        }

        while (await reader.ReadAsync())
        {
            var a = new Aluno(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDateTime(3)
            )
            {
                id_sala = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                turma = reader.IsDBNull(5) ? null : reader.GetString(5),
                points = reader.GetInt32(6),
                level = reader.GetInt32(7),
                xp = reader.GetInt32(8),
                stars = reader.GetInt32(9)
            };
            lista.Add(a);
        }
        return lista;
    }

    public static async Task<List<Aluno>> Read(int id_sala)
    {
        DB.testConnection();

        var lista = new List<Aluno>();
        await using var cmd = DB.dataSource.CreateCommand(
            "SELECT id, username, name, created_at, id_sala, turma, points, level, xp, stars FROM alunos WHERE id_sala = $1");
        cmd.Parameters.AddWithValue(id_sala);
        await using var reader = await cmd.ExecuteReaderAsync();

        if (!reader.HasRows)
        {
            throw new ResourceNotFoundException("Não existe aluno nesta sala");
        }

        while (await reader.ReadAsync())
        {
            var a = new Aluno(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDateTime(3)
            )
            {
                id_sala = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                turma = reader.IsDBNull(5) ? null : reader.GetString(5),
                points = reader.GetInt32(6),
                level = reader.GetInt32(7),
                xp = reader.GetInt32(8),
                stars = reader.GetInt32(9)
            };
            lista.Add(a);
        }
        return lista;
    }

    public static async Task<Aluno> GetAlunoByUsername(string username)
    {
        DB.testConnection();

        await using var cmd = DB.dataSource.CreateCommand(
            "SELECT id, username, name, created_at, id_sala, turma, points, level, xp, stars FROM alunos WHERE username = $1");
        cmd.Parameters.AddWithValue(username);
        await using var reader = await cmd.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return new Aluno(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDateTime(3)
            )
            {
                id_sala = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                turma = reader.IsDBNull(5) ? null : reader.GetString(5),
                points = reader.GetInt32(6),
                level = reader.GetInt32(7),
                xp = reader.GetInt32(8),
                stars = reader.GetInt32(9)
            };
        }
        throw new ResourceNotFoundException("Nenhum aluno com este nome");
    }

    public static async Task<bool> AlunoExists(string username)
    {
        DB.testConnection();

        await using var cmd = DB.dataSource.CreateCommand("SELECT id FROM alunos WHERE username = $1");
        cmd.Parameters.AddWithValue(username);
        await using var reader = await cmd.ExecuteReaderAsync();
        return reader.HasRows;
    }
}