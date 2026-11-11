using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using Game.Scripts;
using Models;
using Repository.Postgress;
using Service;

public partial class AlunoEntrar : Control
{
	private List<string> _ips;
	private VBoxContainer _ipVBoxContainer;
	private VBoxContainer _middleVBoxContainer;
	private VBoxContainer _buttonVBoxContainer;
	
	private SessionManager session;
	
	public override async void _Ready()
	{
		_ipVBoxContainer = GetNode<VBoxContainer>("PanelContainer/VBoxContainer/professorHBoxContainer/ipVBoxContainer");
		_middleVBoxContainer = GetNode<VBoxContainer>("PanelContainer/VBoxContainer/professorHBoxContainer/middleVBoxContainer");
		_buttonVBoxContainer = GetNode<VBoxContainer>("PanelContainer/VBoxContainer/professorHBoxContainer/buttonVBoxContainer");
		
		session = GetNode<SessionManager>("/root/SessionManager");
		
		NetworkPacket res = await AlunoService.enterGame();
		
		string statusDoJogo = "Desconhecido";
		string mensagemDoProf = "Entrar";
		
		try
		{
			// O res.DadosJson veio com a string gigante de fora: {"senderIP":"", "Tipo"..., "DadosJson": ...}
			if (!string.IsNullOrEmpty(res.DadosJson))
			{
				// CAMADA 1: Desserializa o pacote exterior para conseguir aceder à propriedade DadosJson real
				using JsonDocument docExterior = JsonDocument.Parse(res.DadosJson);
				JsonElement raizExterior = docExterior.RootElement;
			
				if (raizExterior.TryGetProperty("DadosJson", out JsonElement dadosJsonElement))
				{
					string jsonInternoBruto = dadosJsonElement.GetString();
				
					if (!string.IsNullOrEmpty(jsonInternoBruto))
					{
						// CAMADA 2: Ativa a insensibilidade a maiúsculas/minúsculas e lê o Status e Message
						var opcoes = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
						var dados = JsonSerializer.Deserialize<Dados>(jsonInternoBruto, opcoes);
					
						statusDoJogo = dados.Status;
						mensagemDoProf = dados.Message;
					
						GD.Print("=== SUCESSO A DESCODIFICAR ===");
						GD.Print("STATUS ENCONTRADO: " + statusDoJogo);
						GD.Print("MESSAGE ENCONTRADA: " + mensagemDoProf);
					}
				}
			}
		}
		catch (Exception e)
		{
			GD.PrintErr("Erro ao decodificar as camadas de JSON: " + e.Message);
		}
		
		Label ip = new Label();
		ip.Text = "Professor: " + mensagemDoProf + res.senderIP;
		Label middle = new Label();
		middle.Text = "|";
		Button button = new Button();
		button.Text = "ENTRAR";
		button.Pressed += OnButtonPressed;
		
		_ipVBoxContainer.AddChild(ip);
		_middleVBoxContainer.AddChild(middle);
		_buttonVBoxContainer.AddChild(button);
		_ips.Add(res.senderIP);
	}

	public void OnButtonPressed()
	{
		Usuario prof = new Professor();
		prof.ip = _ips[0];
		session.usuariosNaRede.Add(prof);
		Error res = GetTree().ChangeSceneToFile("res://Scenes/Forms/AlunoForms.tscn");
		
		if (res != Error.Ok)
		{
			GD.Print("Erro: " + res);
		}
		
	}
	
	
	private struct Dados
	{
		public string Status { get; set; }
		public string Message { get; set; }
	}
	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override  void _Process(double delta)
	{
		

	}
}
