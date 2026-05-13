using Npgsql;
using Repository.Exceptions;

namespace Repository.Postgress;

public class DB
{
    public static NpgsqlDataSource dataSource {get; private set;}
    public static bool conectado = false;
    
    //Funcão deve rodar apenas uma vez ao inciar o game e TEM QUE DAR CERTO
    public static async Task connect()
    {
        var connectionString = "Host=localhost:5432;Username=postgres;Password=root1234;Database=Game"; // login do banco e local doo banco na rede
        dataSource = NpgsqlDataSource.Create(connectionString); // Inicializa a conexão
        
        while (!conectado)
        {
            try
            {
                //tenta conectar
                await using var connection = await dataSource.OpenConnectionAsync();
                Console.WriteLine("Conectado: " + connection);
                conectado = true;
            }
            catch (Exception e)
            {
                //se der erro vai tentando a cada 1 Segundo
                Console.WriteLine("Erro ao conectar: " + e);
                await Task.Delay(1000);
            }
        }
    }
    public  static void testConnection()
    {
        if (!conectado || dataSource == null)
        {
            throw new DatabaseNotConnectedException();
        }   
    }
    public static async Task Setup()
    {
        await using var cmd = dataSource.CreateCommand(@"
        

        -- Tabela de Salas
        CREATE TABLE IF NOT EXISTS salas (
            id SERIAL primary key,
	        name varchar(255) not null,
	        descricao text,
	        created_at timestamp
        );

        -- Tabela de Usuários (com FK para salas)
        CREATE TABLE IF NOT EXISTS usuarios (
        	id SERIAL primary KEY,
	        username varchar(255) unique not null,
	        name varchar(255) not null,
	        email varchar(255) unique,
	        id_sala INTEGER references salas(id) on delete set null,
	        password varchar(255),
	        created_at timestamp
        );
    ");

        await cmd.ExecuteNonQueryAsync();
        Console.WriteLine("[DB] Tabelas verificadas/criadas com sucesso.");
    }
}