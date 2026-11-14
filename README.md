# ProjetoIntegradorI — Hackoon Game Hub

Jogo educativo top-down desenvolvido para o Projeto Integrador I do curso de
Análise e Desenvolvimento de Sistemas (FATEC Mogi Mirim).

O projeto é dividido em:

- **HackoonGameHub/Game** — cliente do jogo em **Godot 4.6 + C#**.
- **HackoonGameHub/Models, Repository, Service, Exceptions** — bibliotecas C#
  de domínio, acesso ao PostgreSQL, segurança e descoberta do servidor na rede.
- **HackoonGameHub/pgsql** — PostgreSQL portátil embutido, separado por SO:
  `pgsql/windows` (binários do Windows), `pgsql/linux` (binários do Linux) e
  `pgsql/macos` (quando necessário). O build copia apenas a pasta do SO atual.
- **Documentation** — manuscrito, artigos e documentação do projeto.
- **Diario** — diário de bordo do Projeto Integrador.
- **sql** — script de criação das tabelas.

## Fluxo atual

1. **Professor**: menu inicial → **SALA DO PROFESSOR** → login/cadastro →
   lista de salas → escolhe uma sala → **entra direto no lobby**, com o
   **painel do professor** (dashboard) aberto. A sessão já fica **anunciada na
   rede** nesse momento, então os alunos podem entrar a qualquer momento —
   não existe mais uma tela de espera que precise ser aberta para o lobby
   aparecer.
2. **Aluno**: menu inicial → **ENTRAR COMO ALUNO** → vê a sessão aberta pelo
   professor (nome da sala, professor e IP vindos do broadcast UDP) e a
   **lista de alunos que já estão na sala** (atualizada ao vivo) → **tela de
   conta** (cria a conta ou acessa, escolhe a cor do personagem) → **JOGAR**
   entra direto no lobby como cliente. Quem não quer criar conta usa
   **JOGAR SEM CONTA** e entra como convidado (sem banco).
3. **Lobby**: cena top-down com os personagens dos alunos sincronizados via
   ENet (porta `7777`), cada um com a cor escolhida na tela de conta. A visão é
   de **mapa inteiro** (sem o zoom do personagem) e o HUD é compacto: sala,
   status, contador de jogadores e o botão **🎮 JOGOS**. O professor **não
   aparece como personagem**: ele fica com o **painel de gerenciamento**, que
   lista os alunos conectados e as **portas de jogo abertas** (com quantidade
   de jogadores e placar ao vivo), abre jogos do catálogo e permite
   **INICIAR JOGO** e **ENCERRAR CONEXÃO**.
4. **Ciclo de jogos**: todo mundo pode jogar. O jogador abre o painel
   **🎮 JOGOS**, escolhe um jogo do **catálogo** e clica em **ABRIR PORTA** —
   isso cria uma porta ENet própria da partida (porta `7778`+) e abre o jogo
   em tela cheia sobre o lobby. Os colegas veem a porta na lista e entram com
   **ENTRAR**; o dono vê o placar ao vivo e o professor acompanha tudo pelo
   painel. Ao fechar (botão **FECHAR JOGO** ou `Esc`), todos voltam ao lobby.
5. **Adicionar um jogo novo** é uma linha no catálogo
   (`Game/Scripts/Games/GameRegistry.cs`) apontando para a cena. O jogo só
   precisa herdar de `MinigameBase`; o lobby, o painel do professor e as portas
   passam a mostrá-lo automaticamente. Veja
   [`Documentation/Games.md`](Documentation/Games.md).

## Como executar

1. Abra a pasta `HackoonGameHub/Game` no **Godot 4.6 Mono**.
2. Compile o C# (`Build` no Godot ou `dotnet build HackoonGameHub.sln` na pasta
   `HackoonGameHub`).
3. Execute a cena principal (`Scenes/InitialScene.tscn`).

> O banco PostgreSQL embutido é iniciado automaticamente pela máquina do
> professor na primeira execução (o app cria e sobe o servidor portátil). Se a
> porta `5432` já estiver ocupada (ex.: um Docker de teste), o app escolhe
> automaticamente a primeira porta livre (5433, 5434...) e anuncia a porta
> certa no broadcast, então o aluno conecta sem configurar nada. Para forçar
> uma porta específica, defina a variável de ambiente `HACKOON_PG_PORT`
> (ex.: `5433`). O app só encerra o banco que ele mesmo iniciou/adotou;
> Docker/serviços externos não são afetados.

### Testar o lobby sem rede/banco

Abra `Scenes/Personagem/LobbyPrincipal.tscn` e pressione **F6**, ou use:

```bash
godot --path HackoonGameHub/Game \
  res://Scenes/Personagem/LobbyPrincipal.tscn -- --lobby-solo
```

Isso instancia o personagem padrão em modo solo, ideal para testar a arte e a
movimentação antes de estilizar.

### Testar host e cliente na mesma máquina

Em dois terminais diferentes:

```bash
# Terminal 1 (host)
godot --path HackoonGameHub/Game \
  res://Scenes/Personagem/LobbyPrincipal.tscn -- --lobby-host

# Terminal 2 (cliente)
godot --path HackoonGameHub/Game \
  res://Scenes/Personagem/LobbyPrincipal.tscn -- --lobby-join=127.0.0.1
```

### Testar o catálogo e o ciclo de jogos

O atalho `--lobby-jogos` abre o painel **🎮 JOGOS** do aluno e
`--lobby-criar-jogo` abre a primeira porta do catálogo:

```bash
# Abre o lobby sozinho e o catálogo
godot --path HackoonGameHub/Game \
  res://Scenes/Personagem/LobbyPrincipal.tscn -- --lobby-solo --lobby-jogos

# Abre o lobby sozinho e já abre uma porta do jogo
godot --path HackoonGameHub/Game \
  res://Scenes/Personagem/LobbyPrincipal.tscn -- --lobby-solo --lobby-criar-jogo=2
```

### Testar o botão INICIAR JOGO

O atalho `--lobby-iniciar` dispara o mesmo botão do painel do professor
(fecha o painel e leva todos para o lobby, de onde os jogos são abertos pelo
catálogo). Use `--lobby-iniciar=3` para esperar 3 segundos antes de disparar:

```bash
godot --path HackoonGameHub/Game \
  res://Scenes/Personagem/LobbyPrincipal.tscn -- --lobby-host --lobby-iniciar=3
```

### Testar uma partida entre dois alunos (porta de jogo)

Em três terminais:

```bash
# Terminal 1 (host/professor)
godot --path HackoonGameHub/Game \
  res://Scenes/Personagem/LobbyPrincipal.tscn -- --lobby-host

# Terminal 2 (aluno que abre a porta)
godot --path HackoonGameHub/Game \
  res://Scenes/Personagem/LobbyPrincipal.tscn -- \
  --lobby-join=127.0.0.1 --lobby-criar-jogo=3

# Terminal 3 (outro aluno) — entra na partida aberta
godot --path HackoonGameHub/Game \
  res://Scenes/Personagem/LobbyPrincipal.tscn -- \
  --lobby-join=127.0.0.1 --lobby-entrar-jogo=8
```

## Personagem padrão (placeholder)

O personagem fica em `Scenes/Personagem/Personagem.tscn`.

- A parte visual está isolada no nó **Sprite** (`AnimatedSprite2D`).
- As animações existentes são `idleDown`, `down`, `left`, `right`, `up` e as
  diagonais (`downLeft`, `downRight`, `topLeft`, `topRight`).
- Para estilizar depois, basta substituir o `SpriteFrames` do nó **Sprite** no
  editor. A movimentação, a câmera, o nome e a sincronização continuam
  funcionando sem alterar o código.
- A sombra é o nó `Shadow` e o nome é o `NameLabel`, ambos separados para
  facilitar a troca de arte.

## Documentação

- [`Documentation/Lobby.md`](Documentation/Lobby.md) — detalhamento técnico do
  lobby, do ciclo fechado e do que foi corrigido.
- [`Documentation/Games.md`](Documentation/Games.md) — contrato dos minigames e
  passo a passo para adicionar um jogo novo ao catálogo.
