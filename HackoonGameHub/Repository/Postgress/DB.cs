using System.Diagnostics;
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
        var connectionString = "Host=127.0.0.1;Port=5432;Username=postgres;Database=postgres;Password="; // login do banco e local doo banco na rede
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
    public static void StartDatabaseLinux()
    {
        
        if (Process.GetProcessesByName("postgres").Length > 0)
        {
            Console.WriteLine("PostgreSQL já está em execução. Pulando inicialização.");
            return;
        }
        
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string pgsqlPath = Path.Combine(baseDir, "linuxPG");
        string pgctlPath = Path.Combine(pgsqlPath, "bin", "pg_ctl");
        string dataPath = Path.Combine(baseDir, "data");
        string logPath = Path.Combine(baseDir, "logfile");

        // Partes Essenciais para Linux
            // 1. Corrige a permissão 0700 exigida pelo Postgres no Linux
            Process.Start("chmod", $"700 \"{dataPath}\"").WaitForExit();

            // 2. Garante que subpastas essenciais (que o Git pode ter ignorado) existam
            string[] folders = { "pg_tblspc", "pg_replslot", "pg_snapshots", "pg_commit_ts" };
            foreach (var folder in folders)
            {
                string fullPath = Path.Combine(dataPath, folder);
                if (!Directory.Exists(fullPath)) Directory.CreateDirectory(fullPath);
            }
            // Partes Essenciais para Linux

        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = pgctlPath,
            Arguments = $"-D \"{dataPath}\" -l \"{logPath}\" start",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = baseDir // Crucial para o binário achar as libs locais
        };

        Process.Start(psi);
    }
    public static void StartDatabaseWindows()
    {
        // No Windows, verificamos o processo sem a necessidade de comandos shell
        if (Process.GetProcessesByName("postgres").Length > 0)
        {
            Console.WriteLine("PostgreSQL já está em execução no Windows.");
            return;
        }

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        // No seu caso, a pasta de binários do Windows (winPG)
        string pgsqlPath = Path.Combine(baseDir, "winPG"); 
        string pgctlPath = Path.Combine(pgsqlPath, "bin", "pg_ctl.exe");
        string dataPath = Path.Combine(baseDir, "data");
        string logPath = Path.Combine(baseDir, "logfile");
        string sharePath = Path.Combine(baseDir, "share", "postgresql");
        
        string[] folders = { "pg_tblspc", "pg_replslot", "pg_snapshots", "pg_commit_ts" };
        foreach (var folder in folders)
        {
            string fullPath = Path.Combine(dataPath, folder);
            if (!Directory.Exists(fullPath)) Directory.CreateDirectory(fullPath);
        }

        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = pgctlPath,
            // O comando de inicialização é idêntico
            Arguments = $"-D \"{dataPath}\" -l \"{logPath}\" start",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.Combine(pgsqlPath, "bin")
        };

        try 
        {
            Process.Start(psi);
            Console.WriteLine("Servidor Windows iniciado.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Erro ao iniciar no Windows: " + ex.Message);
        }
    }
}