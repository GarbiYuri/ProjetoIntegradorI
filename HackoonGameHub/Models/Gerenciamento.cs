using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Models;

public static class UDPService
{
    private const int Porta = 5432;
    
    // Evento disparado quando uma mensagem chega
    public static event Action<string, string>? OnMessageReceived;

    // 1. FUNÇÃO PARA OUVIR (API / Receptor)
    public static async Task StartListeningAsync()
    {
        using var udpListener = new UdpClient(Porta);
        Console.WriteLine($"[UDP Server] Ouvindo broadcasts na porta {Porta}...");

        while (true)
        {
            try
            {
                var result = await udpListener.ReceiveAsync();
                string message = Encoding.UTF8.GetString(result.Buffer);
                string senderIP = result.RemoteEndPoint.Address.ToString();

                // Dispara o evento notificando que dados chegaram
                OnMessageReceived?.Invoke(senderIP, message);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[UDP Server] Erro: {e.Message}");
                break;
            }
        }
    }

    // 2. FUNÇÃO PARA ENVIAR (Simulador de cliente ou de outra máquina)
    public static void SendBroadcast(string message)
    {
        using var udpClient = new UdpClient();
        udpClient.EnableBroadcast = true;
        
        var endPoint = new IPEndPoint(IPAddress.Broadcast, Porta);
        byte[] bytes = Encoding.UTF8.GetBytes(message);

        udpClient.Send(bytes, bytes.Length, endPoint);
        Console.WriteLine($"[UDP Client] Broadcast enviado: {message}");
    }
}