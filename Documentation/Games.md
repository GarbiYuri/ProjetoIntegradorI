# Jogos do Hackoon Game Hub — catálogo, portas e contrato

Este documento explica como o Hub abre, executa e organiza os jogos e mostra o
passo a passo para **adicionar um jogo novo** sem mexer na lógica do lobby.

## 1. Visão geral do ciclo

```
Aluno/Professor
   │  abre o painel 🎮 JOGOS e escolhe um cartão do catálogo
   ▼
LobbyPrincipal.AbrirJogoDoCatalogo(GameInfo)
   │  1. cria um servidor ENet da partida (porta 7778+)
   │  2. aplica uma API de multiplayer própria em GameSessionRoot
   │  3. instancia a cena do jogo (que herda de MinigameBase)
   ▼
GameOverlay (tela cheia sobre o lobby)
   │  entrega o GameContexto ao jogo
   ▼
Jogo roda sozinho ou com os colegas da mesma porta
   │  RPCs usam a API da partida (o lobby continua na conexão 7777)
   ▼
FECHAR JOGO / Esc → volta ao lobby e libera a porta
```

Peças principais:

| Peça | Onde fica |
| --- | --- |
| Catálogo | `Game/Scripts/Games/GameRegistry.cs` |
| Metadados | `Game/Scripts/Games/GameInfo.cs` |
| Contrato do jogo | `Game/Scripts/Games/MinigameBase.cs` |
| Contexto entregue ao jogo | `Game/Scripts/Games/GameContexto.cs` |
| Overlay / raiz da partida | `Game/Scripts/Games/GameOverlay.cs` e `Game/Scenes/Games/GameOverlay.tscn` |
| Porta e ciclo no lobby | `Game/Scripts/Personagem/LobbyPrincipal.cs` |
| Jogo de referência | `Game/Games/RegrasDeConvivencia/` |

## 2. Contrato de um minigame

A raiz da cena do jogo **precisa** ser um `Control` com um script que herde de
`MinigameBase`:

```csharp
using Godot;
using Game.Scripts.Games;

namespace Game.Games;

public partial class MeuJogo : MinigameBase
{
    public override string GameId => "meu-jogo";

    public override void Iniciar(GameContexto contexto)
    {
        base.Iniciar(contexto);

        // Monta a interface, lê contexto.Jogador/Sala.
        // Se contexto.SouHost, o jogador é o dono da porta.
        // Se não, conecte-se aos RPCs da porta (a API já está aplicada).
    }

    public override void Encerrar()
    {
        // Para timers e cancela inscrições de eventos de rede.
        base.Encerrar();
    }
}
```

`MinigameBase` oferece:

| Membro | Para que serve |
| --- | --- |
| `GameId` | Identificador estável; deve bater com o `GameInfo.Id` |
| `Contexto` | Porta, host, jogador, nick, sala e `SalaId` |
| `Iniciar(contexto)` | Chamado pelo overlay depois de pendurar a cena |
| `Encerrar()` | Chamado antes de fechar/liberar a porta |
| `SouServidor` | `true` no dono da porta |
| `NomeLocal` | Nome de exibição do jogador local |
| `AvisarStatus(texto)` | Publica uma mensagem no HUD do lobby |
| `AvisarPlacar(texto)` | Publica o **placar parcial** no painel do professor |
| `AvisarFim(texto)` | Publica o **resumo final** no painel do professor |
| `PedirSaida()` | Pede ao overlay para fechar o jogo |

> O jogo **não conhece o lobby**. Ele recebe tudo pelo `GameContexto` e usa os
> eventos acima para conversar com o Hub.

### 2.1 Rede da partida

- O lobby roda em `7777`. Cada porta de jogo abre um ENet próprio em `7778`,
  `7779`... e a API de multiplayer é aplicada **somente** em
  `GameSessionRoot` (o nó onde o jogo é pendurado).
- Por isso os RPCs do jogo funcionam normalmente (`[Rpc]`,
  `Multiplayer.GetUniqueId()`, `RpcId`, etc.) sem interferir na conexão do
  lobby.
- Use `CallLocal = true` quando o dono da porta também precisa executar o
  mesmo método (ex.: iniciar a partida no host e replicar para os clientes).
- O dono da porta é o servidor da partida e deve validar/responder os pedidos
  dos clientes.

## 3. Adicionar um jogo novo

### Passo 1 — criar a cena

Crie a pasta e a cena, por exemplo:

```
Game/Games/MeuJogo/MeuJogo.tscn
Game/Games/MeuJogo/MeuJogo.cs
```

A cena deve ser um `Control` em tela cheia com o script na raiz:

```
[gd_scene load_steps=2 format=3]

[ext_resource type="Script" path="res://Games/MeuJogo/MeuJogo.cs" id="1"]

[node name="MeuJogo" type="Control"]
layout_mode = 3
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
grow_horizontal = 2
grow_vertical = 2
script = ExtResource("1")
```

### Passo 2 — registrar no catálogo

Abra `Game/Scripts/Games/GameRegistry.cs` e acrescente um `GameInfo`:

```csharp
new GameInfo(
    id: "meu-jogo",
    nome: "Meu Jogo",
    descricao: "Uma frase curta que aparece no cartão.",
    cena: "res://Games/MeuJogo/MeuJogo.tscn",
    categoria: "Raciocínio",
    minJogadores: 1,
    maxJogadores: 8,
    cor: new Color(0.42f, 0.69f, 0.95f),
    emblema: "★"),
```

Pronto. O jogo passa a aparecer automaticamente:

- no **catálogo do aluno** (painel 🎮 JOGOS);
- no **painel do professor** (JOGOS DISPONÍVEIS NO HUB);
- nas **portas abertas** (nome, categoria, jogadores e placar).

Se a cena não existir, o botão do cartão fica desabilitado e o lobby avisa no
log — os outros jogos continuam funcionando.

### Passo 3 — conteúdo e imagens

- Coloque imagens/áudio dentro da pasta do jogo (`Game/Games/MeuJogo/...`) e
  referencie por `res://`.
- Rode o Godot uma vez no editor ou use
  `godot --headless --path Game --editor --quit` para gerar os arquivos
  `.import` das imagens novas.
- Commite os `.import` junto com as imagens (o `.godot/` é cache local).

### Passo 4 — testar

```bash
# A cena sozinha (não deve depender do lobby para abrir)
godot --headless --path Game res://Games/MeuJogo/MeuJogo.tscn

# Lobby solo com o catálogo aberto
godot --path Game res://Scenes/Personagem/LobbyPrincipal.tscn -- \
  --lobby-solo --lobby-jogos

# Lobby solo abrindo a primeira porta do catálogo
godot --path Game res://Scenes/Personagem/LobbyPrincipal.tscn -- \
  --lobby-solo --lobby-criar-jogo=2
```

Para testar com mais jogadores, use host + clientes:

```bash
# Terminal 1
godot --path Game res://Scenes/Personagem/LobbyPrincipal.tscn -- --lobby-host

# Terminal 2 (abre a porta)
godot --path Game res://Scenes/Personagem/LobbyPrincipal.tscn -- \
  --lobby-join=127.0.0.1 --lobby-criar-jogo=3

# Terminal 3 (entra na partida)
godot --path Game res://Scenes/Personagem/LobbyPrincipal.tscn -- \
  --lobby-join=127.0.0.1 --lobby-entrar-jogo=8
```

## 4. O jogo de referência: Regras de Convivência

`Game/Games/RegrasDeConvivencia/` é o porte do jogo original para o ciclo do
Hub. Ele mantém o conteúdo:

- 10 atitudes (5 boas e 5 ruins), 5 sorteadas por partida;
- escolha **ATITUDE BOA** / **ATITUDE RUIM**;
- feedback da resposta certa;
- placar por jogador e ranking final.

E acrescenta o que o Hub precisa:

- roda em uma porta própria (`7778+`), com uma API de multiplayer isolada;
- sincroniza jogadores, respostas e placar entre os participantes;
- publica placar ao vivo e resumo final para o painel do professor;
- permite reiniciar a partida (dono inicia; convidados pedem).

O pacote original pode ser mantido em `Game/RegrasDeConvivencia-main/` apenas
como referência; nesse caso ele fica fora da varredura do Godot (`.gdignore`).

## 5. Regras e portas

| Porta | Uso |
| --- | --- |
| `7777` (ENet) | Lobby (posições, nomes, lista de portas, RPCs do lobby) |
| `7778`, `7779`, ... (ENet) | Uma partida por porta de jogo |
| `11000` (UDP) | Descoberta do professor |
| `11001` (UDP) | Anúncio do professor para os alunos |
| `5432`/`5433` (TCP) | PostgreSQL embutido do professor |

- Cada jogador pode abrir **uma porta por vez**; abrir outra substitui a
  anterior.
- Quando o dono fecha o jogo (ou sai do lobby), a porta é encerrada e todos os
  participantes voltam ao lobby.
- O professor pode encerrar qualquer porta pelo painel.
- Convidados sem conta podem abrir e entrar em portas normalmente.

## 6. Solução de problemas

| Sintoma | Causa provável | Solução |
| --- | --- | --- |
| Cartão do jogo desabilitado | Cena não encontrada no caminho informado | Confira o `cena:` no `GameRegistry` e se o arquivo existe |
| Imagem em branco no jogo | Imagem nova ainda não importada | Rode o Godot uma vez (editor/import) e commite os `.import` |
| Cliente não recebe o estado | RPC fora da subárvore da porta | Use a API da partida (o jogo já está sob `GameSessionRoot`) |
| Todos voltam ao lobby ao fechar | Comportamento esperado do dono da porta | Para sair só da partida, use **FECHAR JOGO** em um cliente |
| `O banco de Dados não foi Inicializado...` no log | Sem PostgreSQL no teste | O lobby e as portas continuam funcionando em memória |
