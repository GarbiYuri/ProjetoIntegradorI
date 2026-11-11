using System.Net;
using System.Runtime.InteropServices.ComTypes;
using System.Text.Json;
using Models;
using Repository.Exceptions;
using Repository.Postgress;

namespace Service;

public class AlunoService
{
    public static async Task RegisterAluno(Aluno aluno)
    {
        if (!string.IsNullOrEmpty(aluno.password))
        {
            aluno.password = SecurityService.GerarHaskSenha(aluno.password);
        }

        await AlunoDB.Create(aluno);
    }

    /// <summary>Verifica se já existe uma conta com esse nick.</summary>
    public static async Task<bool> Existe(string username)
    {
        return await AlunoDB.AlunoExists(username);
    }
    public static async Task<Aluno?> LoginAluno(string username, string? password)
    {
        Aluno aluno;
        try
        {
            aluno = await AlunoDB.GetAlunoByUsername(username);
            if (!string.IsNullOrEmpty(aluno.password))
            {
                if (string.IsNullOrEmpty(password))
                {
                    throw new ResourceNotFoundException("Usuario Protegido por senha");
                }
                bool logged = SecurityService.ValidarSenha(password, aluno.password);
                if (!logged)
                {
                    throw new ResourceNotFoundException("Usuario e/ou senha Incorretos!");
                }
            }
            return aluno;
        }
        catch (ResourceNotFoundException e)
        {
            Console.WriteLine(e.Message);
            return null;
        }
        catch (Exception e)
        {
            Console.WriteLine("Erro Inesperado: " + e.Message);
            return null;
        }
        
    }

    public static async Task<List<Aluno>> showAll()
    {
        return await AlunoDB.Listar();
    }
    public static async Task<NetworkPacket?> enterGame()
    {
        var ipGame = new TaskCompletionSource<string>();
        NetworkPacket networkPacket = new NetworkPacket();
        Action<string, string>? tratarMensagemUDP = null;
        tratarMensagemUDP = (senderIP, jsonRecebido) =>
        {
            try
            {
                var pacote = JsonSerializer.Deserialize<NetworkPacket>(jsonRecebido);
                if (pacote != null && pacote.Tipo == "DESCOBERTA_SERVIDOR")
                {
                    
                    networkPacket.Tipo = pacote.Tipo;
                    networkPacket.DadosJson = jsonRecebido;
                    ipGame.TrySetResult(senderIP);
                    BroadcastUDP.OnMessageReceived -= tratarMensagemUDP;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
            
        };
        BroadcastUDP.OnMessageReceived += tratarMensagemUDP;
        
        _ = Task.Run(() => BroadcastUDP.StartListeningAluno());

        // Se nenhum professor estiver anunciando, não deixa o aluno preso para sempre.
        Task concluida = await Task.WhenAny(ipGame.Task, Task.Delay(15000));

        if (concluida != ipGame.Task)
        {
            BroadcastUDP.OnMessageReceived -= tratarMensagemUDP;
            BroadcastUDP.StopListening();
            return null;
        }

        string ipServer = await ipGame.Task;
        
        networkPacket.senderIP = ipServer;
        
        BroadcastUDP.StopListening();

        // O professor anuncia a porta do banco dele no broadcast; se ele usou
        // uma porta alternativa (ex.: 5433 porque o Docker ocupa a 5432), o
        // aluno conecta na porta certa sem configurar nada.
        int portaProfessor = LerPortaDoPacote(networkPacket.DadosJson);

        if (portaProfessor > 0)
        {
            DB.DefinirPorta(portaProfessor);
        }
        
        await DB.Connect(ipServer, iniciarLocal: false);
        
        return networkPacket;
    }

    /// <summary>
    /// Extrai a "Port" do payload do broadcast (o JSON tem o formato
    /// { DadosJson: "{ ... Port: 5433 ... }" }).
    /// </summary>
    private static int LerPortaDoPacote(string jsonRecebido)
    {
        try
        {
            using JsonDocument docExterior = JsonDocument.Parse(jsonRecebido);

            if (!docExterior.RootElement.TryGetProperty("DadosJson", out JsonElement dadosElement))
            {
                return 0;
            }

            string? jsonInterno = dadosElement.GetString();

            if (string.IsNullOrWhiteSpace(jsonInterno))
            {
                return 0;
            }

            using JsonDocument docInterno = JsonDocument.Parse(jsonInterno);

            if (docInterno.RootElement.TryGetProperty("Port", out JsonElement portaElement) &&
                portaElement.TryGetInt32(out int porta))
            {
                return porta;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("[ALUNO] Broadcast sem porta legível: " + e.Message);
        }

        return 0;
    }

    public static async Task<Aluno> Read(int id)
    {
        Aluno aluno = await AlunoDB.GetAlunoById(id);

        if (aluno == null)
        {
            throw new ResourceNotFoundException($"Nenhuma sala foi encontrada.");
        }
        return aluno;
    }
    private static bool _enviandoPersona = false;

    public static void SendMyPersona(int id)
    {
        if (_enviandoPersona) return;
        _enviandoPersona = true;

        var pacoteServidor = new NetworkPacket{
            Tipo = "ENTRADA",
            DadosJson = JsonSerializer.Serialize(new {
                Status = "ENTROU_SERVIDOR",
                ID = id
            })
        };

        _ = Task.Run(async () =>
        {
            while (_enviandoPersona)
            {
                try
                {
                    // Importante: enviamos o objeto (e não o JSON já serializado),
                    // senão o pacote chega como uma string JSON dentro de outra.
                    // Vai para a porta do PROFESSOR (11000), que é onde ele
                    // escuta os alunos entrando.
                    BroadcastUDP.SendBroadcast(pacoteServidor, BroadcastUDP.PortaProfessor);
                }
                catch (Exception e)
                {
                    Console.WriteLine("[UDP] Falha ao enviar personagem: " + e.Message);
                }

                await Task.Delay(1000);
            }
        });
    }

    public static void StopSendingPersona()
    {
        _enviandoPersona = false;
    }
}