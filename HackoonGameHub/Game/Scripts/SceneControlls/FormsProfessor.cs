using Godot;
using System;
using System.Threading.Tasks;
using Game.Scripts;
using Models;
using Repository.Exceptions;
using Service;

public partial class FormsProfessor : Control
{
	private Button btnVoltar;
	private Button btnCadastrar;
	private Button btnEntrar;
	
	private Label returnLabel;
	
	private LineEdit txtNome;
	private LineEdit txtEmail;
	private LineEdit txtSenha;

	private AnimatedSprite2D carregamento;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		btnVoltar = GetNode<Button>("PanelContainer/VBoxContainer/HBoxContainer/voltarButton");
		btnCadastrar = GetNode<Button>("PanelContainer/VBoxContainer/HBoxContainer/cadastrarButton");
		btnEntrar = GetNode<Button>("PanelContainer/VBoxContainer/HBoxContainer/entrarButton");
		
		btnVoltar.Pressed += OnVoltarPressed;
		btnCadastrar.Pressed += OnCadastrarPressed;
		btnEntrar.Pressed += OnEntrarPressed;
		
		txtNome = GetNode<LineEdit>("PanelContainer/VBoxContainer/textNome");
		txtEmail = GetNode<LineEdit>("PanelContainer/VBoxContainer/textEmail");
		txtSenha = GetNode<LineEdit>("PanelContainer/VBoxContainer/textSenha");
		
		returnLabel = GetNode<Label>("returnLabel");
		returnLabel.Visible = false;
		
		carregamento = GetNode<AnimatedSprite2D>("Carregamento");
		carregamento.Visible = false;
	}

	private void OnVoltarPressed()
	{
		Error result = GetTree().ChangeSceneToFile("res://Scenes/InitialScene.tscn");
		if (result != Error.Ok)
		{
			GD.Print("Erro ao carrergr a cena: " + result);
		}
	}

	private async void OnCadastrarPressed()
	{
		try
		{
			carregamento.Visible = true;
			returnLabel.Visible = true;
			returnLabel.Text = "Conectando ao Banco";
			carregamento.Play("default");
			string result = await ProfessorService.StartGameAsync();
			
			returnLabel.Text = result;

			await Task.Delay(1000);
			returnLabel.Visible = false;
			carregamento.Visible = false;
			
			Professor professor = new Professor(txtNome.GetText(), txtEmail.GetText(), txtSenha.GetText());
			await ProfessorService.RegisterProfessor(professor);
			var session = GetNode<SessionManager>("/root/SessionManager");
			session.usuario = professor;
			
			returnLabel.Visible = true;
			returnLabel.Text = "Cadastrado com sucesso!";

			txtEmail.Text = " ";
			txtNome.Text = " ";
			txtSenha.Text = " ";
			
			Error resul = GetTree().ChangeSceneToFile("res://Scenes/Forms/FormsSalas.tscn");

			if (resul != Error.Ok)
			{
				GD.Print("Erro ao carrergr a cena: " + result);
			}
			
		}
		catch (InvalidParameterException ex)
		{
			Console.WriteLine(ex.Message);
		}
		catch (ResourceAlreadyExistsException ex)
		{
			Console.WriteLine(ex.Message);
		}
		catch (Exception e)
		{
			Console.WriteLine(e.Message);
		}
		
	}

	private async void OnEntrarPressed()
	{
		try
		{
			carregamento.Visible = true;
			returnLabel.Visible = true;
			returnLabel.Text = "Conectando ao Banco";
			carregamento.Play("default");
			string result = await ProfessorService.StartGameAsync();
			
			returnLabel.Text = result;

			await Task.Delay(1000);
			returnLabel.Visible = false;
			carregamento.Visible = false;
			
			Professor professor = new Professor(txtNome.GetText(), txtEmail.GetText(), txtSenha.GetText());
			professor = await ProfessorService.LoginProfessor(txtEmail.GetText(), txtSenha.GetText());
			var session = GetNode<SessionManager>("/root/SessionManager");
			session.usuario = professor;
			
			Error resul = GetTree().ChangeSceneToFile("res://Scenes/Forms/FormsSalas.tscn");

			if (resul != Error.Ok)
			{
				GD.Print("Erro ao carrergr a cena: " + result);
			}
			
		}
		catch (ResourceNotFoundException e)
		{
			GD.Print(e.Message);
		}
		catch (Exception e)
		{
			GD.Print("Erro Inesperado: " + e.Message);
		}
		
	}
	
	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
