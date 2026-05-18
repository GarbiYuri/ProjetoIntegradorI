using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Models;
using Repository.Exceptions;

namespace Repository.Postgress;

public class ProfessorDB
{
    public static async Task Create(Professor professor)
    {
        DB.testConnection();

        if (professor.name == null || professor.email == null)
        {
            throw new InvalidParameterException("Faltam Parametros Obrigatórios para Professor");
        }

        await using var cmd = DB.dataSource.CreateCommand(
            @"INSERT INTO professores (name, email, password, created_at) values ($1, $2, $3, $4) returning id;");
        cmd.Parameters.AddWithValue(professor.name);
        cmd.Parameters.AddWithValue(professor.email);
        cmd.Parameters.AddWithValue(professor.password ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(professor.createdAt);

        var res = await cmd.ExecuteScalarAsync();
        if (res != null)
        {
            professor.id = Convert.ToInt32(res);
        }
    }

    public static async Task<List<Professor>> Listar()
    {
        DB.testConnection();

        var lista = new List<Professor>();
        await using var cmd = DB.dataSource.CreateCommand("SELECT id, name, email, created_at FROM professores");
        await using var reader = await cmd.ExecuteReaderAsync();

        if (!reader.HasRows)
        {
            throw new ResourceNotFoundException("Nenhum professor cadastrado");
        }

        while (await reader.ReadAsync())
        {
            var p = new Professor(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDateTime(3)
            );
            lista.Add(p);
        }
        return lista;
    }

    public static async Task<Professor> GetProfessorByEmail(string email)
    {
        DB.testConnection();

        await using var cmd = DB.dataSource.CreateCommand("SELECT id, name, email, created_at FROM professores WHERE email = $1");
        cmd.Parameters.AddWithValue(email);
        await using var reader = await cmd.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return new Professor(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDateTime(3)
            );
        }
        throw new ResourceNotFoundException("Nenhum professor com este email");
    }

    public static async Task<bool> ProfessorExists(string email)
    {
        DB.testConnection();

        await using var cmd = DB.dataSource.CreateCommand("SELECT id FROM professores WHERE email = $1");
        cmd.Parameters.AddWithValue(email);
        await using var reader = await cmd.ExecuteReaderAsync();
        return reader.HasRows;
    }
}