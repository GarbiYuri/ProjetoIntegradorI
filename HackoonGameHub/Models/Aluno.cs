using System;
using System.Linq;

namespace Models;

public class Aluno : Usuario
{
    public string username { get; set; }
    public int? id_sala { get; set; }
    public string turma { get; set; }
    public int points { get; set; } = 0;
    public int level { get; set; } = 1;
    public int xp { get; set; } = 0;
    public int stars { get; set; } = 0;

    // Construtor para criação de novo Aluno
    public Aluno(string username, string name) : base(name)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new Exception("Nick obrigatório");
        }

        if (username.Length < 3)
        {
            throw new Exception("Nick muito curto");
        }

        if (username.Length > 12)
        {
            throw new Exception("Nick muito longo");
        }

        if (!username.All(c => char.IsLetterOrDigit(c) || c == ' '))
        {
            throw new Exception("Nick inválido");
        }

        this.username = char.ToUpper(username.Trim()[0]) + username.Trim().Substring(1).ToLower();
    }

    // Construtor para carregar Aluno do Banco de Dados
    public Aluno(int id, string username, string name, DateTime createdAt) : base(id, name, createdAt)
    {
        this.username = username;
    }

    private int GetXpToNextLevel()
    {
        return 100 + (level * 25); //Balanceamento de XP com base no LVL
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

        while (xp >= GetXpToNextLevel()) //Metodo cap de XP
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