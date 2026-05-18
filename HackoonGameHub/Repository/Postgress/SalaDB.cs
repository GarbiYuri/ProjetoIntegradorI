using Models;
using Repository.Exceptions;

namespace Repository.Postgress;
//CRUD DE SALA
public class SalaDB
{
    public static async Task Create(Sala sala)
    {
        DB.testConnection();

        if (sala.name == null)
        {
            throw new InvalidParameterException("Faltam Argumentos para criar sala!");
        }
        
        await using var cmd = 
        DB.dataSource.CreateCommand(@"INSERT INTO salas (name, descricao, created_at) values ($1, $2, $3) returning id;");
        cmd.Parameters.AddWithValue(sala.name);
        cmd.Parameters.AddWithValue(sala.descricao ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(sala.createdAt);
        
            var res = await cmd.ExecuteScalarAsync();
            if (res != null)
            {
                sala.id = Convert.ToInt32(res);
            }
    }

    public static async Task<List<Sala>> ReadAll()
    {
  
        DB.testConnection();

        List<Sala> salas = new List<Sala>();
        
        await using var cmd = DB.dataSource.CreateCommand("Select id, name, descricao, created_at from salas");

        await using var reader = await cmd.ExecuteReaderAsync();

        if (!reader.HasRows)
        {
            throw new ResourceNotFoundException($"Nenhuma sala foi encontrada.");
        }
        
        while (await reader.ReadAsync())
        {
            Sala sala = new Sala(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetDateTime(3)
            );
            salas.Add(sala);
        }
        return salas;
    }
    public static async Task<Sala> Read(int id_sala)
    {
  
            DB.testConnection();
            
            await using var cmd = DB.dataSource.CreateCommand("Select id, name, descricao, created_at from salas where id = $1");
            cmd.Parameters.AddWithValue(id_sala);

            await using var reader = await cmd.ExecuteReaderAsync();
            
            if (await reader.ReadAsync())
            {
                Sala sala = new Sala(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetDateTime(4)
                );
                return sala;
            }
            
            throw new ResourceNotFoundException($"Sala com ID {id_sala} não foi encontrada.");
        }

    public static async Task Update(Sala sala, int id_sala)
    {
        DB.testConnection();

        if (sala.name == null)
        {
            throw new InvalidParameterException("Faltam Argumentos para atualizar sala!");
        }
        
        await using var cmd = DB.dataSource.CreateCommand("update salas set name = $1, descricao = $2 where id = $3");
        cmd.Parameters.AddWithValue(sala.name);
        cmd.Parameters.AddWithValue(sala.descricao ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(id_sala);
        
        await cmd.ExecuteNonQueryAsync();
        
    }
}