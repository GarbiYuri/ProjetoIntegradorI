using Godot;

/// <summary>
/// Ferramenta de desenvolvimento para capturar screenshots das telas.
///
/// Uso (precisa de uma janela/display real, não headless):
///   Godot --path Game res://Scenes/Dev/ScreenshotRunner.tscn -- \
///       res://Scenes/Forms/AlunoConta.tscn /tmp/opencode/conta.png
///
/// O primeiro argumento é a cena a abrir; o segundo é o arquivo PNG de saída.
/// </summary>
public partial class ScreenshotRunner : Node
{
	public override void _Ready()
	{
		// O capturador fica pendurado no Root para sobreviver à troca de cena.
		GetTree().Root.CallDeferred("add_child", new CaptureNode());
	}
}

public partial class CaptureNode : Node
{
	public override async void _Ready()
	{
		string[] args = OS.GetCmdlineUserArgs();
		string cena = args.Length > 0 ? args[0] : "res://Scenes/InitialScene.tscn";
		string saida = args.Length > 1 ? args[1] : "/tmp/opencode/shot.png";

		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		GetTree().ChangeSceneToFile(cena);

		// Espera alguns frames para o layout e o background assentarem.
		int frames = 50;

		// "--espera=60" espera mais frames (útil para capturar telas que
		// dependem de outro processo, como o painel com a lista de jogos).
		foreach (string argumento in OS.GetCmdlineUserArgs())
		{
			if (argumento.StartsWith("--espera=") &&
				int.TryParse(argumento.Substring("--espera=".Length), out int extra))
			{
				frames += extra;
			}
		}

		for (int i = 0; i < frames; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}

		Image imagem = GetViewport().GetTexture().GetImage();
		imagem.SavePng(saida);
		GD.Print("[SHOT] Salvo em " + saida);
		GetTree().Quit();
	}
}
