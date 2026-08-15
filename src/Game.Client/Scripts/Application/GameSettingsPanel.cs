using System;
using System.Collections.Generic;
using Godot;

public partial class GameSettingsPanel : PanelContainer
{
    private readonly Dictionary<string, Button> _bindingButtons = new(StringComparer.Ordinal);
    private readonly List<Action> _refreshers = new();
    private GameUserSettings _working = new();
    private VBoxContainer? _content;
    private Label? _status;
    private string? _captureAction;

    public event Action? CloseRequested;
    public event Action<GameUserSettings>? Applied;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Stop;
        BuildUi();
        ReloadFromService();
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

        Key selected = key.PhysicalKeycode != Key.None
            ? key.PhysicalKeycode
            : key.Keycode;
        if (selected == Key.Escape)
        {
            CancelCapture("Rebind cancelled.");
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
        SetStatus($"{PrettyAction(completed)} → {selected}. Press Apply to save.");
        GetViewport().SetInputAsHandled();
    }

    public void ReloadFromService()
    {
        _working = GameUserSettingsService.Current.Clone();
        RefreshUiFromWorkingCopy();
        SetStatus("Settings are stored separately from the gameplay save slot.");
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
        Label title = new()
        {
            Text = "SETTINGS",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 30);
        root.AddChild(title);

        Label scope = new()
        {
            Text = "Controls • Accessibility • Audio",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        scope.AddThemeFontSizeOverride("font_size", 15);
        root.AddChild(scope);
        root.AddChild(new HSeparator());

        ScrollContainer scroll = new()
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        root.AddChild(scroll);
        _content = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _content.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_content);

        BuildPointerAndAccessibility(_content);
        BuildKeyboardBindings(_content);
        BuildAudio(_content);

        _status = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 34)
        };
        root.AddChild(_status);

        HBoxContainer buttons = new()
        {
            Alignment = BoxContainer.AlignmentMode.End
        };
        buttons.AddThemeConstantOverride("separation", 10);
        root.AddChild(buttons);
        Button defaults = NewButton("Restore Defaults");
        defaults.Pressed += RestoreDefaults;
        buttons.AddChild(defaults);
        Button apply = NewButton("Apply");
        apply.Pressed += ApplySettings;
        buttons.AddChild(apply);
        Button close = NewButton("Close");
        close.Pressed += () => CloseRequested?.Invoke();
        buttons.AddChild(close);
    }

    private void BuildPointerAndAccessibility(VBoxContainer root)
    {
        root.AddChild(SectionLabel("LOOK & ACCESSIBILITY"));
        AddSlider(
            root,
            "On-foot mouse sensitivity",
            0.0005,
            0.0100,
            0.0005,
            () => _working.OnFootMouseSensitivity,
            value => _working.OnFootMouseSensitivity = (float)value,
            value => value.ToString("0.0000"));
        AddSlider(
            root,
            "Ship mouse sensitivity",
            0.0005,
            0.0150,
            0.0005,
            () => _working.ShipMouseSensitivity,
            value => _working.ShipMouseSensitivity = (float)value,
            value => value.ToString("0.0000"));
        AddSlider(
            root,
            "Field of view",
            60,
            110,
            1,
            () => _working.FieldOfViewDegrees,
            value => _working.FieldOfViewDegrees = (float)value,
            value => $"{value:0}°");
        AddSlider(
            root,
            "UI scale",
            0.8,
            1.5,
            0.05,
            () => _working.UiScale,
            value => _working.UiScale = (float)value,
            value => $"{value:0.00}×");

        AddCheck(root, "Invert on-foot horizontal look", () => _working.InvertOnFootX, v => _working.InvertOnFootX = v);
        AddCheck(root, "Invert on-foot vertical look", () => _working.InvertOnFootY, v => _working.InvertOnFootY = v);
        AddCheck(root, "Invert ship pitch", () => _working.InvertShipPitch, v => _working.InvertShipPitch = v);
        AddCheck(root, "Invert ship yaw", () => _working.InvertShipYaw, v => _working.InvertShipYaw = v);
        AddCheck(root, "Subtitles", () => _working.SubtitlesEnabled, v => _working.SubtitlesEnabled = v);
        AddCheck(root, "Camera shake", () => _working.CameraShakeEnabled, v => _working.CameraShakeEnabled = v);
        AddCheck(root, "Motion blur", () => _working.MotionBlurEnabled, v => _working.MotionBlurEnabled = v);
        root.AddChild(new Label
        {
            Text = "Color-coded shell states are also identified by text/icon labels.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        });
    }

    private void BuildKeyboardBindings(VBoxContainer root)
    {
        root.AddChild(new HSeparator());
        root.AddChild(SectionLabel("ON-FOOT KEYBOARD"));
        AddBinding(root, "move_forward");
        AddBinding(root, "move_backward");
        AddBinding(root, "move_left");
        AddBinding(root, "move_right");
        AddBinding(root, "jump");
        AddBinding(root, "interact");
        AddBinding(root, "player_sprint");
        AddBinding(root, "player_crouch");

        root.AddChild(SectionLabel("SHIP KEYBOARD"));
        AddBinding(root, "ship_forward");
        AddBinding(root, "ship_reverse");
        AddBinding(root, "ship_strafe_left");
        AddBinding(root, "ship_strafe_right");
        AddBinding(root, "ship_lift_up");
        AddBinding(root, "ship_lift_down");
        AddBinding(root, "ship_roll_left");
        AddBinding(root, "ship_roll_right");
        AddBinding(root, "ship_pitch_up");
        AddBinding(root, "ship_pitch_down");
        AddBinding(root, "ship_yaw_left");
        AddBinding(root, "ship_yaw_right");
        AddBinding(root, "ship_boost");
        AddBinding(root, "ship_brake");
        AddBinding(root, "ship_camera");
        AddBinding(root, "ship_stabilize");

        root.AddChild(SectionLabel("SYSTEM"));
        AddBinding(root, "pause");
        AddBinding(root, "planet_map");
        root.AddChild(new Label
        {
            Text = "Standard gamepad mappings remain active independently of keyboard rebinding.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        });
    }

    private void BuildAudio(VBoxContainer root)
    {
        root.AddChild(new HSeparator());
        root.AddChild(SectionLabel("AUDIO ACCESSIBILITY"));
        AddSlider(root, "Music", 0, 1, 0.05, () => _working.MusicVolume, v => _working.MusicVolume = (float)v, Percent);
        AddSlider(root, "Effects", 0, 1, 0.05, () => _working.EffectsVolume, v => _working.EffectsVolume = (float)v, Percent);
        AddSlider(root, "Speech", 0, 1, 0.05, () => _working.SpeechVolume, v => _working.SpeechVolume = (float)v, Percent);
    }

    private void AddBinding(VBoxContainer root, string action)
    {
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 12);
        Label label = new()
        {
            Text = PrettyAction(action),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.AddChild(label);
        Button button = NewButton("Unbound");
        button.CustomMinimumSize = new Vector2(180, 38);
        button.Pressed += () => BeginCapture(action);
        row.AddChild(button);
        _bindingButtons[action] = button;
        root.AddChild(row);
    }

    private void AddCheck(
        VBoxContainer root,
        string text,
        Func<bool> getter,
        Action<bool> setter)
    {
        CheckButton check = new()
        {
            Text = text,
            ButtonPressed = getter()
        };
        check.Toggled += setter;
        _refreshers.Add(() => check.ButtonPressed = getter());
        root.AddChild(check);
    }

    private void AddSlider(
        VBoxContainer root,
        string text,
        double min,
        double max,
        double step,
        Func<double> getter,
        Action<double> setter,
        Func<double, string> formatter)
    {
        VBoxContainer block = new();
        block.AddThemeConstantOverride("separation", 2);
        HBoxContainer caption = new();
        Label label = new() { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill };
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
        slider.ValueChanged += value =>
        {
            setter(value);
            valueLabel.Text = formatter(value);
        };
        _refreshers.Add(() =>
        {
            double value = getter();
            slider.Value = value;
            valueLabel.Text = formatter(value);
        });
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

    private void BeginCapture(string action)
    {
        _captureAction = action;
        if (_bindingButtons.TryGetValue(action, out Button? button))
        {
            button.Text = "PRESS A KEY…";
        }
        SetStatus($"Rebinding {PrettyAction(action)}. Press Esc to cancel.");
    }

    private void CancelCapture(string message)
    {
        if (_captureAction is not null && _bindingButtons.TryGetValue(_captureAction, out Button? button))
        {
            button.Text = GameUserSettingsService.DescribeKey(_captureAction, _working);
        }
        _captureAction = null;
        SetStatus(message);
    }

    private void RestoreDefaults()
    {
        _working = new GameUserSettings();
        RefreshUiFromWorkingCopy();
        SetStatus("Default controls and accessibility values restored locally. Press Apply to save.");
    }

    private void ApplySettings()
    {
        Error error = GameUserSettingsService.SaveAndApply(_working);
        if (error != Error.Ok)
        {
            SetStatus($"Settings save FAILED: {error}.");
            return;
        }
        Applied?.Invoke(GameUserSettingsService.Current);
        SetStatus("Settings saved and applied.");
    }

    private void SetStatus(string text)
    {
        if (_status is not null)
        {
            _status.Text = text;
        }
    }

    private static Label SectionLabel(string text)
    {
        Label label = new() { Text = text };
        label.AddThemeFontSizeOverride("font_size", 19);
        return label;
    }

    private static Button NewButton(string text) => new()
    {
        Text = text,
        CustomMinimumSize = new Vector2(150, 40)
    };

    private static string Percent(double value) => $"{value * 100.0:0}%";

    private static string PrettyAction(string action) => action switch
    {
        "move_forward" => "Move forward",
        "move_backward" => "Move backward",
        "move_left" => "Move left",
        "move_right" => "Move right",
        "jump" => "Jump / jetpack",
        "interact" => "Interact",
        "player_sprint" => "Sprint",
        "player_crouch" => "Crouch / swim down",
        "ship_forward" => "Thrust forward",
        "ship_reverse" => "Thrust reverse",
        "ship_strafe_left" => "Strafe left",
        "ship_strafe_right" => "Strafe right",
        "ship_lift_up" => "Lift up",
        "ship_lift_down" => "Lift down",
        "ship_roll_left" => "Roll left",
        "ship_roll_right" => "Roll right",
        "ship_pitch_up" => "Pitch up",
        "ship_pitch_down" => "Pitch down",
        "ship_yaw_left" => "Yaw left",
        "ship_yaw_right" => "Yaw right",
        "ship_boost" => "Boost",
        "ship_brake" => "Brake",
        "ship_camera" => "Switch camera",
        "ship_stabilize" => "Stabilization toggle",
        "pause" => "Pause / menu",
        "planet_map" => "Planet map",
        _ => action
    };
}
