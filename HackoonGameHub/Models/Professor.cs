using System;

namespace Models;

public class Professor : Usuario
{
    public string email { get; set; }

    // Construtor para criação de novo Professor
    public Professor(string name, string email) : base(name)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new Exception("Email obrigatório");
        }
        this.email = email.Trim();
    }

    // Construtor para carregar Professor do Banco de Dados
    public Professor(int id, string name, string email, DateTime createdAt) : base(id, name, createdAt)
    {
        this.email = email;
    }
}