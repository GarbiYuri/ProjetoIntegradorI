using Godot;
using System;
using Repository.Postgress;

public partial class BancoButton : Button
{
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public override async void _Pressed()
	{
		var loadingIcon = GetNode<AnimatedSprite2D>("loadingIcon");

		Disabled = true;
		loadingIcon.Visible = true;
		loadingIcon.Play("loading", 1,true);
		Text = "Carregando...";

		try
		{
			await DB.Connect();
			Text = "Conectado";
			loadingIcon.Visible = false;
			GetTree().ChangeSceneToFile("res://TeacherScene/Salas/Salas.tscn");
		}catch(Exception e)
		{
			GD.PrintErr(e.Message);
			Text = "Tentar Novamente";
			Disabled = false;
			loadingIcon.Visible = false;
		}
	}
}
