using Godot;

public partial class SalvageRepairSlice
{
    private string _crossPlatformDeterminismAcceptanceHud = "READY";
    private bool? _crossPlatformDeterminismAcceptancePassed;
    private bool _crossPlatformDeterminismReadyPrinted;

    private void PrintCrossPlatformDeterminismReady()
    {
        if (_crossPlatformDeterminismReadyPrinted)
        {
            return;
        }

        _crossPlatformDeterminismReadyPrinted = true;
        GD.Print(
            "TASK-212 cross-platform determinism/offline READY: " +
            "platforms=Windows-x64+Linux-x64; golden=shared-v1; " +
            "generatorVersion=3-version-bump-required; culture=en-US/ru-RU/tr-TR; " +
            "canonicalSignature=SHA256-invariant; CI=windows+linux-golden-matrix; " +
            "singlePlayerNetworkRequired=0; cloudFeatures=optional; " +
            "productionNetworkDependencies=0-static-audit; F5=acceptance.");
    }

    private void RunCrossPlatformDeterminismAcceptance()
    {
        CrossPlatformDeterminismReport report =
            CrossPlatformDeterminismAcceptanceRunner.Run(
                PlanetEnvironment,
                PlanetaryPoiCatalog);
        _crossPlatformDeterminismAcceptancePassed = report.Passed;
        _crossPlatformDeterminismAcceptanceHud = report.Passed
            ? $"PASS v={report.GeneratorVersion} culture={report.CulturesTested} offline=1"
            : $"FAIL v={report.GeneratorVersion} signature={report.CanonicalSignature[..8]}";
        if (report.Passed)
        {
            GD.Print(report.BuildOutputLine());
        }
        else
        {
            GD.PushError(report.BuildOutputLine());
        }
    }
}
