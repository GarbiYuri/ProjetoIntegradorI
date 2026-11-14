using Godot;

public partial class InicialSilabas : Control
{
    [Export] public Control MainMenuContainer; // Arraste seu PanelContainer aqui
    [Export] public Button PlayButton;
    [Export] public Button ExitButton;

    public override void _Ready()
    {
        // Garante que o menu principal esteja visível ao iniciar
        if (MainMenuContainer != null)
            MainMenuContainer.Show();

        // Conecta os botões
        if (PlayButton != null)
            PlayButton.Pressed += OnPlayClicked;

        if (ExitButton != null)
            ExitButton.Pressed += () => GetTree().Quit();
    }

    private void OnPlayClicked()
    {
        // Vai direto para a cena do jogo (Main.tscn)
        if (ResourceLoader.Exists("res://Main.tscn"))
        {
            GetTree().ChangeSceneToFile("res://Main.tscn");
        }
        else
        {
            GD.PrintErr("Cena res://Main.tscn não encontrada!");
        }
    }
}