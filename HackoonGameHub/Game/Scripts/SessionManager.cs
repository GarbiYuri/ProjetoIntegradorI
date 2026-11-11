using System.Collections.Generic;
using Godot;
using Models;
using Repository.Postgress;

namespace Game.Scripts;

/// <summary>
/// Modo em que o lobby deve iniciar quando a cena for carregada.
/// </summary>
public enum LobbyMode
{
    None,
    Solo,
    Host,
    Client
}

public partial class SessionManager : Node
{
    public Usuario usuario { get; set; }
    public List<Usuario> usuariosNaRede { get; set; } = new List<Usuario>();
    public Sala sala { get; set; }

    // Id da sala em que o aluno entrou (0 = ainda não entrou em sala nenhuma).
    // Vem no broadcast do professor e permite ligar a conta do aluno à sala.
    public int salaId { get; set; }

    // Nick escolhido pelo aluno na tela de conta (aparece na lista de
    // alunos da sala que o professor anuncia).
    public string playerNick { get; set; } = "";

    // ---------------------------------------------------------------------
    // Ponte entre as telas (login/salas) e a cena do Lobby.
    // ---------------------------------------------------------------------
    public LobbyMode lobbyMode { get; set; } = LobbyMode.None;
    public string lobbyIp { get; set; } = "127.0.0.1";
    public string hostName { get; set; } = "";
    public string playerName { get; set; } = "Jogador";
    public string roomName { get; set; } = "Lobby";

    // Cor do personagem escolhida na tela de conta (formato hex, ex. "#rrggbb").
    public string personagemColorHex { get; set; } = "";

    /// <summary>Cor atual do personagem; branco quando nenhuma foi escolhida.</summary>
    public Color GetPersonagemColor()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(personagemColorHex))
            {
                return new Color(personagemColorHex);
            }
        }
        catch
        {
            // Hex inválido: volta para a arte original (branco).
        }

        return Colors.White;
    }

    public override void _Ready()
    {
        GD.Print("Session Manager Ready");
    }

    public override void _ExitTree()
    {
        // Se esta máquina iniciou o PostgreSQL portátil, encerra junto com o jogo.
        // Um banco externo (Docker/serviço/máquina remota) não é afetado.
        DB.StopLocalServer();
    }
}
