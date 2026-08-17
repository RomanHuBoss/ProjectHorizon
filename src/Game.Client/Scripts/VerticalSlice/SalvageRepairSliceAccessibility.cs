using System;
using System.Globalization;
using Godot;

public partial class SalvageRepairSlice
{
    private PanelContainer? _accessibilitySubtitlePanel;
    private Label? _accessibilitySubtitleLabel;
    private Label? _accessibilityStatusCueLabel;
    private double _accessibilitySubtitleRemaining;
    private double _accessibilityStatusRefresh;
    private bool _accessibilityReadyPrinted;
    private string _accessibilityAcceptanceHud = "READY";
    private bool? _accessibilityAcceptancePassed;

    private void InitializeAccessibilityRuntime()
    {
        CanvasLayer? hud = GetNodeOrNull<CanvasLayer>("Hud");
        if (hud is null)
        {
            return;
        }

        _accessibilitySubtitlePanel = new PanelContainer
        {
            Name = "AccessibilitySubtitles",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
            AnchorLeft = 0.18f,
            AnchorTop = 0.78f,
            AnchorRight = 0.82f,
            AnchorBottom = 0.94f,
            OffsetLeft = 0.0f,
            OffsetTop = 0.0f,
            OffsetRight = 0.0f,
            OffsetBottom = 0.0f
        };
        _accessibilitySubtitleLabel = new Label
        {
            Name = "Label",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _accessibilitySubtitlePanel.AddChild(_accessibilitySubtitleLabel);
        hud.AddChild(_accessibilitySubtitlePanel);

        _accessibilityStatusCueLabel = new Label
        {
            Name = "AccessibilityStatusCues",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 0.70f,
            AnchorTop = 0.02f,
            AnchorRight = 0.985f,
            AnchorBottom = 0.20f,
            OffsetLeft = 0.0f,
            OffsetTop = 0.0f,
            OffsetRight = 0.0f,
            OffsetBottom = 0.0f,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        };
        _accessibilityStatusCueLabel.AddThemeFontSizeOverride("font_size", 14);
        hud.AddChild(_accessibilityStatusCueLabel);
        ApplyAccessibilityRuntimeSettings();

        if (!_accessibilityReadyPrinted)
        {
            _accessibilityReadyPrinted = true;
            GameUserSettings settings = GameUserSettingsService.Current;
            GD.Print(
                "TASK-204 accessibility runtime READY: " +
                $"invert=onFoot:{(settings.InvertOnFootX ? 1 : 0)}/{(settings.InvertOnFootY ? 1 : 0)};" +
                $"ship:{(settings.InvertShipPitch ? 1 : 0)}/{(settings.InvertShipYaw ? 1 : 0)}; " +
                $"gamepad=deadZone:{settings.GamepadDeadZone:0.00}/response:{settings.GamepadResponseExponent:0.00}; " +
                $"subtitles={(settings.SubtitlesEnabled ? 1 : 0)}@{settings.SubtitleScale:0.00}; " +
                $"reducedMotion=shake:{(settings.CameraShakeEnabled ? 1 : 0)}/motionBlur:{(settings.MotionBlurEnabled ? 1 : 0)}; " +
                "statusCues=text+token; audio=music+sfx+voice; F5=acceptance.");
        }
    }

    private void ApplyAccessibilityRuntimeSettings()
    {
        GameUserSettings settings = GameUserSettingsService.Current;
        GameAccessibilityRuntime.ApplyInputMap(settings);
        if (_accessibilitySubtitleLabel is not null)
        {
            int fontSize = (int)Math.Round(22.0 * settings.SubtitleScale);
            _accessibilitySubtitleLabel.AddThemeFontSizeOverride("font_size", Math.Clamp(fontSize, 17, 33));
        }
        if (_accessibilitySubtitlePanel is not null && !settings.SubtitlesEnabled)
        {
            _accessibilitySubtitlePanel.Visible = false;
        }
    }

    private void UpdateAccessibilityRuntime(double delta)
    {
        _accessibilitySubtitleRemaining = Math.Max(0.0, _accessibilitySubtitleRemaining - Math.Max(0.0, delta));
        if (_accessibilitySubtitlePanel is not null)
        {
            _accessibilitySubtitlePanel.Visible =
                GameUserSettingsService.Current.SubtitlesEnabled &&
                _accessibilitySubtitleRemaining > 0.0 &&
                !string.IsNullOrWhiteSpace(_accessibilitySubtitleLabel?.Text);
        }

        _accessibilityStatusRefresh -= Math.Max(0.0, delta);
        if (_accessibilityStatusRefresh <= 0.0)
        {
            _accessibilityStatusRefresh = 0.25;
            UpdateAccessibilityStatusCues();
        }
    }

    private void PublishAccessibilityCaption(string localizationKey, double durationSeconds = 2.8)
    {
        if (_accessibilitySubtitleLabel is null || _accessibilitySubtitlePanel is null ||
            !GameUserSettingsService.Current.SubtitlesEnabled)
        {
            return;
        }
        _accessibilitySubtitleLabel.Text = L(localizationKey);
        _accessibilitySubtitleRemaining = Math.Clamp(durationSeconds, 0.5, 8.0);
        _accessibilitySubtitlePanel.Visible = true;
    }

    private void UpdateAccessibilityStatusCues()
    {
        if (_accessibilityStatusCueLabel is null || _playerSurvivalRuntime is null)
        {
            return;
        }
        bool hidden = _hudMode == SalvageRepairHudMode.Hidden;
        _accessibilityStatusCueLabel.Visible = !hidden;
        if (hidden)
        {
            return;
        }

        PlayerSurvivalEffectiveStats stats = PlayerSurvival.GetEffectiveStats();
        static double Ratio(double value, double maximum) =>
            maximum <= 0.0001 ? 0.0 : Math.Clamp(value / maximum, 0.0, 1.0);
        double hp = Ratio(PlayerSurvival.Health, stats.MaximumHealth);
        double shield = Ratio(PlayerSurvival.Shield, stats.MaximumShield);
        double oxygen = Ratio(PlayerSurvival.Oxygen, stats.MaximumOxygen);
        double hazard = Ratio(PlayerSurvival.HazardProtection, stats.MaximumHazardProtection);
        _accessibilityStatusCueLabel.Text = string.Join("\n", new[]
        {
            $"[HP][{AccessibilityControlPolicy.SeverityToken(hp)}] {PlayerSurvival.Health.ToString("0", CultureInfo.InvariantCulture)}/{stats.MaximumHealth.ToString("0", CultureInfo.InvariantCulture)}",
            $"[SH][{AccessibilityControlPolicy.SeverityToken(shield)}] {PlayerSurvival.Shield.ToString("0", CultureInfo.InvariantCulture)}/{stats.MaximumShield.ToString("0", CultureInfo.InvariantCulture)}",
            $"[O2][{AccessibilityControlPolicy.SeverityToken(oxygen)}] {PlayerSurvival.Oxygen.ToString("0", CultureInfo.InvariantCulture)}/{stats.MaximumOxygen.ToString("0", CultureInfo.InvariantCulture)}",
            $"[HZ][{AccessibilityControlPolicy.SeverityToken(hazard)}] {PlayerSurvival.HazardProtection.ToString("0", CultureInfo.InvariantCulture)}/{stats.MaximumHazardProtection.ToString("0", CultureInfo.InvariantCulture)}"
        });
    }

    private void RunAccessibilityAcceptance()
    {
        AccessibilityAcceptanceReport report = AccessibilityAcceptanceRunner.Evaluate(
            GameUserSettingsService.Current,
            _accessibilitySubtitlePanel,
            _accessibilityStatusCueLabel,
            _player,
            _voyageShip);
        _accessibilityAcceptancePassed = report.Passed;
        _accessibilityAcceptanceHud = report.Passed
            ? $"PASS dz={report.DeadZone:0.00} response={report.ResponseExponent:0.00} captions=1 cues=1"
            : "FAIL accessibility runtime contract";
        string output = report.BuildOutputLine();
        if (report.Passed)
        {
            GD.Print(output);
        }
        else
        {
            GD.PushError(output);
        }
    }
}
