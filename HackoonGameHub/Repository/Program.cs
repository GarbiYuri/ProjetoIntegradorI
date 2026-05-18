namespace Repository;
using System;
using System.Threading.Tasks;
using Models;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Iniciando Teste de API Local (Sem Godot) ===");

        // 1. Se inscreve no evento para simular o comportamento da API
        UDPService.OnMessageReceived += (ip, mensagem) =>
        {
            Console.WriteLine($"\n[API CALLBACK] Sucesso!");
            Console.WriteLine($"-> Origem (IP): {ip}");
            Console.WriteLine($"-> Conteúdo recebido: {mensagem}");
            
            // Aqui você simularia o processamento dos dados, ex:
            // Se a mensagem for um JSON de uma Sala, você instanciaria o objeto Sala aqui.
        };

        // 2. Roda o servidor UDP em uma Thread separada (Task) para não travar o console
        _ = Task.Run(() => UDPService.StartListeningAsync());

        // Aguarda 1 segundo apenas para garantir que o servidor inicializou
        await Task.Delay(1000);

        // 3. Simula um envio de dados na rede local
        string dadosDaSalaFicticia = "ID: 1 | Nome: Sala dos Alunos | Descrição: Aula de C#";
        UDPService.SendBroadcast(dadosDaSalaFicticia);

        Console.WriteLine("\nPressione qualquer tecla para fechar o teste...");
        Console.ReadKey();
    }
}    
