using System;
using System.Linq;
using System.Text.Json;
using Godot;

public partial class SalvageRepairSlice
{
    private string _task138AcceptanceHud = "READY";

    private void InitializeTestingRuntime()
    {
        GD.Print(
            "TASK-138 verification suite READY: unitGroups=10; saveScenarios=8; " +
            "loadScenarios=8; goldenVersion=" + ProjectHorizonGenerator.Version +
            "; coverage=Domain>=80/WorldGen>=70/Persistence>=80; runner=tools\\run-section36-tests.cmd.");
    }

    private static bool SmokePackedScene(string resourcePath)
    {
        PackedScene? packed = GD.Load<PackedScene>(resourcePath);
        if (packed is null)
        {
            return false;
        }

        Node? instance = packed.Instantiate();
        if (instance is null)
        {
            return false;
        }

        instance.Free();
        return true;
    }

    private bool RunSequentialLandingProbe(out int completedLoops)
    {
        ShipSystemsRuntime ship = new(ShipSystemsCatalog, commissioned: true);
        StageOneVoyageRuntime voyage = new();
        completedLoops = 0;
        for (int index = 0; index < 100; index++)
        {
            ship.Refuel(100000.0);
            if (voyage.TryBoard(ship, out _) != StageOneVoyageActionResult.Applied ||
                voyage.TryLaunch(ship, out _) != StageOneVoyageActionResult.Applied)
            {
                return false;
            }

            voyage.UpdateFlightState(
                StageOneVoyageRuntime.StationDockPositionX,
                StageOneVoyageRuntime.StationDockPositionY,
                StageOneVoyageRuntime.StationDockPositionZ,
                0.0, 0.0, 0.0, 0.0, 0.0, 0.0);
            if (voyage.TryDock(ship, 0.0, 0.0, out _) !=
                    StageOneVoyageActionResult.Applied ||
                voyage.TryUndock(ship, out _) != StageOneVoyageActionResult.Applied)
            {
                return false;
            }

            voyage.UpdateFlightState(
                StageOneVoyageRuntime.SurfacePositionX,
                StageOneVoyageRuntime.LaunchPositionY,
                StageOneVoyageRuntime.SurfacePositionZ,
                0.0, 0.0, 0.0, 0.0, 0.0, 0.0);
            if (voyage.TryLand(ship, 0.0, 0.0, out _) !=
                    StageOneVoyageActionResult.Applied ||
                voyage.TryDisembark(out _) != StageOneVoyageActionResult.Applied)
            {
                return false;
            }

            completedLoops = voyage.CompletedLoops;
        }

        return completedLoops == 100 && voyage.LandingCount == 100 &&
            voyage.DockingCount == 100 && voyage.TakeoffCount == 100;
    }

    private void RunTestingArchitectureAcceptance()
    {
        _task138AcceptanceHud = "RUNNING";
        try
        {
            string goldenJson = Godot.FileAccess.GetFileAsString(
                "res://Testing/golden-seeds.v1.json");
            string suiteJson = Godot.FileAccess.GetFileAsString(
                "res://Testing/section36-suite.json");
            GoldenSeedManifest golden = GoldenSeedContract.LoadFromJson(goldenJson);
            using JsonDocument suite = JsonDocument.Parse(suiteJson);
            JsonElement root = suite.RootElement;
            int unitGroups = root.GetProperty("unitGroups").GetArrayLength();
            int saveScenarios = root.GetProperty("saveScenarios").GetArrayLength();
            int loadScenarios = root.GetProperty("loadScenarios").GetArrayLength();
            JsonElement coverage = root.GetProperty("coverage");
            bool suiteContract = unitGroups == 10 && saveScenarios == 8 &&
                loadScenarios == 8 &&
                Math.Abs(coverage.GetProperty("domain").GetDouble() - 0.80) < 0.0001 &&
                Math.Abs(coverage.GetProperty("worldGen").GetDouble() - 0.70) < 0.0001 &&
                Math.Abs(coverage.GetProperty("persistence").GetDouble() - 0.80) < 0.0001 &&
                root.GetProperty("visualSmokeRequired").GetBoolean();

            bool version = golden.GeneratorVersion == ProjectHorizonGenerator.Version &&
                GalaxyNavigationRuntime.GeneratorVersion == ProjectHorizonGenerator.Version;
            int systemsPassed = 0;
            string systemMismatch = string.Empty;
            foreach (GoldenSystemCase testCase in golden.SystemCases)
            {
                if (!GoldenSeedContract.VerifySystemCase(testCase, out string mismatch))
                {
                    systemMismatch = mismatch;
                    break;
                }
                systemsPassed++;
            }
            bool poi = GoldenSeedContract.VerifyPoiFixture(
                golden.PoiFixture,
                PlanetaryPoiCatalog,
                out string poiMismatch);

            CubeSphereBuildData first = CubeSphereMeshBuilder.Build(
                17, 30.0f, 2.5f, 0.015f, 138001);
            CubeSphereBuildData second = CubeSphereMeshBuilder.Build(
                17, 30.0f, 2.5f, 0.015f, 138001);
            bool worldgenVisualSmoke = first.TotalVertices == second.TotalVertices &&
                first.TotalTriangles == second.TotalTriangles &&
                first.SeamComparisons == first.ExpectedSeamComparisons &&
                first.MaximumSeamPositionError <= 0.0001f &&
                first.MaximumSeamNormalError <= 0.0001f &&
                first.Faces.SelectMany(face => face.Vertices)
                    .Zip(second.Faces.SelectMany(face => face.Vertices))
                    .All(pair => pair.First.IsEqualApprox(pair.Second));
            bool visualComponentSmoke = GetNodeOrNull<CanvasLayer>("Hud") is not null &&
                SmokePackedScene("res://Scenes/UI/MainMenu.tscn") &&
                SmokePackedScene("res://Scenes/Developer/DeveloperWorkbench.tscn") &&
                SmokePackedScene("res://Scenes/Planet/CubeSpherePrototype.tscn") &&
                SmokePackedScene("res://Scenes/Ship/ShipFlightPrototype.tscn");
            bool visualSmoke = worldgenVisualSmoke && visualComponentSmoke;

            bool landingStress = RunSequentialLandingProbe(
                out int completedLandings);

            bool passed = suiteContract && version &&
                systemsPassed == golden.SystemCases.Count && poi && visualSmoke &&
                landingStress;
            _task138AcceptanceHud = passed ? "PASS" : "FAIL";
            string result =
                $"TASK-138 verification suite acceptance {(passed ? "PASS" : "FAIL")}: " +
                $"generatorVersion={ProjectHorizonGenerator.Version}; " +
                $"goldenSystems={systemsPassed}/{golden.SystemCases.Count}; " +
                $"goldenPoi={(poi ? 1 : 0)}; controlHeights={(poi ? 1 : 0)}; " +
                $"checksums={(systemsPassed == golden.SystemCases.Count && poi ? 1 : 0)}; " +
                $"unitGroups={unitGroups}/10; saveScenarios={saveScenarios}/8; " +
                $"loadScenarios={loadScenarios}/8; landingStress={completedLandings}/100; " +
                $"visualSmoke={(visualSmoke ? 1 : 0)}; visualComponents={(visualComponentSmoke ? 1 : 0)}; " +
                $"coverageThresholds={(suiteContract ? "80/70/80" : "invalid")}; " +
                $"systemMismatch={systemMismatch}; poiMismatch={poiMismatch}; " +
                "runner=tools/run-section36-tests.cmd; result=section-36-verification-runtime.";
            GD.Print(result);
            if (!passed)
            {
                GD.PushError(result);
            }
        }
        catch (Exception exception)
        {
            _task138AcceptanceHud = "FAIL";
            GD.PushError($"TASK-138 verification suite acceptance FAIL: {exception}");
        }
    }
}
