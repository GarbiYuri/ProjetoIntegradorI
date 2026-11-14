using System;
using System.Linq;

namespace Models;

public class Aluno : Usuario
{
    public string? username { get; set; }
    public int? id_sala { get; set; }
    public string? turma { get; set; }
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

    /// <summary>XP necessário para passar do nível atual para o próximo.</summary>
    public int XpParaProximoNivel()
    {
        return 100 + (level * 25); //Balanceamento de XP com base no LVL
    }

    public void AddXp(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        if (level >= 99)
        {
            level = 99;
            xp = 0;
            return;
        }

        xp += amount;

        while (xp >= XpParaProximoNivel()) //Metodo cap de XP
        {
            xp -= XpParaProximoNivel();
            level++;

            if (level >= 99)
            {
                level = 99;
                xp = 0;
                break;
            }
        }
    }

    /// <summary>
    /// Acumula os pontos conquistados na missão (placar) e dispara a regra de
    /// negócio que converte XP em nível.
    /// </summary>
    public void AdicionarPontos(int pontos)
    {
        RegistrarRecompensa(pontos, 0);
    }

    /// <summary>
    /// Aplica a recompensa de uma partida (pontos + estrelas) e devolve um
    /// retrato do antes/depois. É a regra de domínio compartilhada pelo
    /// <c>PontuacaoService</c> e pelo repositório transacional.
    /// </summary>
    public ResultadoPontuacao RegistrarRecompensa(int pontos, int estrelas)
    {
        var resultado = new ResultadoPontuacao
        {
            AlunoId = id,
            PontosTotais = points,
            EstrelasTotais = stars,
            NivelAnterior = level,
            NivelNovo = level,
            XpAtual = xp,
            XpProximoNivel = XpParaProximoNivel()
        };

        pontos = Math.Max(0, pontos);
        estrelas = Math.Clamp(estrelas, 0, 3);

        if (pontos > 0)
        {
            points += pontos;
            AddXp(pontos);
        }

        if (estrelas > 0)
        {
            stars += estrelas;
        }

        resultado.Salvo = true;
        resultado.PontosGanhos = pontos;
        resultado.EstrelasGanhas = estrelas;
        resultado.PontosTotais = points;
        resultado.EstrelasTotais = stars;
        resultado.NivelNovo = level;
        resultado.XpAtual = xp;
        resultado.XpProximoNivel = XpParaProximoNivel();

        if (resultado.SubiuNivel)
        {
            resultado.Mensagem = $"Você subiu para o nível {level}!";
        }

        return resultado;
    }
}