using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;

public partial class SalvageRepairSlice
{
    private GalaxyNavigationRuntime? _galaxyNavigationRuntime;
    private PanelContainer? _galaxyMapPanel;
    private Label? _galaxyMapLabel;
    private bool _galaxyMapOpen;
    private bool _galaxyMapSystemTab;
    private int _galaxyMapSelection;
    private IReadOnlyList<GalaxySystemDefinition> _galaxyMapSystems =
        Array.Empty<GalaxySystemDefinition>();
    private string _galaxyMapFeedback = "select a destination";
    private Task<GalaxyNavigationAcceptanceReport>?
        _galaxyNavigationAcceptanceTask;
    private GalaxyNavigationAcceptanceReport?
        _galaxyNavigationAcceptanceReport;
    private string _galaxyNavigationAcceptanceHud = "READY";

    private GalaxyNavigationRuntime GalaxyNavigation =>
        _galaxyNavigationRuntime ??
        throw new InvalidOperationException(
            "Galaxy navigation runtime is unavailable.");

    private void BindGalaxyNavigationSceneNodes()
    {
        _galaxyMapPanel = GetNodeOrNull<PanelContainer>("Hud/GalaxyMap");
        _galaxyMapLabel = GetNodeOrNull<Label>("Hud/GalaxyMap/Label");
        if (_galaxyMapPanel is null || _galaxyMapLabel is null)
        {
            throw new InvalidOperationException(
                "Vertical slice scene is missing the galaxy map panel.");
        }
    }

    private void InitializeGalaxyNavigationRuntime(
        GalaxyNavigationSaveData? saveData)
    {
        _galaxyNavigationRuntime = new GalaxyNavigationRuntime(saveData);
        _galaxyMapOpen = false;
        _galaxyMapSystemTab = false;
        _galaxyMapSelection = 0;
        _galaxyMapFeedback = saveData is null
            ? "legacy/fresh save: starter system selected"
            : "galaxy navigation restored";
        RefreshGalaxyMapSystems();
        if (_galaxyMapPanel is not null)
        {
            _galaxyMapPanel.Visible = false;
        }
    }

    private bool HandleGalaxyNavigationInput(Key physical, Key logical)
    {
        if (_galaxyMapOpen)
        {
            if (Matches(physical, logical, Key.Escape) ||
                Matches(physical, logical, Key.M))
            {
                CloseGalaxyMap("galaxy map closed");
            }
            else if (Matches(physical, logical, Key.Tab))
            {
                _galaxyMapSystemTab = !_galaxyMapSystemTab;
                _galaxyMapSelection = 0;
                UpdateGalaxyMapPanel();
            }
            else if (Matches(physical, logical, Key.Up))
            {
                MoveGalaxyMapSelection(-1);
            }
            else if (Matches(physical, logical, Key.Down))
            {
                MoveGalaxyMapSelection(1);
            }
            else if (Matches(physical, logical, Key.Enter) &&
                !_galaxyMapSystemTab)
            {
                ConfirmGalaxyMapDestination();
            }

            return true;
        }

        if (Matches(physical, logical, Key.M) &&
            (_state == SalvageRepairSliceState.Ready ||
             _state == SalvageRepairSliceState.Passed))
        {
            OpenGalaxyMap();
            return true;
        }

        return false;
    }

    private void OpenGalaxyMap()
    {
        if (_galaxyMapPanel is null || _galaxyMapLabel is null)
        {
            return;
        }

        CloseRecipeSelector();
        CloseStationServices();
        CloseBaseBuildMode();
        CloseDiscoveryCatalog();
        CloseShipManagement();
        _galaxyMapOpen = true;
        _galaxyMapSystemTab = false;
        _galaxyMapSelection = 0;
        RefreshGalaxyMapSystems();
        _galaxyMapFeedback = StageOneVoyage.Location ==
                StageOneVoyageLocation.OrbitalStation &&
            StageOneVoyage.Piloted
            ? "select a system and press Enter to jump"
            : "map available; hyperspace requires a piloted ship docked at an orbital station";
        _galaxyMapPanel.Visible = true;
        UpdateGalaxyMapPanel();
        _status = "galaxy map opened";
    }

    private void CloseGalaxyMap(string status = "")
    {
        _galaxyMapOpen = false;
        if (_galaxyMapPanel is not null)
        {
            _galaxyMapPanel.Visible = false;
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            _status = status;
        }
    }

    private void RefreshGalaxyMapSystems()
    {
        if (_galaxyNavigationRuntime is null)
        {
            _galaxyMapSystems = Array.Empty<GalaxySystemDefinition>();
            return;
        }

        List<GalaxySystemDefinition> systems = GalaxyNavigation
            .GetNearbySystems(radius: 2, maximumCount: 25)
            .ToList();
        GalaxySystemDefinition? selected = GalaxyNavigation.SelectedDestination;
        if (selected is not null && !string.Equals(
                selected.SystemId,
                GalaxyNavigation.CurrentSystem.SystemId,
                StringComparison.Ordinal) &&
            systems.All(system => !string.Equals(
                system.SystemId,
                selected.SystemId,
                StringComparison.Ordinal)))
        {
            systems.Insert(0, selected);
        }

        _galaxyMapSystems = systems;
        if (_galaxyMapSystems.Count == 0)
        {
            _galaxyMapSelection = 0;
            return;
        }

        int selectedIndex = selected is null
            ? -1
            : systems.FindIndex(system => string.Equals(
                system.SystemId,
                selected.SystemId,
                StringComparison.Ordinal));
        _galaxyMapSelection = selectedIndex >= 0
            ? selectedIndex
            : Math.Clamp(
                _galaxyMapSelection,
                0,
                _galaxyMapSystems.Count - 1);
    }

    private void MoveGalaxyMapSelection(int delta)
    {
        int count = _galaxyMapSystemTab
            ? GalaxyNavigation.CurrentSystem.Planets.Count
            : _galaxyMapSystems.Count;
        if (count == 0)
        {
            return;
        }

        _galaxyMapSelection = (_galaxyMapSelection + delta + count) % count;
        UpdateGalaxyMapPanel();
    }

    private void ConfirmGalaxyMapDestination()
    {
        if (_galaxyMapSystems.Count == 0)
        {
            _galaxyMapFeedback = "no generated destination is available";
            UpdateGalaxyMapPanel();
            return;
        }

        GalaxySystemDefinition destination =
            _galaxyMapSystems[_galaxyMapSelection];
        GalaxyNavigation.SelectDestination(destination);
        ShipEffectiveStats stats = ShipSystems.GetEffectiveStats();
        GalaxyRoutePlan route = GalaxyNavigation.PlanRoute(
            destination,
            stats.HyperdriveRange);
        if (!route.Reachable)
        {
            _galaxyMapFeedback =
                $"route unavailable within {stats.HyperdriveRange:0.#} ly range";
            UpdateGalaxyMapPanel();
            return;
        }

        GalaxyTravelActionResult result = GalaxyNavigation.TryJumpToSelected(
            ShipSystems,
            StageOneVoyage.Location,
            out string description);
        _galaxyMapFeedback = description;
        if (result != GalaxyTravelActionResult.Applied)
        {
            UpdateGalaxyMapPanel();
            return;
        }

        StageOneVoyage.ArriveAtOrbitalStationFromHyperspace();
        _stationServicesOpenedFromVoyage = false;
        ApplyStageOneVoyageToScene();
        RefreshGalaxyMapSystems();
        QueueCurrentSnapshot(AutosaveTrigger.Hyperspace);
        GD.Print(
            "TASK-114 player hyperspace jump PASS: " +
            $"galaxy={GalaxyNavigation.CurrentSystem.GalaxyId}; " +
            $"system={GalaxyNavigation.CurrentSystem.SystemId}; " +
            $"sector={GalaxyNavigation.CurrentSystem.SectorX}," +
            $"{GalaxyNavigation.CurrentSystem.SectorY}," +
            $"{GalaxyNavigation.CurrentSystem.SectorZ}; " +
            $"star={GalaxyNavigation.CurrentSystem.StarType}; " +
            $"planets={GalaxyNavigation.CurrentSystem.Planets.Count}; " +
            $"jumps={GalaxyNavigation.JumpCount}; " +
            $"visited={GalaxyNavigation.VisitedSystemIds.Count}; " +
            $"fuel={ShipSystems.Fuel.ToString("0.###", CultureInfo.InvariantCulture)}; " +
            $"result={description}");
        UpdateGalaxyMapPanel();
    }

    private void UpdateGalaxyMapPanel()
    {
        if (!_galaxyMapOpen || _galaxyMapLabel is null)
        {
            return;
        }

        GalaxySystemDefinition current = GalaxyNavigation.CurrentSystem;
        if (_galaxyMapSystemTab)
        {
            string planets = string.Join(
                "\n",
                current.Planets.Select((planet, index) =>
                {
                    string marker = index == _galaxyMapSelection ? ">" : " ";
                    return $"{marker} {planet.OrbitIndex:00} " +
                        $"{planet.Archetype,-12} moons={planet.MoonCount} " +
                        $"atmosphere={(planet.HasAtmosphere ? 1 : 0)} " +
                        $"water={(planet.HasWater ? 1 : 0)}";
                }));
            _galaxyMapLabel.Text =
                "SYSTEM MAP [Tab: Galaxy]\n" +
                $"{current.DisplayName} • {current.SystemId} • {current.GalaxyId}\n" +
                $"sector={current.SectorX},{current.SectorY},{current.SectorZ} • " +
                $"star={current.StarType} • economy={current.EconomyType} • " +
                $"danger={current.DangerLevel}\n" +
                $"Planets {current.Planets.Count}/8:\n{planets}\n\n" +
                "Up/Down select • Tab galaxy • M/Esc close\n" +
                $"Status: {_galaxyMapFeedback}";
            return;
        }

        ShipEffectiveStats stats = ShipSystems.GetEffectiveStats();
        string systems = string.Join(
            "\n",
            _galaxyMapSystems.Select((system, index) =>
            {
                string marker = index == _galaxyMapSelection ? ">" : " ";
                string visited = GalaxyNavigation.VisitedSystemIds.Contains(
                    system.SystemId)
                    ? "VISITED"
                    : "NEW";
                double distance = GalaxyNavigationRuntime.Distance(
                    current,
                    system);
                GalaxyRoutePlan route = GalaxyNavigation.PlanRoute(
                    system,
                    stats.HyperdriveRange);
                int jumps = route.Reachable
                    ? Math.Max(0, route.Systems.Count - 1)
                    : -1;
                return $"{marker} {system.DisplayName,-20} " +
                    $"[{system.SectorX,2},{system.SectorY,2},{system.SectorZ,2}] " +
                    $"{system.StarType,-16} {distance,6:0.0}ly " +
                    $"route={(jumps < 0 ? "--" : jumps.ToString(CultureInfo.InvariantCulture))} " +
                    $"{visited}";
            }));
        _galaxyMapLabel.Text =
            "GALAXY MAP [Tab: System]\n" +
            $"Current: {current.DisplayName} • {current.SystemId} • {current.GalaxyId}\n" +
            $"hyperdriveRange={stats.HyperdriveRange:0.#}ly • " +
            $"hyperReady={(ShipSystems.HyperspaceReady ? 1 : 0)} • " +
            $"fuel={ShipSystems.Fuel:0.#}/{stats.FuelCapacity:0.#} • " +
            $"visited={GalaxyNavigation.VisitedSystemIds.Count} • " +
            $"jumps={GalaxyNavigation.JumpCount}\n" +
            systems + "\n\n" +
            "Up/Down select • Enter route/jump • Tab system • M/Esc close\n" +
            "Jump requires: commissioned + flight-ready + hyperdrive module + orbital dock\n" +
            $"Status: {_galaxyMapFeedback}";
    }

    private string BuildGalaxyNavigationHudLine()
    {
        return _galaxyNavigationRuntime is null
            ? "Galaxy navigation: unavailable"
            : "Galaxy navigation: " + GalaxyNavigation.BuildSummary() +
              $"; range={ShipSystems.GetEffectiveStats().HyperdriveRange:0.#}ly; " +
              $"ready={(ShipSystems.HyperspaceReady ? 1 : 0)}; map=M";
    }

    private void BeginGalaxyNavigationAcceptance(string directory)
    {
        string testPath = System.IO.Path.Combine(
            directory,
            "save_1.galaxy-navigation-test.db");
        _galaxyNavigationAcceptanceHud = "RUNNING";
        _galaxyNavigationAcceptanceReport = null;
        _galaxyNavigationAcceptanceTask =
            GalaxyNavigationAcceptanceRunner.RunAsync(
                testPath,
                SlotId,
                ShipSystemsCatalog,
                RepairRecipe,
                _lifetimeCancellation.Token);
    }

    private void PollGalaxyNavigationAcceptanceTask()
    {
        if (_galaxyNavigationAcceptanceTask is null ||
            !_galaxyNavigationAcceptanceTask.IsCompleted)
        {
            return;
        }

        Task<GalaxyNavigationAcceptanceReport> task =
            _galaxyNavigationAcceptanceTask;
        _galaxyNavigationAcceptanceTask = null;
        try
        {
            GalaxyNavigationAcceptanceReport report =
                task.GetAwaiter().GetResult();
            _galaxyNavigationAcceptanceReport = report;
            _galaxyNavigationAcceptanceHud = report.Passed
                ? $"PASS deterministic={(report.DeterministicGeneration ? 1 : 0)}, " +
                  $"stars={(report.StarCoverage ? 1 : 0)}, " +
                  $"route={(report.RoutePlanning ? 1 : 0)}, " +
                  $"jump={(report.HyperspaceJump ? 1 : 0)}, " +
                  $"stress100={(report.Stress100 ? 1 : 0)}, " +
                  $"restore={(report.ColdRestore ? 1 : 0)}"
                : $"FAIL {report.Result}";
            _status = report.Result;
            string output =
                "TASK-114 galaxy navigation acceptance " +
                (report.Passed ? "PASS" : "FAIL") + ": " +
                $"deterministic={(report.DeterministicGeneration ? 1 : 0)}; " +
                $"coordinates={(report.CoordinateHierarchy ? 1 : 0)}; " +
                $"starCoverage={(report.StarCoverage ? 1 : 0)}; " +
                $"planetBounds={(report.PlanetBounds ? 1 : 0)}; " +
                $"routePlanning={(report.RoutePlanning ? 1 : 0)}; " +
                $"preconditions={(report.Preconditions ? 1 : 0)}; " +
                $"hyperspaceJump={(report.HyperspaceJump ? 1 : 0)}; " +
                $"fuelDebited={(report.FuelDebited ? 1 : 0)}; " +
                $"visitedPersistence={(report.VisitedPersistence ? 1 : 0)}; " +
                $"stress100={(report.Stress100 ? 1 : 0)}; " +
                $"coldRestore={(report.ColdRestore ? 1 : 0)}; " +
                $"legacyFallback={(report.LegacyFallback ? 1 : 0)}; " +
                $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}; " +
                $"logWritten={(report.LogWritten ? 1 : 0)}; " +
                $"maxWriters={report.Diagnostics.MaximumConcurrentWriters}; " +
                $"integrity={report.Diagnostics.IntegrityResult}; " +
                $"elapsedMs={report.ElapsedMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)}; " +
                $"result={report.Result}";
            if (report.Passed)
            {
                GD.Print(output);
            }
            else
            {
                GD.PushError(output);
            }

            UpdateCombinedCatalogAndShipAcceptanceState();
        }
        catch (Exception exception)
        {
            Fail("galaxy navigation acceptance", exception);
        }
    }
}
