using Godot;

public partial class SalvageRepairSlice
{
    private string _orbitalHandoffRecoveryAcceptanceHud = "READY";
    private bool? _orbitalHandoffRecoveryAcceptancePassed;

    private void RunOrbitalHandoffRecoveryAcceptance()
    {
        EnsureOrbitalBackdropRuntime();
        OrbitalHandoffRecoveryAcceptanceReport report =
            OrbitalHandoffRecoveryAcceptanceRunner.Run();

        bool starfieldLive = _spaceStarfieldRoot is not null &&
            GodotObject.IsInstanceValid(_spaceStarfieldRoot) &&
            _spaceStarfield is not null &&
            GodotObject.IsInstanceValid(_spaceStarfield);
        bool stationSceneAligned = _orbitalStation is not null &&
            _orbitalDockMarker is not null &&
            _orbitalStation.Position.DistanceTo(new Vector3(
                (float)StageOneVoyageRuntime.StationDockPositionX,
                (float)StageOneVoyageRuntime.StationDockPositionY,
                (float)(StageOneVoyageRuntime.StationDockPositionZ - 31.0))) <= 0.1f &&
            _orbitalDockMarker.Position.DistanceTo(new Vector3(
                (float)StageOneVoyageRuntime.StationDockPositionX,
                (float)StageOneVoyageRuntime.StationDockPositionY,
                (float)StageOneVoyageRuntime.StationDockPositionZ)) <= 0.1f;

        bool passed = report.Passed && starfieldLive && stationSceneAligned;
        _orbitalHandoffRecoveryAcceptancePassed = passed;
        _orbitalHandoffRecoveryAcceptanceHud = passed
            ? report.BuildHudLine()
            : $"FAIL model={(report.Passed ? 1 : 0)} stars={(starfieldLive ? 1 : 0)} station={(stationSceneAligned ? 1 : 0)}";

        string output = report.BuildOutputLine().Replace(
            $"acceptance {(report.Passed ? "PASS" : "FAIL")}:",
            $"acceptance {(passed ? "PASS" : "FAIL")}:") +
            $" liveStarfield={(starfieldLive ? 1 : 0)}; stationScene={(stationSceneAligned ? 1 : 0)}.";
        if (passed)
        {
            GD.Print(output);
        }
        else
        {
            GD.PushError(output);
        }
    }
}
