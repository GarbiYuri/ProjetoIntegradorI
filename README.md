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
   entra direto no lobby como cliente (o lobby já está aberto, então entra na
   hora).
3. **Lobby**: cena top-down com os personagens dos alunos sincronizados via
   ENet (porta `7777`), cada um com a cor escolhida na tela de conta. O
   professor **não aparece como personagem**: ele fica com o **painel de
   gerenciamento**, que lista os alunos conectados, os jogos criados pelos
   alunos, e permite **INICIAR JOGO** e **ENCERRAR CONEXÃO** (fecha a sessão
   para todos).
4. **INICIAR JOGO**: fecha o painel e todos (professor e alunos) ficam no
   lobby — que é onde os minigames vão entrar (nó `World/Jogos`). O professor
   pode voltar ao painel a qualquer momento pelo botão **PAINEL** do HUD.
5. **Jogo online (conexão interna)**: **só o aluno cria jogo** — ele não cria
   sala (lobby), isso é do professor. Dentro do lobby, o aluno abre uma
   **conexão interna** (outro servidor ENet, porta `7778`+) com o botão
   **CRIAR** do painel **JOGOS ONLINE**; os colegas da sala veem o jogo na
   lista e entram com **ENTRAR**. O professor acompanha e pode **ENCERRAR**
   esses jogos pelo painel dele.

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

### Testar o botão INICIAR JOGO

O atalho `--lobby-iniciar` dispara o mesmo botão do painel do professor
(o RPC que fecha o painel e leva todos para o lobby). Use `--lobby-iniciar=3`
para esperar 3 segundos antes de disparar:

```bash
godot --path HackoonGameHub/Game \
  res://Scenes/Personagem/LobbyPrincipal.tscn -- --lobby-host --lobby-iniciar=3
```

### Testar o jogo online criado pelo aluno (conexão interna)

Em dois terminais:

```bash
# Terminal 1 (host) — e um cliente que cria o jogo
godot --path HackoonGameHub/Game \
  res://Scenes/Personagem/LobbyPrincipal.tscn -- --lobby-host

godot --path HackoonGameHub/Game \
  res://Scenes/Personagem/LobbyPrincipal.tscn -- \
  --lobby-join=127.0.0.1 --lobby-criar-jogo=3

# Terminal 3 (outro aluno) — entra no jogo criado
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

## Documentação do lobby

Consulte [`Documentation/Lobby.md`](Documentation/Lobby.md) para o detalhamento
técnico do ciclo fechado, o que foi corrigido e as próximas etapas.
