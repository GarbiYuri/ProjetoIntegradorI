using Godot;
using System;
using System.Collections.Generic;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;
using Game.Scripts;
using Models;
using Service;

public partial class LobbyPanel : Control
{
	private Label _nomeSalaLabel;

	private VBoxContainer _usernameContainer;
	private VBoxContainer _nameContainer;
	private VBoxContainer _middleContainer;
	
	private SessionManager session;
	
	
	public override async void _Ready()
	{
		_nomeSalaLabel = GetNode<Label>("PanelContainer/VBoxContainer/HBoxContainer/nomeSalaLabel");
		_usernameContainer = GetNode<VBoxContainer>("PanelContainer/VBoxContainer/alunosHBoxContainer/usernameVBoxContainer");
		_nameContainer = GetNode<VBoxContainer>("PanelContainer/VBoxContainer/alunosHBoxContainer/nomeVBoxContainer");
		_middleContainer = GetNode<VBoxContainer>("PanelContainer/VBoxContainer/alunosHBoxContainer/middleVBoxContainer");
		
		session = GetNode<SessionManager>("/root/SessionManager");

		_nomeSalaLabel.Text = session.sala.name;

		NetworkPacket res = await ProfessorService.carregarAlunos();
		
		string statusDoJogo = "Desconhecido";
		int idDoAluno = -1;
		
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
						idDoAluno = dados.ID;
					
						GD.Print("=== SUCESSO A DESCODIFICAR ===");
						GD.Print("STATUS ENCONTRADO: " + statusDoJogo);
						GD.Print("ID DO ALUNO ENCONTRADO: " + idDoAluno);
					}
				}
			}
		}	
		catch (Exception e)
		{
			GD.PrintErr("Erro ao decodificar as camadas de JSON: " + e.Message);
		}
		Aluno aluno = await AlunoService.Read(idDoAluno);
		session.usuariosNaRede.Add(aluno);

		foreach (var usuario in session.usuariosNaRede)
		{
			Label name = new Label();
			name.Text = usuario.name;

			if (usuario.GetType() == typeof(Aluno))
			{
				Aluno a = usuario as Aluno;
				Label username = new Label();
				username.Text = string.IsNullOrEmpty(a.username) ? "" :  a.username;
			}
		}

	}
	
	

	private struct Dados
	{
		public string Status { get; set; }
		public int ID { get; set; }
	}
	
	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override async void _Process(double delta)
	{
		await ProfessorService.SendMessage(session.usuario.name);
	}
}
