using Godot;

public partial class SalvageRepairSlice
{
    private SystemCapabilitySnapshot? _systemCapabilitySnapshot;
    private string _systemCapabilityAcceptanceHud = "READY";
    private bool? _systemCapabilityAcceptancePassed;

    private void InitializeSystemCapabilityPreflight()
    {
        _systemCapabilitySnapshot = SystemCapabilityDiagnostics.Capture();
        string evidence = SystemCapabilityDiagnostics.BuildEvidence(_systemCapabilitySnapshot);
        GD.Print(evidence);
        StructuredGameLogger.Log(
            _systemCapabilitySnapshot.Evaluation.MinimumSatisfied
                ? GameLogLevel.Information
                : GameLogLevel.Warning,
            GameLogCategory.BOOT,
            evidence);
        GD.Print(
            "TASK-206 system capability READY: " +
            "spec=28.2/28.3; os=Windows10-x64-or-Linux-x86_64; " +
            "cpu=4-min/6-rec; ram=8GiB-min/16GiB-rec; storage=20GiB-min/30GiB-rec; " +
            "renderer=Vulkan-or-OpenGL3-compatibility; gpuMemory=4GiB-min-or-supported-integrated/6GiB-rec-policy; " +
            "ssd=required-but-portable-runtime-detection=unknown; " +
            "recommendation=Low/Medium/Compatibility-advisory; profileMutation=0; F5=acceptance.");
    }

    private void RunSystemCapabilityAcceptance()
    {
        _systemCapabilitySnapshot ??= SystemCapabilityDiagnostics.Capture();
        SystemCapabilityAcceptanceReport report = SystemCapabilityAcceptanceRunner.Evaluate(
            _systemCapabilitySnapshot,
            GameUserSettingsService.Current.GraphicsQualityProfile);
        _systemCapabilityAcceptancePassed = report.Passed;
        _systemCapabilityAcceptanceHud = report.Passed
            ? $"PASS {report.LiveTier} rec={report.LiveRecommendation} min={(report.LiveMinimumSatisfied ? 1 : 0)}"
            : $"FAIL {report.LiveTier} rec={report.LiveRecommendation}";
        string line = report.BuildOutputLine();
        if (report.Passed)
        {
            GD.Print(line);
        }
        else
        {
            GD.PushError(line);
        }
    }
}
