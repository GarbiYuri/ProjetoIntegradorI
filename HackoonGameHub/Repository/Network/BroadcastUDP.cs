using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Repository.Exceptions;

namespace Repository.Postgress;

/// <summary>
/// Descoberta do servidor por UDP na rede local.
///
/// Duas portas para professor e aluno poderem rodar na MESMA máquina sem
/// disputar o bind do socket:
/// - PortaProfessor (11000): professor ouve os pacotes ENTRADA dos alunos;
/// - PortaAluno (11001): aluno ouve os pacotes DESCOBERTA_SERVIDOR do professor.
///
/// O envio vai para vários destinos para funcionar em qualquer cenário:
/// - 127.0.0.1 (mesmo PC);
/// - 255.255.255.255 (broadcast limitado);
/// - broadcast dirigido de cada interface (ex.: 192.168.15.255), que atravessa
///   roteadores/APs que descartam o 255.255.255.255.
/// </summary>
public class BroadcastUDP
{
    public const int PortaProfessor = 11000;
    public const int PortaAluno = 11001;
    
    // O evento que passa a string do JSON e o IP
    public static event Action<string, string>? OnMessageReceived;

    private static readonly object _lock = new();
    private static UdpClient? _listener;
    private static bool _estaOuvindo = false;
    private static int _geracao = 0;
    
    /// <summary>
    /// Porta onde ESTA máquina vai ouvir. O professor usa PortaProfessor e o
    /// aluno usa PortaAluno (chamado via StartListeningAsync(porta)).
    /// </summary>
    public static int PortaOuvindo { get; private set; } = PortaProfessor;

    /// <summary>Ouvinte do professor (recebe ENTRADA dos alunos).</summary>
    public static Task StartListeningProfessor() => StartListeningAsync(PortaProfessor);

    /// <summary>Ouvinte do aluno (recebe DESCOBERTA_SERVIDOR do professor).</summary>
    public static Task StartListeningAluno() => StartListeningAsync(PortaAluno);

    private static async Task StartListeningAsync(int porta)
    {
        int minhaGeracao;

        lock (_lock)
        {
            // Se já tiver um ouvinte rodando nesta máquina, não abre outro
            if (_estaOuvindo) return;

            _estaOuvindo = true;
            minhaGeracao = ++_geracao;
            PortaOuvindo = porta;
        }

        UdpClient? udpListener = null;

        try
        {
            udpListener = new UdpClient(porta);

            lock (_lock)
            {
                _listener = udpListener;
            }

            Console.WriteLine($"[UDP] Ouvinte ativado com sucesso na porta {porta}...");

            while (_estaOuvindo && minhaGeracao == _geracao)
            {
                try
                {
                    // Aguarda a chegada de um pacote de forma assíncrona
                    var result = await udpListener!.ReceiveAsync();
                    
                    string jsonMessage = Encoding.UTF8.GetString(result.Buffer);
                    string senderIP = result.RemoteEndPoint.Address.ToString();

                    // Dispara o evento de forma segura
                    OnMessageReceived?.Invoke(senderIP, jsonMessage);
                }
                catch (SocketException) when (!_estaOuvindo || minhaGeracao != _geracao)
                {
                    break;
                }
                catch (ObjectDisposedException) when (!_estaOuvindo || minhaGeracao != _geracao)
                {
                    break;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                {
                    // Erro comum de UDP no Windows (ignora e continua ouvindo)
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception)
                {
                    // Se der erro de leitura ou JSON corrompido, não mata o loop! 
                    // Apenas ignora o pacote ruim e espera o próximo
                    if (!_estaOuvindo || minhaGeracao != _geracao) break;
                    continue;
                }
            }
        }
        catch (Exception e)
        {
            if (_estaOuvindo && minhaGeracao == _geracao)
            {
                _estaOuvindo = false;
                throw new NetworkBroadcastException("Erro fatal de inicialização do socket Broadcast", e);
            }
        }
        finally
        {
            // Garante que se o loop parar, a porta é liberada IMEDIATAMENTE
            lock (_lock)
            {
                if (minhaGeracao == _geracao)
                {
                    _estaOuvindo = false;
                }

                if (_listener == udpListener)
                {
                    _listener = null;
                }
            }

            try
            {
                udpListener?.Close();
                udpListener?.Dispose();
            }
            catch
            {
                // Ignora erros ao fechar um socket que já foi fechado.
            }
        }
    }

    public static void StopListening()
    {
        UdpClient? listener;

        lock (_lock)
        {
            _estaOuvindo = false;
            _geracao++;
            listener = _listener;
            _listener = null;
        }

        try
        {
            listener?.Close();
            listener?.Dispose();
        }
        catch
        {
            // Ignora erros ao fechar um socket que já foi fechado.
        }
    }
    
    /// <summary>
    /// Envia o pacote para a rede local na porta indicada
    /// (PortaProfessor para o aluno anunciar a entrada,
    /// PortaAluno para o professor anunciar a sessão).
    /// </summary>
    public static void SendBroadcast(object messageObject, int portaDestino)
    {
        string json = JsonSerializer.Serialize(messageObject);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        // Um socket só para todos os destinos.
        using var udpClient = new UdpClient { EnableBroadcast = true };

        int enviosOk = 0;
        Exception? ultimoErro = null;

        foreach (IPEndPoint destino in ListarDestinos(portaDestino))
        {
            try
            {
                udpClient.Send(bytes, bytes.Length, destino);
                enviosOk++;
            }
            catch (Exception e)
            {
                ultimoErro = e;
            }
        }

        if (enviosOk == 0)
        {
            throw new NetworkBroadcastException(
                "Falha ao disparar pacote na rede local.", ultimoErro);
        }

        Console.WriteLine($"[UDP] Anúncio enviado para {enviosOk} destinos na porta {portaDestino}.");
    }

    /// <summary>
    /// Destinos de envio: loopback (mesmo PC), broadcast limitado e o
    /// broadcast dirigido de cada interface de rede (mais aceito por roteadores).
    /// </summary>
    private static List<IPEndPoint> ListarDestinos(int porta)
    {
        var destinos = new List<IPEndPoint>
        {
            new(IPAddress.Loopback, porta),
            new(IPAddress.Broadcast, porta)
        };

        try
        {
            foreach (NetworkInterface interfaceRede in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (interfaceRede.OperationalStatus != OperationalStatus.Up ||
                    interfaceRede.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (UnicastIPAddressInformation informacao in interfaceRede.GetIPProperties().UnicastAddresses)
                {
                    if (informacao.Address.AddressFamily != AddressFamily.InterNetwork ||
                        informacao.IPv4Mask is null || informacao.IPv4Mask.Equals(IPAddress.Any))
                    {
                        continue;
                    }

                    // Endereço de broadcast da sub-rede: ip | ~máscara
                    var endereco = informacao.Address.GetAddressBytes();
                    var mascara = informacao.IPv4Mask.GetAddressBytes();
                    var broadcast = new byte[4];

                    for (int i = 0; i < 4; i++)
                    {
                        broadcast[i] = (byte)(endereco[i] | ~mascara[i]);
                    }

                    var ipBroadcast = new IPAddress(broadcast);

                    if (!destinos.Any(d => d.Address.Equals(ipBroadcast)))
                    {
                        destinos.Add(new IPEndPoint(ipBroadcast, porta));
                    }
                }
            }
        }
        catch
        {
            // Sem acesso às interfaces, o loopback + 255.255.255.255 ainda valem.
        }

        return destinos;
    }
}
