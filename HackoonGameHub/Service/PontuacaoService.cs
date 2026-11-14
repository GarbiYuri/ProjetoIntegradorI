using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Models;
using Repository.Exceptions;
using Repository.Postgress;

namespace Service;

/// <summary>
/// Regras de negócio do sistema de pontos do Hackoon Game Hub.
///
/// Fluxo completo de uma partida:
///   1. o jogo termina e chama <see cref="RegistrarPartidaAsync"/> com o
///      placar e o identificador da porta/partida;
///   2. o serviço valida os dados, aplica o limite por partida e calcula as
///      estrelas pelo desempenho (0 a 3);
///   3. o <see cref="PontuacaoDB"/> grava o histórico e atualiza os totais do
///      aluno em uma única transação;
///   4. o jogo recebe um <see cref="ResultadoPontuacao"/> para mostrar ao
///      aluno (+pontos, estrelas e possível subida de nível).
///
/// Regras importantes:
/// - convidados (alunoId = 0) não pontuam;
/// - sem banco conectado o jogo continua funcionando e simplesmente não salva;
/// - a mesma partida nunca pontua duas vezes (idempotência no banco);
/// - existe um teto por partida para evitar placares absurdos/abusos.
/// </summary>
public static class PontuacaoService
{
    /// <summary>Teto de segurança para uma única partida.</summary>
    public const int PontosMaximosPorPartida = 1000;

    /// <summary>
    /// Calcula as estrelas (0 a 3) comparando o placar com o máximo possível
    /// daquela partida. A regra é a mesma para todos os jogos do Hub.
    /// </summary>
    public static int CalcularEstrelas(int pontos, int pontosMaximos)
    {
        if (pontos <= 0 || pontosMaximos <= 0)
        {
            return 0;
        }

        double proporcao = Math.Min(pontos, pontosMaximos) / (double)pontosMaximos;

        if (proporcao >= 0.85)
        {
            return 3;
        }

        if (proporcao >= 0.55)
        {
            return 2;
        }

        return 1;
    }

    /// <summary>
    /// Registra a pontuação de uma partida e aplica as regras de progressão.
    /// Devolve <c>null</c> quando o jogador é convidado, quando o banco não
    /// está conectado ou quando os dados são inválidos.
    /// </summary>
    public static async Task<ResultadoPontuacao?> RegistrarPartidaAsync(
        int alunoId,
        string jogoKey,
        string jogoNome,
        string partidaId,
        int pontos,
        int pontosMaximos = 100,
        int? estrelas = null,
        int? salaId = null,
        string? detalhes = null)
    {
        // Convidado: o jogo roda, mas não há conta para salvar.
        if (alunoId <= 0)
        {
            return null;
        }

        // Sem banco (modo offline/solo) o jogo não deve travar.
        if (!DB.conectado)
        {
            Console.WriteLine("[PONTOS] Banco desconectado: pontuação não será salva.");
            return null;
        }

        jogoKey = Sanitizar(jogoKey, 64, "jogo");
        jogoNome = Sanitizar(jogoNome, 255, jogoKey);
        partidaId = Sanitizar(partidaId, 120, $"{jogoKey}:{Guid.NewGuid():N}");

        pontos = Math.Clamp(pontos, 0, PontosMaximosPorPartida);
        pontosMaximos = Math.Clamp(pontosMaximos, 1, 10_000);

        int estrelasCalculadas = estrelas ?? CalcularEstrelas(pontos, pontosMaximos);
        estrelasCalculadas = pontos <= 0 ? 0 : Math.Clamp(estrelasCalculadas, 0, 3);

        detalhes = string.IsNullOrWhiteSpace(detalhes)
            ? null
            : detalhes.Trim()[..Math.Min(detalhes.Trim().Length, 500)];

        try
        {
            // Valida a existência do aluno antes de abrir a transação. A
            // transação do repositório relê a linha com FOR UPDATE.
            await AlunoDB.GetAlunoById(alunoId);

            var registro = new Pontuacao
            {
                id_aluno = alunoId,
                jogo_key = jogoKey,
                jogo_nome = jogoNome,
                partida_id = partidaId,
                id_sala = salaId.HasValue && salaId.Value > 0 ? salaId : null,
                pontos = pontos,
                estrelas = estrelasCalculadas,
                detalhes = detalhes
            };

            ResultadoPontuacao resultado = await PontuacaoDB.RegistrarPartida(registro);
            return resultado;
        }
        catch (ResourceNotFoundException e)
        {
            Console.WriteLine("[PONTOS] " + e.Message);
            return null;
        }
        catch (Exception e)
        {
            Console.WriteLine("[PONTOS] Falha ao salvar pontuação: " + e.Message);
            return null;
        }
    }

    /// <summary>Lê novamente o aluno para atualizar o HUD após a partida.</summary>
    public static async Task<Aluno?> ObterProgressoAsync(int alunoId)
    {
        if (alunoId <= 0 || !DB.conectado)
        {
            return null;
        }

        try
        {
            return await AlunoDB.GetAlunoById(alunoId);
        }
        catch (Exception e)
        {
            Console.WriteLine("[PONTOS] Falha ao carregar o progresso: " + e.Message);
            return null;
        }
    }

    /// <summary>Histórico de partidas do aluno.</summary>
    public static async Task<List<Pontuacao>> ObterHistoricoAsync(int alunoId, int limite = 30)
    {
        if (alunoId <= 0 || !DB.conectado)
        {
            return new List<Pontuacao>();
        }

        try
        {
            return await PontuacaoDB.ListarPorAluno(alunoId, limite);
        }
        catch (Exception e)
        {
            Console.WriteLine("[PONTOS] Falha ao carregar o histórico: " + e.Message);
            return new List<Pontuacao>();
        }
    }

    /// <summary>Ranking da sala (ou geral quando idSala não é informado).</summary>
    public static async Task<List<RankingPontuacao>> ObterRankingAsync(int? idSala, int limite = 10)
    {
        if (!DB.conectado)
        {
            return new List<RankingPontuacao>();
        }

        try
        {
            return await PontuacaoDB.Ranking(idSala, limite);
        }
        catch (Exception e)
        {
            Console.WriteLine("[PONTOS] Falha ao carregar o ranking: " + e.Message);
            return new List<RankingPontuacao>();
        }
    }

    private static string Sanitizar(string? valor, int tamanho, string padrao)
    {
        string texto = string.IsNullOrWhiteSpace(valor) ? padrao : valor.Trim();
        return texto.Length <= tamanho ? texto : texto[..tamanho];
    }
}
