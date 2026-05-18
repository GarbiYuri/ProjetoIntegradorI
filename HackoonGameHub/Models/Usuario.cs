using System;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Models;

public class Usuario
{
    public int id { get; set; }
    public string username { get; set; }
    public string name { get; set; }
    public string email { get; set; }
    public int? id_sala { get; set; }
    public string password { get; set; }    
    public string turma { get; set; }
    public int points { get; set; } = 0;
    public int level { get; set; } = 1;
    public int xp { get; set; } = 0;
    public int stars { get; set; } = 0;
    public DateTime createdAt { get; set; }

    // Construtor para criação de novos usuários (com validação)
    public Usuario(string username, string name)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("Nick obrigatório");
        }

        if (username.Length < 3) 
        {
            throw new ArgumentException("Nick muito curto");
        }

        if (username.Length > 12)
        {
            throw new ArgumentException("Nick muito longo");
        }

        if (!username.All(c => char.IsLetterOrDigit(c) || c == ' '))
        {
            throw new ArgumentException("Nick contém caracteres inválidos");
        }
        
        // Formata o Nick: Ex: "jOgAdOr" vira "Jogador"
        string trimmed = username.Trim();
        this.username = char.ToUpper(trimmed[0]) + trimmed.Substring(1).ToLower();
        
        this.name = name.Trim();
        createdAt = DateTime.Now;
    }

    // Construtor para carregar dados existentes do Banco/API
    public Usuario(int id, string username, string name, DateTime createdAt)
    {
        this.id = id;
        this.username = username;
        this.name = name;
        this.createdAt = createdAt;
    }
    
    private int GetXpToNextLevel()
    {
        return 100 + (level * 25);
    }

    public void AddXp(int amount)
    {
        if (level >= 99)
        {
            level = 99;
            xp = 0;
            return;
        }

        xp += amount;

        while (xp >= GetXpToNextLevel())   
        {
            xp -= GetXpToNextLevel();
            level++;

            if (level >= 99)
            {
                level = 99;
                xp = 0;
                break;
            }
        }
    }
}


        