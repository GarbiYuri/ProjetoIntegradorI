using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Models;
using Repository.Exceptions;

namespace Repository.Postgress;

// CRUD / CONSULTAS UNIFICADAS DE USUÁRIO
public class UsuarioDB
{
    // Método para listar TODOS os utilizadores do sistema (Alunos + Professores)
    public static async Task<List<Usuario>> ListarTodos()
    {
        DB.testConnection();
        var listaUnificada = new List<Usuario>();

        // 1. Procura e adiciona os Alunos
        try
        {
            var alunos = await AlunoDB.Listar();
            listaUnificada.AddRange(alunos);
        }
        catch (ResourceNotFoundException) { /* Ignora se não houver alunos */ }

        // 2. Procura e adiciona os Professores
        try
        {
            var professores = await ProfessorDB.Listar();
            listaUnificada.AddRange(professores);
        }
        catch (ResourceNotFoundException) { /* Ignora se não houver professores */ }

        if (listaUnificada.Count == 0)
        {
            throw new ResourceNotFoundException("Nenhum utilizador (Aluno ou Professor) cadastrado no sistema.");
        }

        return listaUnificada;
    }

    // Método polimórfico para obter qualquer utilizador por um identificador único
    // Como Aluno usa 'username' e Professor usa 'email', este método tenta encontrar em ambos
    public static async Task<Usuario> GetUserByIdentifier(string identificador)
    {
        DB.testConnection();

        // Se contiver '@', é muito provável que seja um Professor (Email)
        if (identificador.Contains("@"))
        {
            try
            {
                return await ProfessorDB.GetProfessorByEmail(identificador);
            }
            catch (ResourceNotFoundException)
            {
                throw new ResourceNotFoundException($"Nenhum professor encontrado com o email: {identificador}");
            }
        }
        else
        {
            // Caso contrário, tenta procurar na tabela de Alunos (Username)
            try
            {
                return await AlunoDB.GetAlunoByUsername(identificador);
            }
            catch (ResourceNotFoundException)
            {
                throw new ResourceNotFoundException($"Nenhum aluno encontrado com o username: {identificador}");
            }
        }
    }

    // Verifica se um utilizador existe no sistema (seja por username ou por email)
    public static async Task<bool> UserExists(string identificador)
    {
        DB.testConnection();

        if (identificador.Contains("@"))
        {
            return await ProfessorDB.ProfessorExists(identificador);
        }
        else
        {
            return await AlunoDB.AlunoExists(identificador);
        }
    }

    // Método opcional: Caso queira apagar um utilizador sabendo apenas o ID e o Tipo
    public static async Task Delete(int id, string tipoUsuario)
    {
        DB.testConnection();
        string tabela = tipoUsuario.ToLower() == "aluno" ? "alunos" : "professores";

        await using var cmd = DB.dataSource.CreateCommand($"DELETE FROM {tabela} WHERE id = $1");
        cmd.Parameters.AddWithValue(id);
        
        int linhasAfetadas = await cmd.ExecuteNonQueryAsync();
        if (linhasAfetadas == 0)
        {
            throw new ResourceNotFoundException($"Não foi possível eliminar: {tipoUsuario} com ID {id} não encontrado.");
        }
    }
}