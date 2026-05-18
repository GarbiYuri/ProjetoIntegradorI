using System;

namespace Models;

public abstract class Usuario
{
    public int id { get; set; }
    public string name { get; set; }
    public string password { get; set; }
    public DateTime createdAt { get; set; }

    // Construtor para criação de novos usuários
    public Usuario(string name)
    {
        this.name = name.Trim();
        createdAt = DateTime.Now;
    }

    // Construtor para carregar do Banco de Dados
    public Usuario(int id, string name, DateTime createdAt)
    {
        this.id = id;
        this.name = name;
        this.createdAt = createdAt;
    }
}