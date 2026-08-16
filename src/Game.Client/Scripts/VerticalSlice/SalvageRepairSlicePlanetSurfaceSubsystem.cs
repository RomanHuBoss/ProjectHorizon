using System;
using System.Globalization;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private string _planetSurfaceSubsystemAcceptanceHud = "READY";
    private bool _planetSurfaceSubsystemReadyPrinted;

    private void UpdatePlanetSurfaceSubsystemRuntime()
    {
        if (_planetSurfaceSubsystemReadyPrinted ||
            !_surfaceRuntimeActive ||
            _planetSurfaceStreamer is null ||
            _npcNavigationSurface is null ||
            _player is null ||
            _planetWeatherRuntime is null ||
            _planetSurfaceContentProfile is null ||
            _planetSurfacePhysicalFrameState is null ||
            _planetSurfaceSkyProfile is null ||
            !_planetSurfaceStreamer.IsStreamingSettled)
        {
            return;
        }

        TerrainChunkProfilerSnapshot terrain =
            _planetSurfaceStreamer.CaptureProfilerSnapshot();
        NpcNavigationSurfaceSnapshot nav =
            _npcNavigationSurface.CreateSnapshot();
        if (terrain.LoadedChunks != PlanetSurfaceStreamingRuntime.ExpectedActiveChunks ||
            terrain.Collisions != PlanetSurfaceStreamingRuntime.ExpectedCollisionChunks ||
            !terrain.CurvedSurface ||
            !nav.ReadyForQueries ||
            nav.ActiveRegions != nav.MaximumRegions ||
            !nav.CurvedSurface ||
            !_npcNavigationSurface.ParentFrameAligned ||
            !_player.SurfaceFrameActive ||
            !_planetSurfaceAtmosphereFrameAligned)
        {
            return;
        }

        _planetSurfaceSubsystemReadyPrinted = true;
        GD.Print(
            "TASK-176 planetary surface subsystem READY: " +
            $"planet={GalaxyNavigation.CurrentPlanetId}; contracts=150-174; " +
            $"chunks={terrain.LoadedChunks}/{PlanetSurfaceStreamingRuntime.ExpectedActiveChunks}; " +
            $"collisions={terrain.Collisions}/{PlanetSurfaceStreamingRuntime.ExpectedCollisionChunks}; " +
            $"navRegions={nav.ActiveRegions}/{nav.MaximumRegions}; " +
            "stack=environment+travel+content+terrain+streaming+world+weather+geodesy+radial+physical+curved; " +
            "F5=acceptance.");
    }

    private void RunPlanetSurfaceSubsystemAcceptance()
    {
        if (_planetEnvironmentRuntime is null ||
            _galaxyNavigationRuntime is null ||
            _planetSurfaceContentProfile is null ||
            _starSystemSimulationNode is null ||
            _planetSurfaceStreamer is null ||
            _npcNavigationSurface is null ||
            _player is null)
        {
            _planetSurfaceSubsystemAcceptanceHud = "FAIL unavailable";
            GD.PushError(
                "TASK-176 planetary surface subsystem acceptance FAIL: runtime unavailable");
            return;
        }

        PlanetarySurfaceSubsystemModelAcceptanceReport model =
            PlanetarySurfaceSubsystemAcceptanceRunner.Run(
                ContentCatalog,
                PlanetEnvironmentCatalog,
                EcologyCatalog,
                PlanetaryPoiCatalog,
                ShipSystemsCatalog,
                RepairRecipe,
                StationRecipes.ToArray());

        PlanetEnvironmentProfile[] profiles = GalaxyNavigation.CurrentSystem.Planets
            .Select(planet => PlanetEnvironment.BuildProfile(
                planet,
                GalaxyNavigation.CurrentSystem.StarType))
            .ToArray();
        PlanetaryGlobeAcceptanceReport globe =
            PlanetaryGlobeAcceptanceRunner.Run(
                profiles,
                _starSystemSimulationNode,
                GalaxyNavigation.CurrentPlanetId);

        TerrainChunkProfilerSnapshot terrain =
            _planetSurfaceStreamer.CaptureProfilerSnapshot();
        NpcNavigationSurfaceSnapshot nav = _npcNavigationSurface.CreateSnapshot();
        bool liveStreamer =
            _planetSurfaceStreamer.IsStreamingSettled &&
            terrain.LoadedChunks == PlanetSurfaceStreamingRuntime.ExpectedActiveChunks &&
            terrain.Collisions == PlanetSurfaceStreamingRuntime.ExpectedCollisionChunks &&
            terrain.CurvedSurface;
        bool liveNavigation =
            nav.ReadyForQueries &&
            nav.ActiveRegions == nav.MaximumRegions &&
            nav.CurvedSurface &&
            _npcNavigationSurface.ParentFrameAligned;

        Vector3 expectedUp = GetCurrentPlanetCurvedWorldUp();
        bool livePlayer =
            _surfaceRuntimeActive &&
            !StageOneVoyage.Piloted &&
            _player.SurfaceFrameActive &&
            _player.ActiveSurfaceUp.Normalized().Dot(expectedUp) >= 0.999f &&
            _player.GlobalTransform.Basis.Y.Normalized().Dot(expectedUp) >= 0.999f;
        bool livePresentation =
            _planetSurfaceWorldCompositionInitialized &&
            _planetSurfaceSkyProfile is not null &&
            _planetSurfaceAtmosphereFrameAligned &&
            _planetSurfaceDistantTerrain is not null &&
            GodotObject.IsInstanceValid(_planetSurfaceDistantTerrain) &&
            _planetSurfaceResourceRoot is not null &&
            GodotObject.IsInstanceValid(_planetSurfaceResourceRoot) &&
            _lastSurfaceResourceCenter is not null;
        bool liveContent =
            _planetaryPoiNodes.Count == PlanetaryPoiCatalog.ExpectedPoiTypeCount &&
            _ecologyFaunaNodes.Count > 0 &&
            _ecologyPlan is not null &&
            _ecologyPlan.Flora.Count > 0;
        bool coldStartSafety =
            _planetSurfaceFallbackBackfaceCollisionEnabled &&
            _planetSurfaceStartupClearanceSamples > 0 &&
            double.IsFinite(_planetSurfaceStartupMinimumClearanceMeters) &&
            _planetSurfaceStartupMinimumClearanceMeters >= 0.79;
        bool liveWeather = _planetWeatherRuntime is not null &&
            double.IsFinite(_planetWeatherRuntime.GameHours);
        bool liveRadialStack =
            _planetRadialSurfaceState is not null &&
            _planetSurfacePhysicalFrameState is { } physicalFrame &&
            physicalFrame.WorldUp.Dot(expectedUp) > 0.995f;

        bool passed =
            model.Passed &&
            globe.Passed &&
            liveStreamer &&
            liveNavigation &&
            livePlayer &&
            livePresentation &&
            liveContent &&
            coldStartSafety &&
            liveWeather &&
            liveRadialStack;

        _planetSurfaceSubsystemAcceptanceHud = passed
            ? $"PASS contracts={model.ContractsPassed}/{model.ContractsTotal} live=8/8 globe=1"
            : $"FAIL contracts={model.ContractsPassed}/{model.ContractsTotal} " +
              $"stream={(liveStreamer ? 1 : 0)} nav={(liveNavigation ? 1 : 0)} " +
              $"player={(livePlayer ? 1 : 0)} sky={(livePresentation ? 1 : 0)}";

        string output =
            $"TASK-176 planetary surface subsystem acceptance {(passed ? "PASS" : "FAIL")}: " +
            $"starterPlanets={model.StarterPlanets}/4; " +
            $"contracts={model.ContractsPassed}/{model.ContractsTotal}; " +
            $"environment={(model.EnvironmentContract ? 1 : 0)}; " +
            $"travel={(model.TravelContract ? 1 : 0)}; content={(model.ContentContract ? 1 : 0)}; " +
            $"terrain={(model.TerrainContract ? 1 : 0)}; streaming={(model.StreamingContract ? 1 : 0)}; " +
            $"world={(model.WorldCompositionContract ? 1 : 0)}; weather={(model.WeatherContract ? 1 : 0)}; " +
            $"frame={(model.FrameContract ? 1 : 0)}; radial={(model.RadialContract ? 1 : 0)}; " +
            $"physical={(model.PhysicalContract ? 1 : 0)}; curved={(model.CurvedContract ? 1 : 0)}; " +
            $"persistenceChain={(model.PersistenceChain ? 1 : 0)}; " +
            $"traversalChain={(model.TraversalChain ? 1 : 0)}; bounded={(model.BoundedResidency ? 1 : 0)}; " +
            $"planetIdentity={(model.CrossPlanetIdentity ? 1 : 0)}; globe={(globe.Passed ? 1 : 0)}; " +
            $"liveStreamer={(liveStreamer ? 1 : 0)}; liveNav={(liveNavigation ? 1 : 0)}; " +
            $"livePlayer={(livePlayer ? 1 : 0)}; livePresentation={(livePresentation ? 1 : 0)}; " +
            $"liveContent={(liveContent ? 1 : 0)}; coldStart={(coldStartSafety ? 1 : 0)}; " +
            $"liveWeather={(liveWeather ? 1 : 0)}; liveRadial={(liveRadialStack ? 1 : 0)}; " +
            $"chunks={terrain.LoadedChunks}/{PlanetSurfaceStreamingRuntime.ExpectedActiveChunks}; " +
            $"collisions={terrain.Collisions}/{PlanetSurfaceStreamingRuntime.ExpectedCollisionChunks}; " +
            $"navRegions={nav.ActiveRegions}/{nav.MaximumRegions}; " +
            $"minGuard={_planetSurfaceStartupMinimumClearanceMeters.ToString("0.000", CultureInfo.InvariantCulture)}m; " +
            $"result={(passed ? "planetary surface stack closed as one coherent runtime subsystem" : model.Result)}";

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
