using Godot;
using System;
using Models;
using Repository.Postgress;

public partial class CriarSala : Button
{
	public override async void _Pressed()
	{
		// Aqui você pegaria os dados de um LineEdit
		Sala sala = new Sala();
		string nome = "Sala de Lógica " + DateTime.Now.Second;
		sala.name = nome;
		sala.descricao = "Desafios para crianças";
		await SalaDB.Create(sala);
	}
}
