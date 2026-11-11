using System;

namespace Models;

/// <summary>
/// Aluno que está conectado ao lobby agora (usado na lista que o professor
/// anuncia e que os alunos enxergam na tela de entrada).
/// </summary>
public class AlunoOnline
{
    public string Nome { get; set; } = string.Empty;
    public string Nick { get; set; } = string.Empty;

    public AlunoOnline()
    {
    }

    public AlunoOnline(string nome, string nick)
    {
        Nome = nome;
        Nick = nick;
    }
}
