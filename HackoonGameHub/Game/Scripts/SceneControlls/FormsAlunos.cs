using Godot;
using System;
using System.Collections.Generic;
using Models;
using Service;

public partial class FormsAlunos : Control
{
	private VBoxContainer _nomeContainer;
	private VBoxContainer _sobrenomeContainer;
	private VBoxContainer _buttonContainer;
	
	private Button _buttonCriar;
	
	public override async void _Ready()
	{
		_nomeContainer = GetNode<VBoxContainer>("PanelContainer/VBoxContainer/alunosHBoxContainer/nomeVBoxContainer");
		_sobrenomeContainer = GetNode<VBoxContainer>("PanelContainer/VBoxContainer/alunosHBoxContainer/sobrenomeVBoxContainer");
		_buttonContainer = GetNode<VBoxContainer>("PanelContainer/VBoxContainer/alunosHBoxContainer/buttonContainer");
		_buttonCriar = GetNode<Button>("PanelContainer/VBoxContainer/HBoxContainer/criarButton");
		List<Aluno> alunos = await AlunoService.showAll();

		foreach (Aluno aluno in alunos)
		{
			Label nome = new Label();
			nome.Text = aluno.name;

			Label username = new Label();
			username.Text = "| " + aluno.username;
			
			Button button = new Button();
			button.Text = "Entrar";
			
			_nomeContainer.AddChild(nome);
			_sobrenomeContainer.AddChild(username);
			_buttonContainer.AddChild(button);
			
			int idAluno = aluno.id;
			button.Pressed += () =>
			{
				GD.Print($"[EVENTO] Clicou para entrar! Enviando ID: {idAluno} ({nome.Text})");
				AlunoService.SendMyPersona(idAluno);
			};

		}

		_buttonCriar.Pressed += OnButtonCriarPressed;
	}

	private void OnButtonCriarPressed()
	{
		Error res = GetTree().ChangeSceneToFile("res://Scenes/Forms/AlunoForms.tscn");
		
		if (res != Error.Ok)
		{
			GD.Print("Erro: " + res);
		}
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
