using System;
using System.Linq;
using Godot;

public sealed record DeveloperCommandResult(bool Success, string Output);

public partial class DeveloperDiagnosticsSuite : CanvasLayer
{
    public static readonly string[] RequiredCommands =
    {
        "teleport", "surface_warp", "spawn", "give", "damage", "heal", "set_time", "set_weather",
        "load_system", "load_planet", "show_chunks", "show_navmesh", "show_ai",
        "profile_worldgen", "save", "reload_content"
    };

    private PanelContainer? _panel;
    private RichTextLabel? _history;
    private LineEdit? _input;
    private SalvageRepairSlice? _slice;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        if (!DeveloperToolContext.IsDeveloperModeAllowed())
        {
            QueueFree();
            return;
        }
        _slice = GetParent() as SalvageRepairSlice;
        BuildUi();
        Visible = DeveloperToolContext.OpenConsoleOnGameplay;
        DeveloperToolContext.OpenConsoleOnGameplay = false;
        if (Visible) FocusCommand();
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (key.CtrlPressed && key.ShiftPressed && (key.Keycode == Key.D || key.PhysicalKeycode == Key.D))
        {
            Visible = !Visible;
            if (Visible) FocusCommand();
            GetViewport().SetInputAsHandled();
        }
    }

    private void BuildUi()
    {
        _panel = new PanelContainer
        {
            AnchorLeft = 0.48f,
            AnchorTop = 0.04f,
            AnchorRight = 0.98f,
            AnchorBottom = 0.58f,
            OffsetLeft = 0,
            OffsetTop = 0,
            OffsetRight = 0,
            OffsetBottom = 0
        };
        AddChild(_panel);
        VBoxContainer root = new();
        root.AddThemeConstantOverride("separation", 6);
        _panel.AddChild(root);
        root.AddChild(new Label { Text = "DEVELOPER CONSOLE  •  Ctrl+Shift+D" });
        _history = new RichTextLabel
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SelectionEnabled = true,
            Text = "Project Horizon developer console\nType help for command list.\n"
        };
        root.AddChild(_history);
        _input = new LineEdit { PlaceholderText = "command ..." };
        _input.TextSubmitted += OnSubmitted;
        root.AddChild(_input);
    }

    private void FocusCommand()
    {
        _input?.GrabFocus();
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void OnSubmitted(string commandLine)
    {
        string trimmed = commandLine.Trim();
        _input!.Clear();
        if (string.IsNullOrWhiteSpace(trimmed)) return;
        Append($"> {trimmed}");
        if (string.Equals(trimmed, "help", StringComparison.OrdinalIgnoreCase))
        {
            Append(string.Join("  ", RequiredCommands));
            return;
        }
        if (_slice is null)
        {
            Append("ERROR: vertical slice bridge unavailable");
            return;
        }
        DeveloperCommandResult outcome = _slice.ExecuteDeveloperCommand(trimmed);
        Append((outcome.Success ? "OK: " : "ERROR: ") + outcome.Output);
    }

    private void Append(string text)
    {
        if (_history is null) return;
        _history.Text += text + "\n";
        _history.ScrollToParagraph(Math.Max(0, _history.GetParagraphCount() - 1));
    }
}
