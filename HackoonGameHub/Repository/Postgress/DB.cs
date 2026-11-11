using System.Diagnostics;
using System.Runtime.InteropServices;
using Npgsql;
using Repository.Exceptions;

namespace Repository.Postgress;

public class DB
{
    private const int PortaPadrao = 5432;
    private const string MarcadorInicio = "# >>> HackoonGameHub";
    private const string MarcadorFim = "# <<< HackoonGameHub";

    private static readonly SemaphoreSlim _conexaoLock = new(1, 1);
    private static int _porta = ResolverPorta();

    private static string? _pgCtl;
    private static string? _dataDir;
    private static string? _logPath;
    private static bool _servidorIniciadoPeloApp;

    public static NpgsqlDataSource dataSource { get; private set; } = null!;
    public static bool conectado = false;

    public static int Porta => _porta;

    /// <summary>
    /// Ajusta a porta usada na conexão (o aluno recebe a porta do professor
    /// pelo broadcast UDP). Só tem efeito antes de conectar.
    /// </summary>
    public static void DefinirPorta(int porta)
    {
        if (conectado)
        {
            return;
        }

        if (porta > 0 && porta <= 65535)
        {
            _porta = porta;
        }
    }

    /// <summary>
    /// Conecta ao PostgreSQL. No professor (127.0.0.1/localhost) o servidor
    /// portátil (Windows ou Linux, conforme o SO) é iniciado automaticamente.
    /// O aluno passa iniciarLocal = false: ele é cliente e conecta direto no
    /// banco do professor pelo IP/porta recebidos no broadcast.
    /// </summary>
    public static async Task<string> Connect(string ipServidor = "127.0.0.1", bool iniciarLocal = true)
    {
        if (conectado)
        {
            return "Já Conectado";
        }

        await _conexaoLock.WaitAsync();
        try
        {
            if (conectado)
            {
                return "Já Conectado";
            }

            bool local = EhLocal(ipServidor) && iniciarLocal;

            if (local)
            {
                await IniciarServidorLocalAsync();
            }

            var connectionString =
                $"Host={ipServidor};Port={_porta};Username=postgres;Database=postgres;Password=root1234";

            dataSource = NpgsqlDataSource.Create(connectionString);

            int tentativas = 0;
            Exception? ultimoErro = null;

            while (tentativas < 10)
            {
                try
                {
                    await using var connection = await dataSource.OpenConnectionAsync();
                    break;
                }
                catch (Exception e)
                {
                    ultimoErro = e;
                    tentativas++;

                    if (tentativas >= 10)
                    {
                        dataSource.Dispose();
                        dataSource = null!;

                        string dica = local
                            ? $" Verifique se a porta {_porta} já está em uso (por exemplo, um container Docker) e veja o log em {_logPath}."
                            : " Verifique se o PostgreSQL do professor aceita conexões na rede.";

                        throw new Exception(
                            $"Não foi possível conectar ao PostgreSQL em {ipServidor}:{_porta}." + dica,
                            ultimoErro);
                    }

                    await Task.Delay(1000);
                }
            }

            // As tabelas são criadas apenas no banco local do professor.
            if (local)
            {
                await Setup();
            }

            conectado = true;
            return "Conectado ao Banco!";
        }
        finally
        {
            _conexaoLock.Release();
        }
    }

    /// <summary>
    /// Encerra o servidor portátil caso ele tenha sido iniciado por este app.
    /// Um PostgreSQL externo (Docker, serviço do sistema ou máquina remota)
    /// não é afetado.
    /// </summary>
    public static void StopLocalServer()
    {
        if (!_servidorIniciadoPeloApp || string.IsNullOrEmpty(_pgCtl) || string.IsNullOrEmpty(_dataDir))
        {
            return;
        }

        try
        {
            ExecutarFerramenta(_pgCtl, $"-D \"{_dataDir}\" -m fast -w stop")
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            // Se o processo já tiver encerrado, não há nada a fazer.
        }

        _servidorIniciadoPeloApp = false;

        try
        {
            dataSource?.Dispose();
        }
        catch
        {
            // Ignora erros ao liberar um datasource já descartado.
        }

        dataSource = null!;
        conectado = false;
    }

    public static void testConnection()
    {
        if (!conectado || dataSource == null)
        {
            throw new DatabaseNotConnectedException();
        }
    }

    private static int ResolverPorta()
    {
        string? valor = Environment.GetEnvironmentVariable("HACKOON_PG_PORT");

        if (int.TryParse(valor, out int porta) && porta > 0 && porta <= 65535)
        {
            return porta;
        }

        return PortaPadrao;
    }

    /// <summary>
    /// Se a porta configurada já estiver em uso (Docker, serviço etc.),
    /// avança para a primeira porta livre (5433, 5434...). Com
    /// HACKOON_PG_PORT definido, a porta escolhida pelo usuário é respeitada.
    /// </summary>
    private static void ResolverPortaLivre()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HACKOON_PG_PORT")))
        {
            return;
        }

        int limite = _porta + 100;

        for (int candidato = _porta; candidato < limite; candidato++)
        {
            if (!PortaOcupada(candidato))
            {
                if (candidato != _porta)
                {
                    Console.WriteLine(
                        $"[DB] Porta {_porta} já está em uso (ex.: Docker). " +
                        $"Usando a porta {candidato} para o PostgreSQL embutido.");
                    _porta = candidato;
                }

                return;
            }
        }

        throw new Exception(
            $"Nenhuma porta livre encontrada entre {_porta} e {limite - 1}. " +
            "Encerre o processo que ocupa a 5432 (ex.: container Docker) ou " +
            "defina HACKOON_PG_PORT com uma porta livre.");
    }

    private static bool PortaOcupada(int porta)
    {
        try
        {
            using var listener = new System.Net.Sockets.TcpListener(
                System.Net.IPAddress.Loopback, porta);
            listener.Start();
            listener.Stop();
            return false;
        }
        catch (System.Net.Sockets.SocketException)
        {
            return true;
        }
    }

    private static bool EhLocal(string ipServidor)
    {
        return ipServidor == "127.0.0.1" || ipServidor == "localhost" || ipServidor == "::1";
    }

    private static async Task IniciarServidorLocalAsync()
    {
        if (_servidorIniciadoPeloApp)
        {
            return;
        }

        string raiz = DescobrirRaizPostgres();
        string bin = Path.Combine(raiz, "bin");

        string pgCtl = Path.Combine(bin, OperatingSystem.IsWindows() ? "pg_ctl.exe" : "pg_ctl");
        string initdb = Path.Combine(bin, OperatingSystem.IsWindows() ? "initdb.exe" : "initdb");

        if (!File.Exists(pgCtl) || !File.Exists(initdb))
        {
            throw new Exception(
                $"Binários do PostgreSQL portátil não encontrados em '{raiz}' para {RuntimeInformation.OSDescription}. " +
                "No Windows espera-se pgsql/windows/bin e no Linux pgsql/linux/bin.");
        }

        _pgCtl = pgCtl;
        _dataDir = Path.Combine(raiz, "data");
        _logPath = Path.Combine(raiz, "postgres.log");

        // Servidor de uma execução anterior ainda ativo (app fechou sem parar
        // o banco, por exemplo): usa a porta que está gravada no
        // postgresql.conf dele, sem reescrever nada.
        var status = await ExecutarFerramenta(pgCtl, $"-D \"{_dataDir}\" status");

        if (status.Codigo == 0)
        {
            int portaGravada = LerPortaDoConfig(_dataDir);

            if (portaGravada > 0 && portaGravada != _porta)
            {
                Console.WriteLine(
                    $"[DB] PostgreSQL embutido já estava rodando na porta {portaGravada}");
                _porta = portaGravada;
            }

            // Adota o servidor órfão: ao fechar o jogo, ele também é encerrado.
            _servidorIniciadoPeloApp = true;
            Console.WriteLine("[DB] PostgreSQL embutido já estava rodando");
            return;
        }

        // Se a porta desejada já estiver ocupada (ex.: um Docker de teste na
        // 5432), o servidor embutido sobe na primeira porta livre seguinte.
        ResolverPortaLivre();

        if (!File.Exists(Path.Combine(_dataDir, "PG_VERSION")))
        {
            if (Directory.Exists(_dataDir) && Directory.EnumerateFileSystemEntries(_dataDir).Any())
            {
                throw new Exception(
                    $"A pasta de dados '{_dataDir}' existe, mas não é um banco PostgreSQL válido. " +
                    "Mova ou apague essa pasta para que o app possa inicializar o banco portátil.");
            }

            Directory.CreateDirectory(_dataDir);

            var init = await ExecutarFerramenta(
                initdb,
                $"-D \"{_dataDir}\" -U postgres --encoding=UTF8 --locale=C -A trust");

            if (init.Codigo != 0)
            {
                throw new Exception(
                    $"Falha ao inicializar o PostgreSQL portátil (código {init.Codigo}).\n{init.Saida}");
            }

            Console.WriteLine($"[DB] PostgreSQL portátil inicializado em {_dataDir}");
        }

        ConfigurarArquivos(_dataDir);

        var inicio = await ExecutarFerramenta(
            pgCtl,
            $"-D \"{_dataDir}\" -l \"{_logPath}\" -o \"-p {_porta}\" -w start");

        if (inicio.Codigo != 0)
        {
            string log = LerFinalDoLog(_logPath);
            throw new Exception(
                $"Não foi possível iniciar o PostgreSQL portátil (código {inicio.Codigo}).\n" +
                $"{inicio.Saida}\n{log}");
        }

        _servidorIniciadoPeloApp = true;
        Console.WriteLine($"[DB] PostgreSQL portátil iniciado na porta {_porta}");
    }

    private static string DescobrirRaizPostgres()
    {
        string baseDir = AppContext.BaseDirectory;

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(baseDir, "pgsql", "windows");
        }

        if (OperatingSystem.IsLinux())
        {
            return Path.Combine(baseDir, "pgsql", "linux");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(baseDir, "pgsql", "macos");
        }

        return Path.Combine(baseDir, "pgsql");
    }

    private static void ConfigurarArquivos(string dataDir)
    {
        // listen_addresses/port ficam em um bloco que pode ser reescrito sem
        // duplicar linhas a cada inicialização.
        string configFile = Path.Combine(dataDir, "postgresql.conf");
        string configuracao = File.Exists(configFile) ? File.ReadAllText(configFile) : string.Empty;

        int inicio = configuracao.IndexOf(MarcadorInicio, StringComparison.Ordinal);

        if (inicio >= 0)
        {
            int fim = configuracao.IndexOf(MarcadorFim, inicio, StringComparison.Ordinal);

            if (fim >= 0)
            {
                configuracao = configuracao.Remove(inicio, fim - inicio + MarcadorFim.Length);
            }
        }

        configuracao = configuracao.TrimEnd() + Environment.NewLine +
                       MarcadorInicio + Environment.NewLine +
                       "listen_addresses = '*'" + Environment.NewLine +
                       $"port = {_porta}" + Environment.NewLine +
                       MarcadorFim + Environment.NewLine;

        File.WriteAllText(configFile, configuracao);

        string hbaFile = Path.Combine(dataDir, "pg_hba.conf");
        string hba = File.Exists(hbaFile) ? File.ReadAllText(hbaFile) : string.Empty;

        if (!hba.Contains("0.0.0.0/0", StringComparison.Ordinal))
        {
            hba = hba.TrimEnd() + Environment.NewLine +
                  "# Rede local (Hackoon Game Hub)" + Environment.NewLine +
                  "host all all 0.0.0.0/0 trust" + Environment.NewLine +
                  "host all all ::/0 trust" + Environment.NewLine;

            File.WriteAllText(hbaFile, hba);
        }
    }

    private static async Task<(int Codigo, string Saida)> ExecutarFerramenta(
        string executavel,
        string argumentos)
    {
        var inicio = new ProcessStartInfo
        {
            FileName = executavel,
            Arguments = argumentos,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executavel) ?? AppContext.BaseDirectory
        };

        if (!OperatingSystem.IsWindows())
        {
            string lib = Path.GetFullPath(Path.Combine(inicio.WorkingDirectory, "..", "lib"));
            string atual = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? string.Empty;

            inicio.Environment["LD_LIBRARY_PATH"] =
                string.IsNullOrEmpty(atual) ? lib : lib + ":" + atual;
            inicio.Environment["LC_ALL"] = "C";
        }

        using var processo = Process.Start(inicio);

        if (processo == null)
        {
            return (-1, "Não foi possível iniciar o processo.");
        }

        string saida = await processo.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        string erro = await processo.StandardError.ReadToEndAsync().ConfigureAwait(false);

        await processo.WaitForExitAsync().ConfigureAwait(false);

        return (processo.ExitCode, (saida + Environment.NewLine + erro).Trim());
    }

    /// <summary>
    /// Lê a porta do bloco marcado no postgresql.conf de um servidor que já
    /// estava rodando (execução anterior do app).
    /// </summary>
    private static int LerPortaDoConfig(string dataDir)
    {
        try
        {
            string configFile = Path.Combine(dataDir, "postgresql.conf");

            if (!File.Exists(configFile))
            {
                return 0;
            }

            string configuracao = File.ReadAllText(configFile);

            int inicio = configuracao.IndexOf(MarcadorInicio, StringComparison.Ordinal);
            int fim = configuracao.IndexOf(MarcadorFim, StringComparison.Ordinal);

            if (inicio < 0 || fim < 0 || fim <= inicio)
            {
                return 0;
            }

            string bloco = configuracao[(inicio + MarcadorInicio.Length)..fim];

            foreach (string linha in bloco.Split('\n'))
            {
                string limpa = linha.Trim();

                if (limpa.StartsWith("port =", StringComparison.Ordinal) &&
                    int.TryParse(limpa["port =".Length..].Trim(), out int porta))
                {
                    return porta;
                }
            }
        }
        catch
        {
            // Se não conseguir ler, o comportamento antigo é mantido.
        }

        return 0;
    }

    private static string LerFinalDoLog(string caminho)
    {
        try
        {
            if (!File.Exists(caminho))
            {
                return string.Empty;
            }

            string[] linhas = File.ReadAllLines(caminho);
            int inicio = Math.Max(0, linhas.Length - 15);
            return "Últimas linhas do log:" + Environment.NewLine +
                   string.Join(Environment.NewLine, linhas[inicio..]);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static async Task Setup()
    {
        await using var cmd = dataSource.CreateCommand(@"
        -- Tabela de Salas
        CREATE TABLE IF NOT EXISTS salas (
            id SERIAL primary key,
            name varchar(255) not null,
            descricao text,
            created_at timestamp
        );

        -- Tabela de Alunos
        CREATE TABLE IF NOT EXISTS alunos (
            id SERIAL primary KEY,
            username varchar(255) unique not null,
            name varchar(255) not null,
            turma varchar(50),
            points INTEGER DEFAULT 0,
            level INTEGER DEFAULT 1,
            xp INTEGER DEFAULT 0,
            stars INTEGER DEFAULT 0,
            id_sala INTEGER references salas(id) on delete set null,
            password varchar(255),
            created_at timestamp
        );

        -- Tabela de Professores
        CREATE TABLE IF NOT EXISTS professores (
            id SERIAL primary KEY,
            name varchar(255) not null,
            email varchar(255) unique not null,
            password varchar(255),
            created_at timestamp
        );

        -- Tabela de Jogos (conexões internas criadas pelos alunos dentro do lobby)
        CREATE TABLE IF NOT EXISTS jogos (
            id SERIAL primary key,
            name varchar(255) not null,
            id_sala INTEGER references salas(id) on delete set null,
            id_aluno INTEGER references alunos(id) on delete set null,
            creator varchar(255),
            host_ip varchar(255),
            port INTEGER DEFAULT 7778,
            status varchar(32) DEFAULT 'ABERTO',
            created_at timestamp
        );
    ");

        await cmd.ExecuteNonQueryAsync();
    }
}
