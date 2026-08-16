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
    private string _galaxyMapFeedback = "";
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
        _galaxyMapFeedback = L(saveData is null ? "ui.galaxy.fresh" : "ui.galaxy.restored");
        RefreshGalaxyMapSystems();
        InitializeInterplanetaryTravelRuntime();
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
                CloseGalaxyMap(L("ui.galaxy.closed"));
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
            else if (Matches(physical, logical, Key.Enter))
            {
                if (_galaxyMapSystemTab)
                {
                    ConfirmPlanetaryDestination();
                }
                else
                {
                    ConfirmGalaxyMapDestination();
                }
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
        _galaxyMapFeedback = L(StageOneVoyage.Location ==
                StageOneVoyageLocation.OrbitalStation && StageOneVoyage.Piloted
            ? "ui.galaxy.jump_prompt"
            : "ui.galaxy.jump_requirement");
        _galaxyMapPanel.Visible = true;
        UpdateGalaxyMapPanel();
        _status = L("ui.galaxy.opened");
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


    private void ConfirmPlanetaryDestination()
    {
        IReadOnlyList<GalaxyPlanetDefinition> planets =
            GalaxyNavigation.CurrentSystem.Planets;
        if (planets.Count == 0)
        {
            _galaxyMapFeedback = L("ui.galaxy.no_destination");
            UpdateGalaxyMapPanel();
            return;
        }

        int index = Math.Clamp(_galaxyMapSelection, 0, planets.Count - 1);
        GalaxyPlanetDefinition planet = planets[index];
        SelectInterplanetaryPlanetTarget(planet, out string result);
        _galaxyMapFeedback = result;
        UpdateGalaxyMapPanel();
    }

    private void ConfirmGalaxyMapDestination()
    {
        if (_galaxyMapSystems.Count == 0)
        {
            _galaxyMapFeedback = L("ui.galaxy.no_destination");
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
            _galaxyMapFeedback = LF("ui.galaxy.route_unavailable",
                ("range", stats.HyperdriveRange.ToString("0.#", CultureInfo.InvariantCulture)));
            UpdateGalaxyMapPanel();
            return;
        }

        bool worldTransit = BeginWorldHyperspaceTransit();
        CaptureCurrentPlanetSurfaceState();
        GalaxyTravelActionResult result = GalaxyNavigation.TryJumpToSelected(
            ShipSystems,
            StageOneVoyage.Location,
            out string description);
        _galaxyMapFeedback = description;
        if (result != GalaxyTravelActionResult.Applied)
        {
            if (worldTransit)
            {
                CompleteWorldHyperspaceTransit(successfulJump: false);
            }
            UpdateGalaxyMapPanel();
            return;
        }

        StageOneVoyage.ArriveAtOrbitalStationFromHyperspace();
        // TASK-178: a planet target belongs to one star system. The galaxy jump
        // clears its selection transactionally; synchronize the same-system
        // travel state in the very same success path so no stale source/target
        // can leak into the newly loaded system.
        InterplanetaryTravel.SynchronizeSelection(GalaxyNavigation);
        _stationServicesOpenedFromVoyage = false;
        ActivateCurrentPlanetSurfaceContent();
        if (worldTransit)
        {
            CompleteWorldHyperspaceTransit(successfulJump: true);
        }
        ApplyStageOneVoyageToScene();
        RefreshGalaxyMapSystems();
        PublishDomainEvent(new SystemDiscovered(
            GalaxyNavigation.CurrentSystem.SystemId,
            GalaxyNavigation.VisitedSystemIds.Count,
            DateTimeOffset.UtcNow));
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
            $"planetTargetCleared={(string.IsNullOrWhiteSpace(GalaxyNavigation.SelectedPlanetId) ? 1 : 0)}; " +
            $"interplanetarySync={(InterplanetaryTravel.IsSelectionConsistentWith(GalaxyNavigation) ? 1 : 0)}; " +
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
            string planets = string.Join("\n", current.Planets.Select((planet, index) =>
            {
                string marker = index == _galaxyMapSelection ? ">" : " ";
                string state = string.Equals(
                        planet.PlanetId,
                        GalaxyNavigation.CurrentPlanetId,
                        StringComparison.Ordinal)
                    ? L("ui.galaxy.planet_current")
                    : string.Equals(
                        planet.PlanetId,
                        GalaxyNavigation.SelectedPlanetId,
                        StringComparison.Ordinal)
                        ? L("ui.galaxy.planet_target")
                        : string.Empty;
                string header = $"{marker} " + LF("ui.galaxy.planet_row",
                    ("index", planet.OrbitIndex.ToString("00", CultureInfo.InvariantCulture)),
                    ("archetype", LocalizeGalaxyPlanetArchetype(planet.Archetype)),
                    ("moons", planet.MoonCount),
                    ("atmosphere", planet.HasAtmosphere ? 1 : 0),
                    ("water", planet.HasWater ? 1 : 0)) +
                    (string.IsNullOrWhiteSpace(state) ? string.Empty : $" [{state}]");
                return header + "\n  " +
                    BuildPlanetEnvironmentMapDetail(planet, current.StarType);
            }));
            _galaxyMapLabel.Text = string.Join("\n", new[]
            {
                L("ui.galaxy.system_header"),
                LF("ui.galaxy.system_summary", ("name", current.DisplayName), ("system", current.SystemId), ("galaxy", current.GalaxyId)),
                LF("ui.galaxy.system_detail", ("x", current.SectorX), ("y", current.SectorY), ("z", current.SectorZ), ("star", LocalizeGalaxyStar(current.StarType)), ("economy", LocalizeGalaxyEconomy(current.EconomyType)), ("danger", current.DangerLevel)),
                LF("ui.galaxy.planets", ("count", current.Planets.Count)),
                planets,
                "",
                L("ui.galaxy.system_controls"),
                LF("ui.galaxy.status", ("status", _galaxyMapFeedback))
            });
            return;
        }

        ShipEffectiveStats stats = ShipSystems.GetEffectiveStats();
        string systems = string.Join("\n", _galaxyMapSystems.Select((system, index) =>
        {
            string marker = index == _galaxyMapSelection ? ">" : " ";
            string visited = L(GalaxyNavigation.VisitedSystemIds.Contains(system.SystemId) ? "ui.galaxy.visited" : "ui.galaxy.new");
            double distance = GalaxyNavigationRuntime.Distance(current, system);
            GalaxyRoutePlan route = GalaxyNavigation.PlanRoute(system, stats.HyperdriveRange);
            int jumps = route.Reachable ? Math.Max(0, route.Systems.Count - 1) : -1;
            return $"{marker} " + LF("ui.galaxy.system_row",
                ("name", system.DisplayName), ("x", system.SectorX), ("y", system.SectorY), ("z", system.SectorZ),
                ("star", LocalizeGalaxyStar(system.StarType)), ("distance", distance.ToString("0.0", CultureInfo.InvariantCulture)),
                ("route", jumps < 0 ? "--" : jumps.ToString(CultureInfo.InvariantCulture)), ("visited", visited));
        }));
        _galaxyMapLabel.Text = string.Join("\n", new[]
        {
            L("ui.galaxy.galaxy_header"),
            LF("ui.galaxy.current", ("name", current.DisplayName), ("system", current.SystemId), ("galaxy", current.GalaxyId)),
            LF("ui.galaxy.flight_summary",
                ("range", stats.HyperdriveRange.ToString("0.#", CultureInfo.InvariantCulture)),
                ("ready", ShipSystems.HyperspaceReady ? 1 : 0),
                ("fuel", ShipSystems.Fuel.ToString("0.#", CultureInfo.InvariantCulture)),
                ("capacity", stats.FuelCapacity.ToString("0.#", CultureInfo.InvariantCulture)),
                ("visited", GalaxyNavigation.VisitedSystemIds.Count), ("jumps", GalaxyNavigation.JumpCount)),
            systems,
            "",
            L("ui.galaxy.controls"),
            L("ui.galaxy.jump_requires"),
            LF("ui.galaxy.status", ("status", _galaxyMapFeedback))
        });
    }

    private static string LocalizeGalaxyStar(GalaxyStarType starType)
    {
        string suffix = starType switch
        {
            GalaxyStarType.RedDwarf => "red_dwarf",
            GalaxyStarType.OrangeDwarf => "orange_dwarf",
            GalaxyStarType.YellowStar => "yellow_star",
            GalaxyStarType.WhiteStar => "white_star",
            GalaxyStarType.BlueStar => "blue_star",
            GalaxyStarType.BinaryDecorative => "binary_decorative",
            _ => throw new ArgumentOutOfRangeException(nameof(starType), starType, null)
        };
        return L("ui.galaxy.star." + suffix);
    }

    private static string LocalizeGalaxyEconomy(string economyType)
    {
        string key = "ui.galaxy.economy." + economyType.ToLowerInvariant();
        return GameLocalizationService.ContainsKey(key)
            ? L(key)
            : economyType;
    }

    private static string LocalizeGalaxyPlanetArchetype(string archetype)
    {
        string key = "ui.galaxy.planet." + archetype.ToLowerInvariant();
        return GameLocalizationService.ContainsKey(key)
            ? L(key)
            : archetype;
    }

    private string BuildGalaxyNavigationHudLine()
    {
        if (_galaxyNavigationRuntime is null)
        {
            return L("ui.hud.galaxy.unavailable");
        }
        return LF(
            "ui.hud.galaxy.summary",
            ("system", GalaxyNavigation.CurrentSystem.SystemId),
            ("galaxy", GalaxyNavigation.CurrentSystem.GalaxyId),
            ("jumps", GalaxyNavigation.JumpCount),
            ("visited", GalaxyNavigation.VisitedSystemIds.Count),
            ("range", ShipSystems.GetEffectiveStats().HyperdriveRange.ToString("0.#", CultureInfo.InvariantCulture)),
            ("ready", ShipSystems.HyperspaceReady ? 1 : 0));
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
