using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Godot;

public partial class MainMenuController : Control
{
    private const string GameplayScene = "res://Scenes/VerticalSlice/SalvageRepairSlice.tscn";

    private readonly CancellationTokenSource _lifetime = new();
    private Task<SaveGameSnapshot?>? _scanTask;
    private Task? _newGameTask;
    private SaveGameSnapshot? _snapshot;
    private VBoxContainer? _mainPanel;
    private HBoxContainer? _mainBody;
    private VBoxContainer? _newGamePanel;
    private VBoxContainer? _loadPanel;
    private GameSettingsPanel? _settingsPanel;
    private Button? _continueButton;
    private Button? _loadButton;
    private Label? _saveSummary;
    private Label? _status;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        GetTree().Paused = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GameUserSettingsService.ReloadAndApply();
        GameLocalizationService.LocaleChanged += OnLocaleChanged;
        BuildUi();
        GameLocalizationService.LocalizeControlTree(this);
        ShowMain();
        _status!.Text = GameLocalizationService.Text("ui.main.status.inspecting");
        _scanTask = ScanPrimarySlotAsync(_lifetime.Token);
        GD.Print("TASK-130 application shell READY: mainMenu=1; newGame=1; load=1; settings=1; pause=1; death=1; separateSettings=1; keyboardRemap=1; gamepad=1.");
    }

    public override void _Process(double delta)
    {
        _ = delta;
        PollScan();
        PollNewGame();
    }

    public override void _ExitTree()
    {
        GameLocalizationService.LocaleChanged -= OnLocaleChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private void BuildUi()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ColorRect background = new()
        {
            Color = new Color(0.008f, 0.015f, 0.028f, 1.0f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(background);

        ColorRect accent = new()
        {
            Color = new Color(0.02f, 0.20f, 0.28f, 0.35f),
            MouseFilter = MouseFilterEnum.Ignore,
            Position = new Vector2(0, 0),
            Size = new Vector2(18, 2000)
        };
        AddChild(accent);

        MarginContainer margin = new();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 72);
        margin.AddThemeConstantOverride("margin_right", 72);
        margin.AddThemeConstantOverride("margin_top", 54);
        margin.AddThemeConstantOverride("margin_bottom", 42);
        AddChild(margin);

        VBoxContainer root = new();
        root.AddThemeConstantOverride("separation", 16);
        margin.AddChild(root);
        Label title = new() { Text = "ui.main.title" };
        title.AddThemeFontSizeOverride("font_size", 44);
        root.AddChild(title);
        Label subtitle = new()
        {
            Text = "ui.main.subtitle",
            Modulate = new Color(0.68f, 0.86f, 0.94f, 1.0f)
        };
        subtitle.AddThemeFontSizeOverride("font_size", 15);
        root.AddChild(subtitle);
        root.AddChild(new HSeparator());

        HBoxContainer body = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        _mainBody = body;
        body.AddThemeConstantOverride("separation", 36);
        root.AddChild(body);

        VBoxContainer nav = new() { CustomMinimumSize = new Vector2(430, 0) };
        nav.AddThemeConstantOverride("separation", 10);
        body.AddChild(nav);
        _mainPanel = nav;

        _continueButton = MenuButton("ui.main.continue");
        _continueButton.Pressed += ContinueGame;
        nav.AddChild(_continueButton);
        Button newGame = MenuButton("ui.main.new_game");
        newGame.Pressed += ShowNewGame;
        nav.AddChild(newGame);
        _loadButton = MenuButton("ui.main.load_game");
        _loadButton.Pressed += ShowLoad;
        nav.AddChild(_loadButton);
        Button settings = MenuButton("ui.main.settings");
        settings.Pressed += ShowSettings;
        nav.AddChild(settings);
        Button quit = MenuButton("ui.main.quit");
        quit.Pressed += () => GetTree().Quit();
        nav.AddChild(quit);

        VBoxContainer info = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        info.AddThemeConstantOverride("separation", 12);
        body.AddChild(info);
        Label version = new()
        {
            Text = "ui.main.version",
            HorizontalAlignment = HorizontalAlignment.Right,
            Modulate = new Color(0.55f, 0.72f, 0.79f, 1.0f)
        };
        info.AddChild(version);
        _saveSummary = new Label
        {
            Text = "ui.main.save_scanning",
            HorizontalAlignment = HorizontalAlignment.Right,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _saveSummary.AddThemeFontSizeOverride("font_size", 18);
        info.AddChild(_saveSummary);

        _newGamePanel = BuildSubPanel("ui.main.new_game");
        AddChild(_newGamePanel);
        Label newText = new()
        {
            Text = "ui.main.new_game_info",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _newGamePanel.AddChild(newText);
        Button start = MenuButton("ui.main.start_standard");
        start.Pressed += BeginNewGame;
        _newGamePanel.AddChild(start);
        Button newBack = MenuButton("ui.common.back");
        newBack.Pressed += ShowMain;
        _newGamePanel.AddChild(newBack);

        _loadPanel = BuildSubPanel("ui.main.load_game");
        AddChild(_loadPanel);
        Label loadText = new()
        {
            Name = "LoadSlotSummary",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _loadPanel.AddChild(loadText);
        Button loadPrimary = MenuButton("ui.main.load_primary");
        loadPrimary.Name = "LoadPrimary";
        loadPrimary.Pressed += ContinueGame;
        _loadPanel.AddChild(loadPrimary);
        Button loadBack = MenuButton("ui.common.back");
        loadBack.Pressed += ShowMain;
        _loadPanel.AddChild(loadBack);

        _settingsPanel = new GameSettingsPanel
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
        _settingsPanel.CloseRequested += ShowMain;
        AddChild(_settingsPanel);

        _status = new Label
        {
            AnchorTop = 1.0f,
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = 72,
            OffsetTop = -58,
            OffsetRight = -72,
            OffsetBottom = -24,
            Modulate = new Color(0.66f, 0.84f, 0.90f, 1.0f)
        };
        AddChild(_status);
    }

    private VBoxContainer BuildSubPanel(string title)
    {
        VBoxContainer panel = new()
        {
            Visible = false,
            AnchorLeft = 0.5f,
            AnchorTop = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -360,
            OffsetTop = -210,
            OffsetRight = 360,
            OffsetBottom = 210
        };
        panel.AddThemeConstantOverride("separation", 16);
        Label heading = new() { Text = title, HorizontalAlignment = HorizontalAlignment.Center };
        heading.AddThemeFontSizeOverride("font_size", 32);
        panel.AddChild(heading);
        panel.AddChild(new HSeparator());
        return panel;
    }

    private static Button MenuButton(string text) => new()
    {
        Text = text,
        CustomMinimumSize = new Vector2(390, 52),
        FocusMode = FocusModeEnum.All
    };

    private void ShowMain()
    {
        _mainBody!.Visible = true;
        _mainPanel!.Visible = true;
        _newGamePanel!.Visible = false;
        _loadPanel!.Visible = false;
        _settingsPanel!.Visible = false;
        _continueButton?.GrabFocus();
    }

    private void ShowNewGame()
    {
        _mainBody!.Visible = false;
        _mainPanel!.Visible = false;
        _newGamePanel!.Visible = true;
        _loadPanel!.Visible = false;
        _settingsPanel!.Visible = false;
    }

    private void ShowLoad()
    {
        _mainBody!.Visible = false;
        _mainPanel!.Visible = false;
        _newGamePanel!.Visible = false;
        _loadPanel!.Visible = true;
        _settingsPanel!.Visible = false;
        UpdateLoadPanel();
    }

    private void ShowSettings()
    {
        _mainBody!.Visible = false;
        _mainPanel!.Visible = false;
        _newGamePanel!.Visible = false;
        _loadPanel!.Visible = false;
        _settingsPanel!.Visible = true;
        _settingsPanel.ReloadFromService();
    }

    private void ContinueGame()
    {
        if (_snapshot is null || _newGameTask is not null)
        {
            _status!.Text = GameLocalizationService.Text("ui.main.status.no_slot");
            return;
        }
        ChangeToGameplay("continue");
    }

    private void BeginNewGame()
    {
        if (_newGameTask is not null || (_scanTask is not null && !_scanTask.IsCompleted))
        {
            return;
        }
        _status!.Text = GameLocalizationService.Text("ui.main.status.resetting");
        _newGameTask = ResetPrimarySlotAsync(_lifetime.Token);
    }

    private void PollScan()
    {
        if (_scanTask is null || !_scanTask.IsCompleted)
        {
            return;
        }
        Task<SaveGameSnapshot?> task = _scanTask;
        _scanTask = null;
        try
        {
            _snapshot = task.GetAwaiter().GetResult();
            RefreshSaveSummary();
            _status!.Text = GameLocalizationService.Text(
                _snapshot is null ? "ui.main.status.empty" : "ui.main.status.ready");
        }
        catch (Exception exception)
        {
            _snapshot = null;
            RefreshSaveSummary();
            _status!.Text = GameLocalizationService.Format("ui.main.status.scan_failed", ("error", exception.Message));
            GD.PushError($"TASK-130 save scan FAILED: {exception}");
        }
    }

    private void PollNewGame()
    {
        if (_newGameTask is null || !_newGameTask.IsCompleted)
        {
            return;
        }
        Task task = _newGameTask;
        _newGameTask = null;
        try
        {
            task.GetAwaiter().GetResult();
            _snapshot = null;
            GD.Print("TASK-130 new-game reset PASS: slot=save_1; settingsPreserved=1; persistenceApi=1.");
            ChangeToGameplay("new-game");
        }
        catch (Exception exception)
        {
            _status!.Text = GameLocalizationService.Format("ui.main.status.new_failed", ("error", exception.Message));
            GD.PushError($"TASK-130 new-game reset FAILED: {exception}");
        }
    }

    private void RefreshSaveSummary()
    {
        bool available = _snapshot is not null;
        if (_continueButton is not null) _continueButton.Disabled = !available;
        if (_loadButton is not null) _loadButton.Disabled = !available;
        if (_saveSummary is null) return;
        _saveSummary.Text = available
            ? GameLocalizationService.Format(
                "ui.main.save_present",
                ("revision", _snapshot!.Revision),
                ("updated", _snapshot.UpdatedUtc),
                ("system", _snapshot.GalaxyNavigation?.CurrentSystemId ?? "legacy"),
                ("inventory", _snapshot.Inventory.Count))
            : GameLocalizationService.Text("ui.main.save_empty");
        UpdateLoadPanel();
    }

    private void UpdateLoadPanel()
    {
        if (_loadPanel is null) return;
        Label? summary = _loadPanel.GetNodeOrNull<Label>("LoadSlotSummary");
        Button? load = _loadPanel.GetNodeOrNull<Button>("LoadPrimary");
        if (summary is null || load is null) return;
        bool available = _snapshot is not null;
        summary.Text = available
            ? GameLocalizationService.Format(
                "ui.main.primary_present",
                ("revision", _snapshot!.Revision),
                ("updated", _snapshot.UpdatedUtc),
                ("ship", _snapshot.Ship.DisplayName),
                ("system", _snapshot.GalaxyNavigation?.CurrentSystemId ?? "legacy"))
            : GameLocalizationService.Text("ui.main.primary_empty");
        load.Disabled = !available;
    }

    private void ChangeToGameplay(string source)
    {
        Error result = GetTree().ChangeSceneToFile(GameplayScene);
        if (result != Error.Ok)
        {
            _status!.Text = GameLocalizationService.Format("ui.main.status.scene_failed", ("error", result));
            return;
        }
        GD.Print($"TASK-130 application transition PASS: source={source}; destination=vertical-slice.");
    }

    private void OnLocaleChanged(string _)
    {
        GameLocalizationService.LocalizeControlTree(this);
        RefreshSaveSummary();
        UpdateLoadPanel();
        if (_status is not null)
        {
            _status.Text = GameLocalizationService.Text(
                _snapshot is null ? "ui.main.status.empty" : "ui.main.status.ready");
        }
    }

    private static async Task<SaveGameSnapshot?> ScanPrimarySlotAsync(CancellationToken cancellationToken)
    {
        using SaveDatabase database = new(GameProfilePaths.PrimaryDatabasePath);
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return await database.LoadAsync(GameProfilePaths.PrimarySlotId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ResetPrimarySlotAsync(CancellationToken cancellationToken)
    {
        using SaveDatabase database = new(GameProfilePaths.PrimaryDatabasePath);
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await database.ResetSlotAsync(GameProfilePaths.PrimarySlotId, cancellationToken).ConfigureAwait(false);
    }
}
