using System.ComponentModel.Design.Serialization;
using System.Security.Cryptography;
using Models;
using Repository.Exceptions;

namespace Repository.Postgress;

// CRUD DE USUARIO
public class UsuarioDB
{
    public static async Task Create(Usuario usuario)
    {
        DB.testConnection();

        if (usuario.name == null ||  usuario.username == null)
        {
            throw new InvalidParameterException("Faltam Parametros Obrigatórios para Usuario");
        }
        
            await using var cmd = DB.dataSource.CreateCommand(
                @"INSERT INTO usuarios (username, name, email, id_sala, password, created_at) values ($1, $2, $3, $4, $5, $6) returning id;");
            cmd.Parameters.AddWithValue(usuario.username);
            cmd.Parameters.AddWithValue(usuario.name);
            cmd.Parameters.AddWithValue(usuario.email ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue(usuario.id_sala ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue(usuario.password ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue(usuario.createdAt);

            var res = await cmd.ExecuteScalarAsync();
            if (res != null)
            {
                usuario.id = Convert.ToInt32(res);
            }
        
    }
    public static async Task<List<Usuario>> Listar()
    {
        DB.testConnection();
        
        var lista = new List<Usuario>();
        await using var cmd = DB.dataSource.CreateCommand("Select id, username, name, email, created_at, id_sala from usuarios");
        await using var reader = await cmd.ExecuteReaderAsync();

        if (!reader.HasRows)
        {
            throw new ResourceNotFoundException("Nenhum aluno cadastrado");
        }
        
        while (await reader.ReadAsync())
        {
            var u = new Usuario(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDateTime(4)
            );
            u.email = reader.IsDBNull(3) ? "sem email" : reader.GetString(3);
            u.id_sala = reader.IsDBNull(5) ? null : reader.GetInt32(5);
            lista.Add(u);
        }
        return lista;
    }
    public static async Task<List<Usuario>> Read(int id_sala)
    {
            DB.testConnection();
            
            var lista = new List<Usuario>();
            await using var cmd = 
            DB.dataSource.CreateCommand("Select id, username, name, email, created_at, id_sala from usuarios where id_sala = $1");
            cmd.Parameters.AddWithValue(id_sala);
            await using var reader = await cmd.ExecuteReaderAsync();

            if (!reader.HasRows)
            {
                throw new ResourceNotFoundException("Não Existe aluno nesta sala");
            }
            
            while (await reader.ReadAsync())
            {
                var u = new Usuario(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetDateTime(4)
                );
                u.email = reader.IsDBNull(3) ? "sem email" : reader.GetString(3);
                u.id_sala = reader.IsDBNull(5) ? null : reader.GetInt32(5);
                lista.Add(u);
            }
            return lista;
            
    }
    public static async Task<Usuario> GetUserByUsername(string username)
    {
        DB.testConnection();
        
        await using var cmd = 
        DB.dataSource.CreateCommand("Select id, username, name, email, created_at, id_sala from usuarios where username = $1");
        cmd.Parameters.AddWithValue(username);
        await using var reader = await cmd.ExecuteReaderAsync();
            
        if (await reader.ReadAsync())
        {
            var u = new Usuario(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDateTime(4)
            );
            u.email = reader.IsDBNull(3) ? "sem email" : reader.GetString(3);
            u.id_sala = reader.IsDBNull(5) ? null : reader.GetInt32(5);
            return u;
        }
        throw new ResourceNotFoundException("Nenhum usuario com este nome");
    }

    public static async Task<bool> UserExists(string username)
    {
        DB.testConnection();
        
        await using var cmd =
        DB.dataSource.CreateCommand("Select id from usuarios where username = $1");
        cmd.Parameters.AddWithValue(username);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (reader.HasRows)
        {
            return true;
        }
        return false;
    }
}