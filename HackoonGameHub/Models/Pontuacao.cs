using System;

namespace Models;

/// <summary>
/// Uma pontuação conquistada por um aluno ao concluir uma partida de um jogo
/// do Hub. Cada linha é um "recibo" imutável da partida; os totais do aluno
/// ficam em <see cref="Aluno.points"/>, <see cref="Aluno.xp"/>,
/// <see cref="Aluno.level"/> e <see cref="Aluno.stars"/>.
/// </summary>
public class Pontuacao
{
    public int id { get; set; }

    public int id_aluno { get; set; }

    /// <summary>Id do jogo no catálogo (GameRegistry).</summary>
    public string jogo_key { get; set; } = string.Empty;

    public string jogo_nome { get; set; } = string.Empty;

    /// <summary>
    /// Identifica a partida/porta. A combinação (aluno, partida) é única, o
    /// que impede salvar a mesma partida duas vezes (duplo clique, reconexão,
    /// RPC repetido etc.).
    /// </summary>
    public string partida_id { get; set; } = string.Empty;

    public int? id_sala { get; set; }

    /// <summary>Pontos ganhos nesta partida.</summary>
    public int pontos { get; set; }

    /// <summary>Estrelas ganhas nesta partida (0 a 3).</summary>
    public int estrelas { get; set; }

    /// <summary>XP efetivamente creditado (pode ser menor no nível máximo).</summary>
    public int xp_ganho { get; set; }

    public int nivel_anterior { get; set; } = 1;

    public int nivel_novo { get; set; } = 1;

    /// <summary>Resumo livre (ex.: "Terra • 3 meteoros • 2 erros").</summary>
    public string? detalhes { get; set; }

    public DateTime createdAt { get; set; } = DateTime.Now;
}

/// <summary>
/// Resultado de uma tentativa de salvar uma pontuação. O jogo usa isso para
/// mostrar ao aluno quanto ganhou, se subiu de nível e se os pontos foram
/// realmente gravados.
/// </summary>
public class ResultadoPontuacao
{
    /// <summary>Falso quando a partida já havia sido salva (idempotência).</summary>
    public bool Salvo { get; set; }

    public int AlunoId { get; set; }

    public int PontosGanhos { get; set; }

    public int PontosTotais { get; set; }

    public int EstrelasGanhas { get; set; }

    public int EstrelasTotais { get; set; }

    public int NivelAnterior { get; set; } = 1;

    public int NivelNovo { get; set; } = 1;

    public int XpAtual { get; set; }

    public int XpProximoNivel { get; set; } = 1;

    public bool SubiuNivel => NivelNovo > NivelAnterior;

    public string Mensagem { get; set; } = string.Empty;

    public static ResultadoPontuacao NaoSalvo(string mensagem)
    {
        return new ResultadoPontuacao { Salvo = false, Mensagem = mensagem };
    }
}

/// <summary>Linha do ranking de pontos usado pelo professor/HUD.</summary>
public class RankingPontuacao
{
    public int AlunoId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Nick { get; set; } = string.Empty;
    public int Pontos { get; set; }
    public int Nivel { get; set; }
    public int Estrelas { get; set; }
}
