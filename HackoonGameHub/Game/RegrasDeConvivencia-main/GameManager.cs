using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public readonly record struct BehaviorData(string Title, string ImagePath, bool IsGood);

public partial class GameManager : Control
{
    [Export] public Label WordLabel;          
    [Export] public Container OptionsContainer; 
    [Export] public Label DescLabel;          
    [Export] public Label FeedbackLabel;
    [Export] public Button RestartButton;
    [Export] public Button MenuButton;
    [Export] public Container MarginContainer2; 
    [Export] public string MenuScenePath = "res://InicialBase.tscn";
    
    [ExportGroup("Elementos do Jogo de Convivência")]
    [Export] public TextureRect ActionImage;  
    [Export] public ColorRect ScreenFlashRect; 
    [Export] public Texture2D VictoryTexture; 

    [ExportGroup("Ícones dos Botões de Resposta")]
    [Export] public Texture2D GoodIcon; 
    [Export] public Texture2D BadIcon;  

    // Banco de dados completo com todas as atitudes
    private List<BehaviorData> _behaviorDatabase;
    // Lista com as 5 atitudes sorteadas para a partida atual
    private List<BehaviorData> _sessionBehaviors;

    private readonly HashSet<BehaviorData> _usedBehaviors = new(); 
    private readonly Dictionary<string, Texture2D> _textureCache = new();

    private BehaviorData _currentBehavior;
    private int _score = 0;
    private int _correctAnswersCount = 0; 
    private bool _canAnswer = false;
    private readonly Random _rand = new();
    private Tween _flashTween;

    private Button _goodButton;
    private Button _badButton;

    public override void _Ready()
    {
        InitializeDatabase();
        SelectSessionBehaviors(5); 
        PreloadTextures();
        SetupUI();

        StartNewTurn();
    }

    private void SelectSessionBehaviors(int amount)
    {
        _sessionBehaviors = _behaviorDatabase
            .OrderBy(_ => _rand.Next())
            .Take(amount)
            .ToList();
    }

    private void SetupUI()
    {
        MarginContainer2?.Hide();

        if (ScreenFlashRect != null)
        {
            ScreenFlashRect.MouseFilter = MouseFilterEnum.Ignore;
            ScreenFlashRect.Hide();
        }

        if (RestartButton != null)
        {
            RestartButton.Hide();
            RestartButton.Pressed += () => GetTree().ReloadCurrentScene();
        }

        if (MenuButton != null)
        {
            MenuButton.Hide();
            MenuButton.Pressed += OnMenuButtonPressed;
        }

        SetupOptionButtons();
    }

    private void SetupOptionButtons()
    {
        if (OptionsContainer == null) return;

        List<Button> buttons = GetAllButtonsRecursive(OptionsContainer);
        if (buttons.Count < 2) return;

        _goodButton = buttons[0];
        _badButton = buttons[1];

        ConfigureButton(_goodButton, GoodIcon, () => OnOptionSelected(true));
        ConfigureButton(_badButton, BadIcon, () => OnOptionSelected(false));
    }

    private void ConfigureButton(Button button, Texture2D iconTexture, Action onClick)
    {
        button.Text = "";  
        button.Icon = iconTexture;
        button.ExpandIcon = true; 
        button.IconAlignment = HorizontalAlignment.Center;
        button.CustomMinimumSize = new Vector2(250, 300); 

        button.Pressed += onClick;
    }

    private void OnMenuButtonPressed()
    {
        if (!string.IsNullOrEmpty(MenuScenePath))
        {
            GetTree().ChangeSceneToFile(MenuScenePath);
        }
    }

    private void StartNewTurn()
    {
        _canAnswer = true;
        if (FeedbackLabel != null) FeedbackLabel.Text = "";

        // Filtra apenas as atitudes da partida atual que ainda não foram jogadas
        var available = _sessionBehaviors.Where(b => !_usedBehaviors.Contains(b)).ToList();

        if (available.Count == 0)
        {
            GameOver();
            return;
        }

        int randomIndex = _rand.Next(0, available.Count);
        _currentBehavior = available[randomIndex];
        _usedBehaviors.Add(_currentBehavior);

        if (WordLabel != null) WordLabel.Text = "Essa atitude é BOA ou RUIM?";
        if (DescLabel != null) DescLabel.Text = _currentBehavior.Title;

        if (ActionImage != null && !string.IsNullOrEmpty(_currentBehavior.ImagePath))
        {
            if (_textureCache.TryGetValue(_currentBehavior.ImagePath, out var texture))
            {
                ActionImage.Texture = texture;
                ActionImage.Show();
            }
            else
            {
                ActionImage.Texture = null;
            }
        }

        OptionsContainer?.Show();
        _goodButton?.Show();
        _badButton?.Show();
    }

    private async void OnOptionSelected(bool choseGood)
    {
        if (!_canAnswer) return;

        if (choseGood == _currentBehavior.IsGood)
        {
            _canAnswer = false;
            _score += 10;
            _correctAnswersCount++; 
            
            ShowFeedback("Acertou!", new Color("22C55E")); 
            FlashScreen(new Color(0.0f, 0.9f, 0.2f, 0.65f)); 
            
            await ToSignal(GetTree().CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout);
            
            if (!IsInstanceValid(this)) return;
            
            StartNewTurn();
        }
        else
        {
            await HandleWrongAnswer("Errou! Essa atitude não é assim.");
        }
    }

    private async System.Threading.Tasks.Task HandleWrongAnswer(string message)
    {
        _canAnswer = false;
        ShowFeedback(message, new Color("EF4444")); 
        FlashScreen(new Color(1.0f, 0.0f, 0.1f, 0.65f)); 
        
        await ToSignal(GetTree().CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
        
        if (!IsInstanceValid(this)) return;

        StartNewTurn();
    }

    private void FlashScreen(Color flashColor)
    {
        if (ScreenFlashRect == null) return;

        ScreenFlashRect.Show();
        ScreenFlashRect.Color = flashColor;

        _flashTween?.Kill();
        _flashTween = CreateTween();
        _flashTween.TweenProperty(ScreenFlashRect, "color:a", 0.0f, 0.6f);
    }

    private void ShowFeedback(string text, Color color)
    {
        if (FeedbackLabel == null) return;
        FeedbackLabel.Text = text;
        FeedbackLabel.AddThemeColorOverride("font_color", color);
    }

    private void GameOver()
    {
        _canAnswer = false;
        int totalQuestions = _sessionBehaviors.Count; // Total da partida (5)

        OptionsContainer?.Hide();
        FeedbackLabel?.Hide();
        
        if (WordLabel != null) 
            WordLabel.Text = $"FIM DE JOGO!\nVocê acertou {_correctAnswersCount} de {totalQuestions}!";

        if (_correctAnswersCount == totalQuestions)
        {
            if (DescLabel != null)
            {
                DescLabel.Text = "PARABÉNS! VOCÊ ACERTOU TUDO!";
                DescLabel.Show();
            }

            if (ActionImage != null)
            {
                if (VictoryTexture != null)
                {
                    ActionImage.Texture = VictoryTexture;
                    ActionImage.Show();
                }
                else
                {
                    ActionImage.Hide();
                }
            }
        }
        else 
        {
            if (DescLabel != null)
            {
                DescLabel.Text = "Tente novamente para acertar todas!";
                DescLabel.Show();
            }
            ActionImage?.Hide();
        }
        
        RestartButton?.Show();
        MenuButton?.Show();
        MarginContainer2?.Show(); 
    }

    private List<Button> GetAllButtonsRecursive(Node parent)
    {
        List<Button> buttons = new();
        foreach (Node child in parent.GetChildren())
        {
            if (child is Button btn)
            {
                if (btn == RestartButton || btn == MenuButton) continue;
                buttons.Add(btn);
            }
            else if (child.GetChildCount() > 0)
            {
                buttons.AddRange(GetAllButtonsRecursive(child));
            }
        }
        return buttons;
    }

    private void PreloadTextures()
    {
        
        foreach (var behavior in _sessionBehaviors)
        {
            if (!string.IsNullOrEmpty(behavior.ImagePath) && ResourceLoader.Exists(behavior.ImagePath))
            {
                _textureCache[behavior.ImagePath] = GD.Load<Texture2D>(behavior.ImagePath);
            }
        }
    }

    private void InitializeDatabase()
    {
        _behaviorDatabase = new List<BehaviorData>
        {
                //as fotos a gente muda aqui diana
            new("Levantar a mão para perguntar", "res://imagens/levantarmao.png", true),
            new("Dividir os brinquedos", "res://imagens/dividindo.png", true),
            new("Empurrar o colega", "res://imagens/empurrar.png", false),
            new("Dizer palavras feias", "res://imagens/palavrao.png", false),
            new("Usar palavras mágicas", "res://imagens/palavras_magicas.png", true),
            new("Respeitar e ajudar o colega", "res://imagens/ajudar_colega.png", true),
            new("Jogar o lixo na lixeira", "res://imagens/lixo_lixeira.png", true),
            new("Correr no corredor", "res://imagens/correr_corredor.pn'g", false),
            new("Entrar na sala empurrando", "res://imagens/entrar_empurrando.png", false),
            new("Gritar no meio da sala de aula", "res://imagens/gritar_sala.png", false)
        };
    }
}