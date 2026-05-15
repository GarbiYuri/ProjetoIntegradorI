using Godot;
using System;
using Models;
using Repository.Postgress;

public partial class Salas : Control
{
	[Export] public PackedScene SalaItemScene; // Uma ceninha simples com Label e Botão "Entrar"
	private VBoxContainer _listaContainer;

	public override void _Ready()
	{
		Sala s = new Sala("teste");
		_listaContainer = GetNode<VBoxContainer>("ScrollContainer/VBoxContainer");
		AtualizarListaDeSalas();
	}

	private async void AtualizarListaDeSalas()
	{
		// Limpa a lista atual
		foreach (Node child in _listaContainer.GetChildren()) child.QueueFree();

		// Busca no banco
		var salas = await SalaDB.ReadAll();

		foreach (var sala in salas)
		{
			// Instancia um botão ou painel para cada sala
			Button btnSala = new Button();
			btnSala.Text = $"{sala.name} - {sala.descricao}";
			btnSala.Pressed += () => EntrarNaSala(sala.id);
			_listaContainer.AddChild(btnSala);
		}
	}

	private void _on_btn_criar_sala_pressed()
	{
		CriarSala criarSala = new CriarSala();
		criarSala._Pressed();
		AtualizarListaDeSalas();
	}

	private void EntrarNaSala(int salaId)
	{
		GD.Print($"Entrando na sala ID: {salaId}");
		// Salva o ID na sua classe global e muda para a cena dos minijogos
		GetTree().ChangeSceneToFile("res://scenes/MenuJogos.tscn");
	}
}
