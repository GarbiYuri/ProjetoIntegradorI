using Godot;
using System;
using System.Collections.Generic;
using Game.Scripts;
using Models;
using Repository.Exceptions;
using Repository.Postgress;
using Service;

public partial class FormsSalas : Control
{
	private VBoxContainer containerSalas;
	private Label helloLabel;
	private Label _emptyLabel;
	
	private Button saveButton;
	private Button cancelarButton;
	
	private LineEdit nomeEdit;
	private LineEdit descricaoEdit;

	private ReturnScene _window;

	private bool _editing = false;

	private Sala _salaToEdit;

	private void RedirecionarLogin()
	{
		Error resultado = GetTree().ChangeSceneToFile("res://Scenes/Forms/FormsProfessor.tscn");

		if (resultado != Error.Ok)
		{
			GD.PrintErr("[SALAS] Não foi possível voltar para o login: " + resultado);
		}
	}

	public override async void _Ready()
	{
		helloLabel = GetNode<Label>("helloLabel");
		var session = GetNode<SessionManager>("/root/SessionManager");

		// Esta tela só existe dentro do fluxo do professor (login → salas).
		// Se alguém abrir a cena sem sessão, volta para o login em vez de quebrar.
		if (session.usuario is not Professor professor)
		{
			GD.PrintErr("[SALAS] Sem professor logado; voltando para o login.");
			// CallDeferred: durante o _Ready a árvore ainda está montando os filhos.
			CallDeferred(nameof(RedirecionarLogin));
			return;
		}

		helloLabel.Text += professor.name;

		_emptyLabel = GetNode<Label>("%EmptyLabel");
		_emptyLabel.Visible = false;
		
		containerSalas = GetNode<VBoxContainer>("HBoxContainer/PanelSalas/VBoxSalas/containerSalas");
		
		saveButton = GetNode<Button>("HBoxContainer/PanelFormsSalas/VBoxContainer/HBoxContainer/salaButton");
		cancelarButton = GetNode<Button>("HBoxContainer/PanelFormsSalas/VBoxContainer/HBoxContainer/cancelarButton");
		
		nomeEdit = GetNode<LineEdit>("HBoxContainer/PanelFormsSalas/VBoxContainer/nomeEdit");
		descricaoEdit = GetNode<LineEdit>("HBoxContainer/PanelFormsSalas/VBoxContainer/descricaoEdit");
		
		cancelarButton.Visible = false;
		cancelarButton.Pressed += OnCancelarPressed;
		saveButton.Pressed += OnSavePressed;
		
		// Em um banco novo ainda não há salas; nesse caso a lista começa vazia
		// em vez de impedir o professor de criar a primeira sala.
		List<Sala> salas;
		try
		{
			salas = await SalaService.ReadAll();
		}
		catch (ResourceNotFoundException)
		{
			salas = new List<Sala>();
		}

		if (!IsInstanceValid(this) || !IsInsideTree())
		{
			return;
		}

		foreach (Sala sala in salas)
		{
			if (sala != null)
			{
				MenuButton newButton = new MenuButton();
				
				if (!string.IsNullOrEmpty(sala.descricao))
				{
					newButton.Text = sala.name + " : " + sala.descricao;
				}
				else
				{
					newButton.Text = sala.name;
				}
				
				PopupMenu popup =  newButton.GetPopup();
				popup.AddItem("Entrar na Sala", 0);
				popup.AddItem("Editar", 1);
				popup.AddItem("Excluir", 2);
				
				containerSalas.AddChild(newButton);

				popup.IdPressed += (idItemClicado) => OnPoputPressed(idItemClicado, sala);
			}
		}

		_emptyLabel.Visible = salas.Count == 0;
		
		_window = GetNode<ReturnScene>("Control");
		
	}

	public async void OnSavePressed()
	{
		if (_editing)
		{
			if (!string.Equals(nomeEdit.Text, _salaToEdit.name))
			{
				_salaToEdit.name = nomeEdit.Text;
			}

			if (!string.Equals(descricaoEdit.Text, _salaToEdit.descricao))
			{
				_salaToEdit.descricao = descricaoEdit.Text;
			}

			try
			{
				await SalaService.Update(_salaToEdit, _salaToEdit.id);
				
				nomeEdit.Text = "";
				descricaoEdit.Text = "";
				cancelarButton.Visible = false;
				_editing = false;
				_salaToEdit = new Sala();
				
				GetTree().ReloadCurrentScene();
				
			}
			catch (ResourceNotFoundException e)
			{
				_window.subtitleLabel.Text = "ERRO";
				_window.descriptionLabel.Text = e.Message;
				_window.Show();
				nomeEdit.Text = "";
				descricaoEdit.Text = "";
				cancelarButton.Visible = false;
				_editing = false;
				_salaToEdit = new Sala();
			}
			
		}
		else
		{
			try
			{
				Sala sala = new Sala(nomeEdit.Text, descricaoEdit.Text);

				await SalaService.CreateSala(sala);

				MenuButton newButton = new MenuButton();

				if (!string.IsNullOrEmpty(sala.descricao))
				{
					newButton.Text = sala.name + " : " + sala.descricao;
				}
				else
				{
					newButton.Text = sala.name;
				}


				PopupMenu popup = newButton.GetPopup();
				popup.AddItem("Entrar na Sala", 0);
				popup.AddItem("Editar", 1);
				popup.AddItem("Excluir", 2);

				containerSalas.AddChild(newButton);

				popup.IdPressed += (idItemClicado) => OnPoputPressed(idItemClicado, sala);
				_emptyLabel.Visible = false;
			}
			catch (InvalidParameterException e)
			{
				_window.subtitleLabel.Text = "ERRO";
				_window.descriptionLabel.Text = e.Message;
				_window.Show();
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}
		}
	}

	public void OnCancelarPressed()
	{
		nomeEdit.Text = "";
		descricaoEdit.Text = "";
		cancelarButton.Visible = false;
		_editing = false;
		_salaToEdit = new Sala();
	}

	public async void OnPoputPressed(long id, Sala sala)
	{
		if (id == 0)
		{
			var session = GetNode<SessionManager>("/root/SessionManager");
			session.sala = sala;
			session.roomName = sala.name;
			session.playerName = session.usuario?.name ?? "Professor";
			session.lobbyMode = LobbyMode.Host;
			session.lobbyIp = "127.0.0.1";

			// Vai direto para o lobby: o painel do professor (dashboard) fica
			// lá dentro e a sessão já nasce aberta para os alunos entrarem.
			Error result = GetTree().ChangeSceneToFile("res://Scenes/Personagem/LobbyPrincipal.tscn");
			if (result != Error.Ok)
			{
				GD.PrintErr("Erro ao abrir a sala: " + result);
			}
		}
		else if (id == 1)
		{
			_editing = true;
			cancelarButton.Visible = true;
			nomeEdit.Text = sala.name;
			descricaoEdit.Text = sala.descricao;
			_salaToEdit = sala;
			
		}else if (id == 2)
		{
			await SalaService.Delete(sala.id);
			GetTree().ReloadCurrentScene();
		}
		
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override async void _Process(double delta)
	{
		
	}
}
