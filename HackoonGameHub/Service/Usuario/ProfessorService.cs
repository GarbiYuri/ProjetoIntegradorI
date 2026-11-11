using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Models;
using Repository.Exceptions;
using Repository.Postgress;

namespace Service;

public static class ProfessorService
{
    private static bool _broadcastando = false;

    public static async Task RegisterProfessor(Professor professor)
    {
        professor.password = BCrypt.Net.BCrypt.HashPassword(professor.password);
        await ProfessorDB.Create(professor);
    }

    public static async Task<Professor?> LoginProfessor(string email, string password)
    {

        Professor professor;
       
            professor = await ProfessorDB.GetProfessorByEmail(email);
            bool logged = SecurityService.ValidarSenha(password, professor.password);
            if (!logged)
            {
                throw new ResourceNotFoundException("Email e/ou senha Incorretos!");
            }

            return professor;
    }

    /// <summary>
    /// IP do professor na rede local (fica no anúncio para o aluno sempre
    /// conectar no endereço certo, em vez de variar conforme o pacote).
    /// </summary>
    private static string _ipLocal = string.Empty;

    public static string IpLocal
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_ipLocal))
            {
                return _ipLocal;
            }

            try
            {
                foreach (System.Net.IPAddress endereco in
                         System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName()))
                {
                    if (endereco.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                        !System.Net.IPAddress.IsLoopback(endereco))
                    {
                        _ipLocal = endereco.ToString();
                        return _ipLocal;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("[UDP] Não foi possível descobrir o IP local: " + e.Message);
            }

            _ipLocal = "127.0.0.1";
            return _ipLocal;
        }
    }

    /// <summary>
    /// Anuncia a sessão do professor na rede local (uma vez por segundo).
    ///
    /// O pacote é remontado a cada envio para que a lista de alunos
    /// conectados e a sala apareçam sempre atualizadas na tela do aluno.
    /// </summary>
    public static void SendMessage(string message, string roomName = "Sala", int salaId = 0,
        Func<List<AlunoOnline>>? listarAlunos = null)
    {
        if (_broadcastando) return;
        _broadcastando = true;

        _ = Task.Run(async () =>
        {
            while (_broadcastando)
            {
                try
                {
                    List<AlunoOnline> alunos = listarAlunos?.Invoke() ?? new List<AlunoOnline>();

                    var pacoteServidor = new NetworkPacket
                    {
                        Tipo = "DESCOBERTA_SERVIDOR",
                        DadosJson = JsonSerializer.Serialize(new
                        {
                            Status = "LOBBY_ABERTO",
                            Message = message,
                            Room = roomName,
                            Port = DB.Porta,
                            SalaId = salaId,
                            HostIp = IpLocal,
                            Alunos = alunos
                        })
                    };

                    // Anúncio vai para a porta do ALUNO (11001), que é onde
                    // ele escuta as sessões abertas.
                    BroadcastUDP.SendBroadcast(pacoteServidor, BroadcastUDP.PortaAluno);
                }
                catch (Exception e)
                {
                    Console.WriteLine("[UDP] Falha ao anunciar o professor: " + e.Message);
                }

                await Task.Delay(1000);
            }
        });
    }

    public static void StopMessage()
    {
        _broadcastando = false;
    }
    public static async Task<string> StartGameAsync()
    {
        string resultado = await DB.Connect();
        
        return resultado;
    }

    public static async Task<Professor> Update(Professor professor)
    {
        if (professor.id < 0)
        {
            throw new ResourceNotFoundException("Usuario não logaddo");
        }
        
        Professor oldProfessor = await ProfessorDB.GetProfessorById(professor.id);
        
        if (oldProfessor == null)
        {
            throw new ResourceNotFoundException("Não existe Cadastro No banco");
        }

        if (string.IsNullOrEmpty(professor.email))
        {
            professor.email = oldProfessor.email;
        }

        if (string.IsNullOrEmpty(professor.email))
        {
            professor.email = oldProfessor.email;
        }

        if (string.IsNullOrEmpty(professor.password))
        {
            professor.password = oldProfessor.password;
        }
        else
        {
            professor.password = SecurityService.GerarHaskSenha(professor.password);
        }
        
        await ProfessorDB.Update(professor, professor.id);

        return professor;

    }
    public static async Task<NetworkPacket?> carregarAlunos()
    {
        var tcs = new TaskCompletionSource<NetworkPacket>();
  
        Action<string, string>? tratarMensagemUDP = null;
        tratarMensagemUDP = (senderIP, jsonRecebido) =>
        {
            try
            {
                var pacote = JsonSerializer.Deserialize<NetworkPacket>(jsonRecebido);
                if (pacote != null && pacote.Tipo == "ENTRADA")
                {
                    
                    var networkPacketResult = new NetworkPacket
                    {
                        Tipo = pacote.Tipo,
                        DadosJson = jsonRecebido
                    };
                    BroadcastUDP.OnMessageReceived -= tratarMensagemUDP;
                    BroadcastUDP.StopListening();
                    tcs.TrySetResult(networkPacketResult);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            
        };
        BroadcastUDP.OnMessageReceived += tratarMensagemUDP;
        
        _ = Task.Run(() => BroadcastUDP.StartListeningProfessor());

        // Espera no máximo 20 segundos por um aluno; se ninguém entrar,
        // libera a tela para o professor abrir o lobby mesmo assim.
        Task concluida = await Task.WhenAny(tcs.Task, Task.Delay(20000));

        if (concluida != tcs.Task)
        {
            BroadcastUDP.OnMessageReceived -= tratarMensagemUDP;
            BroadcastUDP.StopListening();
            return null;
        }

        return await tcs.Task;
    }
    
}