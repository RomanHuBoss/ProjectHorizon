using System;
using System.Collections.Generic;
using Godot;

public partial class GameSettingsPanel : PanelContainer
{
    private readonly Dictionary<string, Button> _bindingButtons = new(StringComparer.Ordinal);
    private readonly List<Action> _refreshers = new();
    private GameUserSettings _working = new();
    private Label? _status;
    private OptionButton? _languageOption;
    private string? _captureAction;

    public event Action? CloseRequested;
    public event Action<GameUserSettings>? Applied;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Stop;
        GameLocalizationService.EnsureInitialized();
        GameLocalizationService.LocaleChanged += OnLocaleChanged;
        BuildUi();
        ReloadFromService();
        RefreshLocalization();
    }

    public override void _ExitTree()
    {
        GameLocalizationService.LocaleChanged -= OnLocaleChanged;
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (!Visible)
        {
            return;
        }
        if (_captureAction is null)
        {
            if (inputEvent.IsActionPressed("pause"))
            {
                CloseRequested?.Invoke();
                GetViewport().SetInputAsHandled();
            }
            return;
        }
        if (inputEvent is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }

        Key selected = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
        if (selected == Key.Escape)
        {
            CancelCapture("ui.settings.status.cancelled");
            GetViewport().SetInputAsHandled();
            return;
        }
        if (selected == Key.None)
        {
            return;
        }

        _working.KeyboardBindings[_captureAction] = (long)selected;
        if (_bindingButtons.TryGetValue(_captureAction, out Button? button))
        {
            button.Text = selected.ToString();
        }
        string completed = _captureAction;
        _captureAction = null;
        SetStatus(GameLocalizationService.Format(
            "ui.settings.status.rebound",
            ("action", PrettyAction(completed)),
            ("key", selected)));
        GetViewport().SetInputAsHandled();
    }

    public void ReloadFromService()
    {
        _working = GameUserSettingsService.Current.Clone();
        RefreshUiFromWorkingCopy();
        SetStatus(GameLocalizationService.Text("ui.settings.status.separate"));
    }

    private void BuildUi()
    {
        CustomMinimumSize = new Vector2(860.0f, 650.0f);
        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 20);
        AddChild(margin);

        VBoxContainer root = new();
        root.AddThemeConstantOverride("separation", 12);
        margin.AddChild(root);
        Label title = new() { Text = "ui.settings.title", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 30);
        root.AddChild(title);
        Label scope = new() { Text = "ui.settings.scope", HorizontalAlignment = HorizontalAlignment.Center };
        scope.AddThemeFontSizeOverride("font_size", 15);
        root.AddChild(scope);
        root.AddChild(new HSeparator());

        ScrollContainer scroll = new()
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        root.AddChild(scroll);
        VBoxContainer content = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(content);

        BuildLanguage(content);
        BuildPointerAndAccessibility(content);
        BuildKeyboardBindings(content);
        BuildAudio(content);

        _status = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 34)
        };
        root.AddChild(_status);

        HBoxContainer buttons = new() { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 10);
        root.AddChild(buttons);
        Button defaults = NewButton("ui.common.restore_defaults");
        defaults.Pressed += RestoreDefaults;
        buttons.AddChild(defaults);
        Button apply = NewButton("ui.common.apply");
        apply.Pressed += ApplySettings;
        buttons.AddChild(apply);
        Button close = NewButton("ui.common.close");
        close.Pressed += () => CloseRequested?.Invoke();
        buttons.AddChild(close);
    }

    private void BuildLanguage(VBoxContainer root)
    {
        root.AddChild(SectionLabel("ui.settings.language_section"));
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 12);
        row.AddChild(new Label
        {
            Text = "ui.settings.language",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center
        });
        _languageOption = new OptionButton { CustomMinimumSize = new Vector2(220, 40) };
        _languageOption.AddItem("ui.settings.language.auto");
        _languageOption.AddItem("ui.settings.language.en");
        _languageOption.AddItem("ui.settings.language.ru");
        _languageOption.ItemSelected += index => _working.LanguageCode = index switch
        {
            1 => GameLocalizationService.EnglishLocale,
            2 => GameLocalizationService.RussianLocale,
            _ => GameLocalizationService.AutomaticLanguage
        };
        row.AddChild(_languageOption);
        root.AddChild(row);
        _refreshers.Add(RefreshLanguageSelection);
    }

    private void BuildPointerAndAccessibility(VBoxContainer root)
    {
        root.AddChild(new HSeparator());
        root.AddChild(SectionLabel("ui.settings.look_section"));
        AddSlider(root, "ui.settings.onfoot_sensitivity", 0.0005, 0.0100, 0.0005,
            () => _working.OnFootMouseSensitivity, value => _working.OnFootMouseSensitivity = (float)value,
            value => value.ToString("0.0000"));
        AddSlider(root, "ui.settings.ship_sensitivity", 0.0005, 0.0150, 0.0005,
            () => _working.ShipMouseSensitivity, value => _working.ShipMouseSensitivity = (float)value,
            value => value.ToString("0.0000"));
        AddSlider(root, "ui.settings.fov", 60, 110, 1,
            () => _working.FieldOfViewDegrees, value => _working.FieldOfViewDegrees = (float)value,
            value => $"{value:0}°");
        AddSlider(root, "ui.settings.ui_scale", 0.8, 1.5, 0.05,
            () => _working.UiScale, value => _working.UiScale = (float)value,
            value => $"{value:0.00}×");

        AddCheck(root, "ui.settings.invert_onfoot_x", () => _working.InvertOnFootX, v => _working.InvertOnFootX = v);
        AddCheck(root, "ui.settings.invert_onfoot_y", () => _working.InvertOnFootY, v => _working.InvertOnFootY = v);
        AddCheck(root, "ui.settings.invert_ship_pitch", () => _working.InvertShipPitch, v => _working.InvertShipPitch = v);
        AddCheck(root, "ui.settings.invert_ship_yaw", () => _working.InvertShipYaw, v => _working.InvertShipYaw = v);
        AddCheck(root, "ui.settings.subtitles", () => _working.SubtitlesEnabled, v => _working.SubtitlesEnabled = v);
        AddCheck(root, "ui.settings.camera_shake", () => _working.CameraShakeEnabled, v => _working.CameraShakeEnabled = v);
        AddCheck(root, "ui.settings.motion_blur", () => _working.MotionBlurEnabled, v => _working.MotionBlurEnabled = v);
        root.AddChild(new Label { Text = "ui.settings.color_note", AutowrapMode = TextServer.AutowrapMode.WordSmart });
    }

    private void BuildKeyboardBindings(VBoxContainer root)
    {
        root.AddChild(new HSeparator());
        root.AddChild(SectionLabel("ui.settings.onfoot_keyboard"));
        foreach (string action in new[] { "move_forward", "move_backward", "move_left", "move_right", "jump", "interact", "player_sprint", "player_crouch" })
        {
            AddBinding(root, action);
        }
        root.AddChild(SectionLabel("ui.settings.ship_keyboard"));
        foreach (string action in new[] { "ship_forward", "ship_reverse", "ship_strafe_left", "ship_strafe_right", "ship_lift_up", "ship_lift_down", "ship_roll_left", "ship_roll_right", "ship_pitch_up", "ship_pitch_down", "ship_yaw_left", "ship_yaw_right", "ship_boost", "ship_brake", "ship_camera", "ship_stabilize" })
        {
            AddBinding(root, action);
        }
        root.AddChild(SectionLabel("ui.settings.system"));
        AddBinding(root, "pause");
        AddBinding(root, "planet_map");
        root.AddChild(new Label { Text = "ui.settings.gamepad_note", AutowrapMode = TextServer.AutowrapMode.WordSmart });
    }

    private void BuildAudio(VBoxContainer root)
    {
        root.AddChild(new HSeparator());
        root.AddChild(SectionLabel("ui.settings.audio"));
        AddSlider(root, "ui.settings.music", 0, 1, 0.05, () => _working.MusicVolume, v => _working.MusicVolume = (float)v, Percent);
        AddSlider(root, "ui.settings.effects", 0, 1, 0.05, () => _working.EffectsVolume, v => _working.EffectsVolume = (float)v, Percent);
        AddSlider(root, "ui.settings.speech", 0, 1, 0.05, () => _working.SpeechVolume, v => _working.SpeechVolume = (float)v, Percent);
    }

    private void AddBinding(VBoxContainer root, string action)
    {
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 12);
        row.AddChild(new Label
        {
            Text = ActionKey(action),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center
        });
        Button button = NewButton("ui.common.unbound");
        button.CustomMinimumSize = new Vector2(180, 38);
        button.Pressed += () => BeginCapture(action);
        row.AddChild(button);
        _bindingButtons[action] = button;
        root.AddChild(row);
    }

    private void AddCheck(VBoxContainer root, string key, Func<bool> getter, Action<bool> setter)
    {
        CheckButton check = new() { Text = key, ButtonPressed = getter() };
        check.Toggled += toggledOn => setter(toggledOn);
        _refreshers.Add(() => check.ButtonPressed = getter());
        root.AddChild(check);
    }

    private void AddSlider(VBoxContainer root, string key, double min, double max, double step,
        Func<double> getter, Action<double> setter, Func<double, string> formatter)
    {
        VBoxContainer block = new();
        block.AddThemeConstantOverride("separation", 2);
        HBoxContainer caption = new();
        Label label = new() { Text = key, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Label valueLabel = new() { Text = formatter(getter()), HorizontalAlignment = HorizontalAlignment.Right };
        caption.AddChild(label);
        caption.AddChild(valueLabel);
        block.AddChild(caption);
        HSlider slider = new()
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = getter(),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        slider.ValueChanged += value => { setter(value); valueLabel.Text = formatter(value); };
        _refreshers.Add(() => { double value = getter(); slider.Value = value; valueLabel.Text = formatter(value); });
        block.AddChild(slider);
        root.AddChild(block);
    }

    private void RefreshUiFromWorkingCopy()
    {
        foreach (Action refresher in _refreshers)
        {
            refresher();
        }
        foreach ((string action, Button button) in _bindingButtons)
        {
            button.Text = GameUserSettingsService.DescribeKey(action, _working);
        }
    }

    private void RefreshLanguageSelection()
    {
        if (_languageOption is null)
        {
            return;
        }
        _languageOption.Selected = _working.LanguageCode switch
        {
            GameLocalizationService.EnglishLocale => 1,
            GameLocalizationService.RussianLocale => 2,
            _ => 0
        };
    }

    private void RefreshLocalization()
    {
        GameLocalizationService.LocalizeControlTree(this);
        if (_languageOption is not null)
        {
            _languageOption.SetItemText(0, GameLocalizationService.Text("ui.settings.language.auto"));
            _languageOption.SetItemText(1, GameLocalizationService.Text("ui.settings.language.en"));
            _languageOption.SetItemText(2, GameLocalizationService.Text("ui.settings.language.ru"));
        }
        RefreshUiFromWorkingCopy();
    }

    private void OnLocaleChanged(string _) => RefreshLocalization();

    private void BeginCapture(string action)
    {
        _captureAction = action;
        if (_bindingButtons.TryGetValue(action, out Button? button))
        {
            button.Text = GameLocalizationService.Text("ui.common.press_key");
        }
        SetStatus(GameLocalizationService.Format("ui.settings.status.rebinding", ("action", PrettyAction(action))));
    }

    private void CancelCapture(string messageKey)
    {
        if (_captureAction is not null && _bindingButtons.TryGetValue(_captureAction, out Button? button))
        {
            button.Text = GameUserSettingsService.DescribeKey(_captureAction, _working);
        }
        _captureAction = null;
        SetStatus(GameLocalizationService.Text(messageKey));
    }

    private void RestoreDefaults()
    {
        _working = new GameUserSettings();
        RefreshUiFromWorkingCopy();
        SetStatus(GameLocalizationService.Text("ui.settings.status.defaults"));
    }

    private void ApplySettings()
    {
        Error error = GameUserSettingsService.SaveAndApply(_working);
        if (error != Error.Ok)
        {
            SetStatus(GameLocalizationService.Format("ui.settings.status.save_failed", ("error", error)));
            return;
        }
        _working = GameUserSettingsService.Current.Clone();
        RefreshLocalization();
        Applied?.Invoke(GameUserSettingsService.Current);
        SetStatus(GameLocalizationService.Text("ui.settings.status.saved"));
    }

    private void SetStatus(string text)
    {
        if (_status is not null)
        {
            _status.Text = text;
        }
    }

    private static Label SectionLabel(string key)
    {
        Label label = new() { Text = key };
        label.AddThemeFontSizeOverride("font_size", 19);
        return label;
    }

    private static Button NewButton(string key) => new() { Text = key, CustomMinimumSize = new Vector2(150, 40) };
    private static string Percent(double value) => $"{value * 100.0:0}%";
    private static string ActionKey(string action) => $"ui.action.{action}";
    private static string PrettyAction(string action) => GameLocalizationService.ContainsKey(ActionKey(action))
        ? GameLocalizationService.Text(ActionKey(action))
        : action;
}
