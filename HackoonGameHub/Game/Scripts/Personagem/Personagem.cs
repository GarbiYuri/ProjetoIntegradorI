using Godot;
using Game.Scripts;

/// <summary>
/// Personagem padrão (placeholder) usado no lobby.
///
/// A parte visual está toda no nó "Sprite" (AnimatedSprite2D). Para trocar a arte
/// depois basta substituir o SpriteFrames no editor, sem mexer nesta lógica.
/// </summary>
public partial class Personagem : Node2D
{
	private const float Speed = 150.0f;

	/// <summary>Área em que o personagem pode andar (definida pelo Lobby).</summary>
	[Export] public Rect2 MovementBounds { get; set; } = new Rect2(-560, -300, 1120, 600);

	/// <summary>
	/// Falso quando o personagem é usado só como preview (ex.: tela de conta):
	/// sem movimento e sem câmera.
	/// </summary>
	[Export] public bool Controllable { get; set; } = true;

	[Export] public Vector2 SyncDirection { get; set; } = Vector2.Zero;
	[Export] public string SyncAnimation { get; set; } = "idleDown";
	[Export] public string PlayerName { get; set; } = "Jogador";

	/// <summary>Nick escolhido pelo aluno (aparece na lista da sala).</summary>
	[Export] public string PlayerNick { get; set; } = "";

	/// <summary>
	/// Cor do personagem (escolhida na tela de conta). É sincronizada pelo
	/// MultiplayerSynchronizer, então a cor aparece para todos na rede.
	/// </summary>
	[Export] public Color SyncColor { get; set; } = Colors.White;

	private AnimatedSprite2D _sprite;
	private Label _nameLabel;
	private Camera2D _camera;
	private SessionManager _session;
	private ShaderMaterial _tintMaterial;
	private Color _corAplicada = new Color(-1f, -1f, -1f, -1f);

	public override void _Ready()
	{
		_sprite = GetNode<AnimatedSprite2D>("Sprite");
		_nameLabel = GetNode<Label>("NameLabel");
		_camera = GetNode<Camera2D>("Camera2D");
		_session = GetNodeOrNull<SessionManager>("/root/SessionManager");
		_tintMaterial = _sprite.Material as ShaderMaterial;

		// Só a máquina dona do personagem liga a câmera. Assim várias câmeras
		// não brigam pela tela quando há mais de um jogador conectado.
		// O preview (Controllable = false) nunca tem câmera nem movimento.
		_camera.Enabled = IsMultiplayerAuthority() && Controllable;

		if (IsMultiplayerAuthority() && _session != null)
		{
			if (!string.IsNullOrWhiteSpace(_session.playerName))
			{
				PlayerName = _session.playerName;
			}
			else if (_session.usuario != null)
			{
				PlayerName = _session.usuario.name;
			}

			if (Controllable)
			{
				// Cor escolhida na tela de conta (fica na sessão local e
				// replica para os outros via SyncColor).
				SyncColor = _session.GetPersonagemColor();
				PlayerNick = _session.playerNick ?? string.Empty;
			}
		}

		PlayAnimation(SyncAnimation);
		UpdateNameLabel();
		AplicarCor();

		GD.Print($"[LOBBY] Personagem '{Name}' | id local: {Multiplayer.GetUniqueId()} | autoridade: {IsMultiplayerAuthority()}");
	}

	public override void _PhysicsProcess(double delta)
	{
		// A posição dos outros jogadores chega pelo MultiplayerSynchronizer.
		// Portanto, só movemos o personagem quando somos a autoridade dele.
		if (IsMultiplayerAuthority() && Controllable)
		{
			Vector2 direction = ReadMovementInput();
			SyncDirection = direction;

			if (direction != Vector2.Zero)
			{
				SyncAnimation = GetWalkAnimation(direction);
				GlobalPosition += direction * Speed * (float)delta;

				GlobalPosition = new Vector2(
					Mathf.Clamp(GlobalPosition.X, MovementBounds.Position.X, MovementBounds.End.X),
					Mathf.Clamp(GlobalPosition.Y, MovementBounds.Position.Y, MovementBounds.End.Y));
			}
			else
			{
				SyncAnimation = GetIdleAnimation(SyncAnimation);
			}
		}

		PlayAnimation(SyncAnimation);
		UpdateNameLabel();
		AplicarCor();
	}

	/// <summary>Aplica a cor escolhida no shader (só quando ela muda).</summary>
	private void AplicarCor()
	{
		if (_tintMaterial == null || _corAplicada == SyncColor)
		{
			return;
		}

		_tintMaterial.SetShaderParameter("tint_color", SyncColor);
		_corAplicada = SyncColor;
	}

	private static Vector2 ReadMovementInput()
	{
		// Setas (ações padrão do Godot) + WASD.
		Vector2 direction = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");

		if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) direction.X -= 1.0f;
		if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) direction.X += 1.0f;
		if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) direction.Y -= 1.0f;
		if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) direction.Y += 1.0f;

		// Evita andar mais rápido na diagonal.
		return direction.LimitLength(1.0f);
	}

	private static string GetWalkAnimation(Vector2 direction)
	{
		if (direction.X < 0 && direction.Y > 0) return "downLeft";
		if (direction.X > 0 && direction.Y > 0) return "downRight";
		if (direction.X < 0 && direction.Y < 0) return "topLeft";
		if (direction.X > 0 && direction.Y < 0) return "topRight";

		if (direction.X < 0) return "left";
		if (direction.X > 0) return "right";
		if (direction.Y < 0) return "up";
		return "down";
	}

	private static string GetIdleAnimation(string walkAnimation)
	{
		return walkAnimation switch
		{
			"down" => "idleDown",
			"left" => "idleLeft",
			"right" => "idleRight",
			"up" => "idleUp",
			"downLeft" => "idleDownLeft",
			"downRight" => "idleDownRight",
			"topLeft" => "idleTopLeft",
			"topRight" => "idleTopRight",
			_ => "idleDown"
		};
	}

	private void PlayAnimation(string animation)
	{
		if (_sprite == null || string.IsNullOrEmpty(animation)) return;

		if (!_sprite.SpriteFrames.HasAnimation(animation))
		{
			animation = "idleDown";
		}

		if (_sprite.Animation != animation)
		{
			_sprite.Play(animation);
		}
	}

	private void UpdateNameLabel()
	{
		if (_nameLabel == null) return;

		string displayName = string.IsNullOrWhiteSpace(PlayerName) ? "Jogador" : PlayerName;
		if (_nameLabel.Text != displayName)
		{
			_nameLabel.Text = displayName;
		}
	}
}
