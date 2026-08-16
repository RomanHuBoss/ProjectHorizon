using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private PlanetSurfaceContentRuntime? _planetSurfaceContentRuntime;
    private PlanetSurfaceContentProfile? _planetSurfaceContentProfile;
    private readonly Dictionary<string, PlanetaryExplorationSaveData>
        _planetaryExplorationPlanetStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EcologySaveData>
        _ecologyPlanetStates = new(StringComparer.Ordinal);
    private string _planetSurfaceContentAcceptanceHud = "READY";

    private PlanetSurfaceContentProfile PlanetSurfaceContentProfile =>
        _planetSurfaceContentProfile ??
        throw new InvalidOperationException(
            "Planet surface-content profile is unavailable.");

    private void InitializePlanetSurfaceContentArchives(
        PlanetaryExplorationSaveData? planetaryExploration,
        EcologySaveData? ecology)
    {
        _planetaryExplorationPlanetStates.Clear();
        _ecologyPlanetStates.Clear();

        if (planetaryExploration is not null)
        {
            if (planetaryExploration.PlanetStates is not null)
            {
                foreach (PlanetaryExplorationPlanetSaveData state in
                    planetaryExploration.PlanetStates)
                {
                    if (!IsPlanetId(state.PlanetId))
                    {
                        continue;
                    }

                    _planetaryExplorationPlanetStates[state.PlanetId] =
                        new PlanetaryExplorationSaveData(
                            state.WorldSeed,
                            state.RegionKey,
                            state.DiscoveryPoints,
                            state.Pois);
                }
            }

            string rootPlanetId = IsPlanetId(planetaryExploration.PlanetId)
                ? planetaryExploration.PlanetId
                : StarterRepairSnapshotFactory.PlanetId;
            _planetaryExplorationPlanetStates[rootPlanetId] =
                planetaryExploration with
                {
                    PlanetId = "",
                    PlanetStates = null
                };
        }

        if (ecology is not null)
        {
            if (ecology.PlanetStates is not null)
            {
                foreach (EcologyPlanetSaveData state in ecology.PlanetStates)
                {
                    if (!IsPlanetId(state.PlanetId))
                    {
                        continue;
                    }

                    _ecologyPlanetStates[state.PlanetId] = new EcologySaveData(
                        state.WorldSeed,
                        state.RegionKey,
                        state.DiscoveryPoints,
                        state.DiscoveredFloraIds,
                        state.DiscoveredFaunaIds,
                        state.RemovedFloraInstanceIds);
                }
            }

            string rootPlanetId = IsPlanetId(ecology.PlanetId)
                ? ecology.PlanetId
                : StarterRepairSnapshotFactory.PlanetId;
            _ecologyPlanetStates[rootPlanetId] = ecology with
            {
                PlanetId = "",
                PlanetStates = null
            };
        }
    }

    private void ActivateCurrentPlanetSurfaceContent(bool rebuildScene = true)
    {
        _planetSurfaceContentRuntime ??= new PlanetSurfaceContentRuntime(
            PlanetEnvironment,
            EcologyCatalog,
            PlanetaryPoiCatalog);
        GalaxyPlanetDefinition planet = GalaxyNavigation.CurrentPlanet;
        PlanetEnvironmentProfile environmentPreview =
            PlanetEnvironment.BuildProfile(
                planet,
                GalaxyNavigation.CurrentSystem.StarType);
        if (!environmentPreview.Landable)
        {
            GD.Print(
                "TASK-154 planet surface content STANDBY: " +
                $"planet={planet.PlanetId}; archetype={planet.Archetype}; " +
                "reason=non-landable-body; previous surface state preserved.");
            return;
        }

        _planetSurfaceContentProfile = _planetSurfaceContentRuntime.BuildProfile(
            planet,
            GalaxyNavigation.CurrentSystem.StarType);
        EnsurePlanetSurfaceFrameForCurrentPlanet();

        bool legacyStarter = string.Equals(
            planet.PlanetId,
            StarterRepairSnapshotFactory.PlanetId,
            StringComparison.Ordinal);
        long worldSeed = legacyStarter
            ? EcologyCatalog.WorldSeed
            : PlanetSurfaceContentProfile.WorldSeed;
        string ecologyRegion = legacyStarter
            ? EcologyCatalog.RegionKey
            : PlanetSurfaceContentProfile.RegionKey;
        long poiSeed = legacyStarter
            ? PlanetaryPoiCatalog.WorldSeed
            : PlanetSurfaceContentProfile.WorldSeed;
        string poiRegion = legacyStarter
            ? PlanetaryPoiCatalog.RegionKey
            : PlanetSurfaceContentProfile.RegionKey;

        _ecologyPlan = legacyStarter
            ? EcologyPlanner.Plan(EcologyCatalog)
            : _planetSurfaceContentRuntime.BuildEcologyPlan(
                PlanetSurfaceContentProfile);
        _planetaryPoiPlacements = legacyStarter
            ? PlanetaryPoiPlanner.Plan(PlanetaryPoiCatalog)
            : _planetSurfaceContentRuntime.BuildPoiPlan(
                PlanetSurfaceContentProfile);

        _ecologyPlanetStates.TryGetValue(
            planet.PlanetId,
            out EcologySaveData? ecologySave);
        _planetaryExplorationPlanetStates.TryGetValue(
            planet.PlanetId,
            out PlanetaryExplorationSaveData? explorationSave);
        _ecologyRuntime = new EcologyRuntime(
            EcologyCatalog,
            EcologyPlan,
            worldSeed,
            ecologyRegion,
            ecologySave);
        _planetaryExplorationRuntime = new PlanetaryExplorationRuntime(
            PlanetaryPoiCatalog,
            _planetaryPoiPlacements,
            poiSeed,
            poiRegion,
            explorationSave);

        _ecologyCatalogOpen = false;
        _ecologyFaunaTab = false;
        _ecologyCatalogSelection = 0;
        _ecologyFeedback = ecologySave is null
            ? "planet ecology regenerated deterministically"
            : "planet ecology deltas restored";
        if (_ecologyCatalogPanel is not null)
        {
            _ecologyCatalogPanel.Visible = false;
        }

        ApplyPlanetSurfaceTerrain();
        ApplyPlanetSurfacePresentation();
        if (_planetSurfaceWorldCompositionInitialized)
        {
            ApplyPlanetSurfaceWorldComposition();
        }
        SyncPlanetWeatherToActivePlanet();
        if (rebuildScene)
        {
            RebuildEcologyScene();
            RebuildPlanetaryPoiScene();
        }

        GD.Print(
            "TASK-154 planet surface content READY: " +
            $"system={GalaxyNavigation.CurrentSystem.SystemId}; " +
            $"planet={planet.PlanetId}; " +
            $"archetype={PlanetSurfaceContentProfile.Environment.Archetype}; " +
            $"biomes={string.Join(",", PlanetSurfaceContentProfile.ActiveBiomeIds)}; " +
            $"flora={EcologyPlan.Flora.Count}; " +
            $"fauna={EcologyPlan.ActiveFauna.Count}/{EcologyPlan.SimplifiedFauna.Count}; " +
            $"pois={_planetaryPoiPlacements.Count}; " +
            $"waterHabitat={(PlanetSurfaceContentProfile.WaterHabitatEnabled ? 1 : 0)}; " +
            $"habitability={PlanetSurfaceContentProfile.Habitability.ToString("0.00", CultureInfo.InvariantCulture)}; " +
            $"region={PlanetSurfaceContentProfile.RegionKey}.");
    }

    private void CaptureCurrentPlanetSurfaceState()
    {
        if (_planetSurfaceContentProfile is null)
        {
            return;
        }

        string planetId = _planetSurfaceContentProfile.PlanetId;
        if (_planetaryExplorationRuntime is not null)
        {
            _planetaryExplorationPlanetStates[planetId] =
                _planetaryExplorationRuntime.CreateSaveData();
        }

        if (_ecologyRuntime is not null)
        {
            _ecologyPlanetStates[planetId] = _ecologyRuntime.CreateSaveData();
        }
    }

    private PlanetaryExplorationSaveData CreatePlanetaryExplorationArchiveSaveData()
    {
        CaptureCurrentPlanetSurfaceState();
        string currentId = _planetSurfaceContentProfile?.PlanetId ??
            GalaxyNavigation.CurrentPlanetId;
        PlanetaryExplorationSaveData current =
            _planetaryExplorationPlanetStates.TryGetValue(
                currentId,
                out PlanetaryExplorationSaveData? state)
                ? state
                : PlanetaryExploration.CreateSaveData();
        PlanetaryExplorationPlanetSaveData[] archive =
            _planetaryExplorationPlanetStates
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new PlanetaryExplorationPlanetSaveData(
                    pair.Key,
                    pair.Value.WorldSeed,
                    pair.Value.RegionKey,
                    pair.Value.DiscoveryPoints,
                    pair.Value.Pois))
                .ToArray();
        return current with
        {
            PlanetId = currentId,
            PlanetStates = archive
        };
    }

    private EcologySaveData CreateEcologyArchiveSaveData()
    {
        CaptureCurrentPlanetSurfaceState();
        string currentId = _planetSurfaceContentProfile?.PlanetId ??
            GalaxyNavigation.CurrentPlanetId;
        EcologySaveData current = _ecologyPlanetStates.TryGetValue(
                currentId,
                out EcologySaveData? state)
            ? state
            : Ecology.CreateSaveData();
        EcologyPlanetSaveData[] archive = _ecologyPlanetStates
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new EcologyPlanetSaveData(
                pair.Key,
                pair.Value.WorldSeed,
                pair.Value.RegionKey,
                pair.Value.DiscoveryPoints,
                pair.Value.DiscoveredFloraIds,
                pair.Value.DiscoveredFaunaIds,
                pair.Value.RemovedFloraInstanceIds))
            .ToArray();
        return current with
        {
            PlanetId = currentId,
            PlanetStates = archive
        };
    }

    private void ApplyPlanetSurfacePresentation()
    {
        if (_planetSurfaceContentProfile is null)
        {
            return;
        }

        PlanetEnvironmentProfile environment =
            _planetSurfaceContentProfile.Environment;
        Area3D? waterPool = GetNodeOrNull<Area3D>("Gameplay/WaterPool");
        if (waterPool is not null)
        {
            bool enabled = _planetSurfaceContentProfile.WaterHabitatEnabled;
            waterPool.Visible = enabled;
            waterPool.Monitoring = enabled;
            waterPool.Monitorable = enabled;
            MeshInstance3D? waterMesh = waterPool.GetNodeOrNull<MeshInstance3D>(
                "MeshInstance3D");
            if (waterMesh is not null)
            {
                PlanetEnvironmentColor water = environment.WaterColor;
                waterMesh.MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(
                        (float)water.R,
                        (float)water.G,
                        (float)water.B,
                        0.72f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    Roughness = 0.18f
                };
            }
        }

        ApplyPlanetaryWaterProfile(environment);

        WorldEnvironment? world = GetNodeOrNull<WorldEnvironment>(
            "WorldEnvironment");
        if (world?.Environment is not null)
        {
            PlanetEnvironmentColor atmosphere = environment.AtmosphereColor;
            world.Environment.BackgroundColor = new Color(
                (float)(atmosphere.R * 0.32),
                (float)(atmosphere.G * 0.32),
                (float)(atmosphere.B * 0.32),
                1.0f);
            world.Environment.AmbientLightColor = new Color(
                (float)atmosphere.R,
                (float)atmosphere.G,
                (float)atmosphere.B,
                1.0f);
        }
    }

    private string BuildPlanetSurfaceContentHudLine()
    {
        if (_planetSurfaceContentProfile is null || _ecologyPlan is null)
        {
            return L("ui.hud.planet_surface.unavailable");
        }

        return LF(
            "ui.hud.planet_surface.summary",
            ("archetype", LocalizeGalaxyPlanetArchetype(PlanetSurfaceContentProfile.Environment.Archetype)),
            ("biomes", PlanetSurfaceContentProfile.ActiveBiomeIds.Count),
            ("flora", EcologyPlan.Flora.Count),
            ("fauna", EcologyPlan.ActiveFauna.Count),
            ("water", L(PlanetSurfaceContentProfile.WaterHabitatEnabled ? "ui.common.yes" : "ui.common.no")));
    }

    private void RunPlanetSurfaceContentAcceptance()
    {
        PlanetSurfaceContentAcceptanceReport report =
            PlanetSurfaceContentAcceptanceRunner.Run(
                PlanetEnvironmentCatalog,
                EcologyCatalog,
                PlanetaryPoiCatalog);
        _planetSurfaceContentAcceptanceHud = report.BuildHudLine();
        if (report.Passed)
        {
            GD.Print(report.BuildOutputLine());
        }
        else
        {
            GD.PushError(report.BuildOutputLine());
        }
    }

    private static Color BuildGroundColor(string archetype) => archetype switch
    {
        "desert" => new Color(0.54f, 0.36f, 0.19f),
        "frozen" => new Color(0.58f, 0.70f, 0.78f),
        "volcanic" => new Color(0.27f, 0.16f, 0.12f),
        "oceanic" => new Color(0.15f, 0.31f, 0.29f),
        "toxic" => new Color(0.27f, 0.34f, 0.16f),
        "barren" => new Color(0.31f, 0.29f, 0.27f),
        "radioactive" => new Color(0.34f, 0.28f, 0.20f),
        "exotic" => new Color(0.28f, 0.20f, 0.39f),
        _ => new Color(0.32f, 0.43f, 0.26f)
    };

    private static bool IsPlanetId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.StartsWith("planet.", StringComparison.Ordinal) &&
        GameContentCatalog.IsStableId(value);
}
