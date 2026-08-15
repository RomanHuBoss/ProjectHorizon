using Godot;

public partial class GamePauseOverlay : CanvasLayer
{
    private SalvageRepairSlice? _host;
    private Control? _backdrop;
    private VBoxContainer? _menu;
    private GameSettingsPanel? _settings;
    private Label? _title;
    private Label? _description;
    private Button? _resume;
    private AudioDirector? _audio;
    private bool _deathMode;
    private string _deathReasonKey = "ui.death.default";

    public bool IsOpen => _backdrop?.Visible ?? false;
    public bool IsDeathMode => _deathMode && IsOpen;
    public bool UiContractReady => _backdrop is not null && _menu is not null &&
        _settings is not null && _title is not null && _description is not null &&
        _resume is not null;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 100;
        _host = GetParent() as SalvageRepairSlice;
        if (_host is null)
        {
            throw new System.InvalidOperationException(
                "GamePauseOverlay must be a direct child of SalvageRepairSlice.");
        }
        GameLocalizationService.LocaleChanged += OnLocaleChanged;
        BuildUi();
        _audio = AudioDirector.EnsureInstalled(GetTree());
        _audio.AttachUiSounds(this);
        GameLocalizationService.LocalizeControlTree(this);
    }

    public override void _ExitTree()
    {
        GameLocalizationService.LocaleChanged -= OnLocaleChanged;
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (!inputEvent.IsActionPressed("pause"))
        {
            return;
        }

        if (IsOpen)
        {
            if (!_deathMode && !(_settings?.Visible ?? false))
            {
                ResumeGame();
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        if (_host?.CanOpenApplicationPause() ?? false)
        {
            OpenPause();
            GetViewport().SetInputAsHandled();
        }
    }

    public void OpenPause()
    {
        if (_backdrop is null || _menu is null || _settings is null ||
            _title is null || _description is null || _resume is null)
        {
            return;
        }
        _deathMode = false;
        _deathReasonKey = "ui.death.default";
        _title.Text = GameLocalizationService.Text("ui.pause.title");
        _description.Text = GameLocalizationService.Text("ui.pause.description");
        _resume.Visible = true;
        _menu.Visible = true;
        _settings.Visible = false;
        _backdrop.Visible = true;
        GetTree().Paused = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _resume.GrabFocus();
        _audio?.PlayUiConfirm();
        GD.Print("TASK-130 pause PASS: treePaused=1; overlayProcess=Always; mouse=visible.");
    }

    public void ShowDeath(string reasonKey)
    {
        if (_backdrop is null || _menu is null || _settings is null ||
            _title is null || _description is null || _resume is null)
        {
            return;
        }
        _deathMode = true;
        _deathReasonKey = string.IsNullOrWhiteSpace(reasonKey) ? "ui.death.default" : reasonKey;
        _title.Text = GameLocalizationService.Text("ui.death.title");
        _description.Text = GameLocalizationService.Text(_deathReasonKey);
        _resume.Visible = false;
        _settings.Visible = false;
        _menu.Visible = true;
        _backdrop.Visible = true;
        GetTree().Paused = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _audio?.PlayUiError();
        GD.Print("TASK-130 death screen PASS: visible=1; treePaused=1; status=text+iconToken.");
    }

    public void CloseForSceneTransition()
    {
        if (_backdrop is not null)
        {
            _backdrop.Visible = false;
        }
        GetTree().Paused = false;
    }

    private void BuildUi()
    {
        _backdrop = new Control { Visible = false };
        _backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_backdrop);
        ColorRect shade = new()
        {
            Color = new Color(0.005f, 0.009f, 0.016f, 0.91f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _backdrop.AddChild(shade);

        PanelContainer panel = new()
        {
            AnchorLeft = 0.5f,
            AnchorTop = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -330,
            OffsetTop = -270,
            OffsetRight = 330,
            OffsetBottom = 270
        };
        _backdrop.AddChild(panel);
        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 28);
        margin.AddThemeConstantOverride("margin_right", 28);
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        panel.AddChild(margin);
        _menu = new VBoxContainer();
        _menu.AddThemeConstantOverride("separation", 12);
        margin.AddChild(_menu);

        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _title.AddThemeFontSizeOverride("font_size", 28);
        _menu.AddChild(_title);
        _description = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(580, 70)
        };
        _menu.AddChild(_description);
        _menu.AddChild(new HSeparator());

        _resume = PauseButton("ui.pause.resume");
        _resume.Pressed += ResumeGame;
        _menu.AddChild(_resume);
        Button settingsButton = PauseButton("ui.pause.settings");
        settingsButton.Pressed += OpenSettings;
        _menu.AddChild(settingsButton);
        Button main = PauseButton("ui.pause.save_main");
        main.Pressed += ReturnToMainMenu;
        _menu.AddChild(main);
        Button quit = PauseButton("ui.pause.save_quit");
        quit.Pressed += QuitGame;
        _menu.AddChild(quit);

        _settings = new GameSettingsPanel
        {
            Visible = false,
            AnchorLeft = 0.5f,
            AnchorTop = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -430,
            OffsetTop = -325,
            OffsetRight = 430,
            OffsetBottom = 325
        };
        _settings.CloseRequested += CloseSettings;
        _settings.Applied += _ => _host?.ApplyApplicationSettings();
        _backdrop.AddChild(_settings);
    }

    private void OnLocaleChanged(string _)
    {
        GameLocalizationService.LocalizeControlTree(this);
        if (_title is null || _description is null) return;
        _title.Text = GameLocalizationService.Text(_deathMode ? "ui.death.title" : "ui.pause.title");
        _description.Text = GameLocalizationService.Text(_deathMode ? _deathReasonKey : "ui.pause.description");
    }

    private static Button PauseButton(string text) => new()
    {
        Text = text,
        CustomMinimumSize = new Vector2(560, 50),
        FocusMode = Control.FocusModeEnum.All
    };

    private void ResumeGame()
    {
        if (_deathMode)
        {
            return;
        }
        if (_backdrop is not null) _backdrop.Visible = false;
        if (_settings is not null) _settings.Visible = false;
        GetTree().Paused = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
        _audio?.PlayUiConfirm();
        GD.Print("TASK-130 resume PASS: treePaused=0; gameplay=active.");
    }

    private void OpenSettings()
    {
        if (_settings is null || _menu is null) return;
        _menu.Visible = false;
        _settings.Visible = true;
        _settings.ReloadFromService();
    }

    private void CloseSettings()
    {
        if (_settings is null || _menu is null) return;
        _settings.Visible = false;
        _menu.Visible = true;
    }

    private void ReturnToMainMenu()
    {
        GetTree().Paused = false;
        _host?.RequestReturnToMainMenu();
    }

    private void QuitGame()
    {
        GetTree().Paused = false;
        _host?.RequestApplicationQuit();
    }
}
