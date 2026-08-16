using System;
using System.Linq;

public sealed record PlanetarySurfaceSubsystemModelAcceptanceReport(
    bool Passed,
    int StarterPlanets,
    int ContractsPassed,
    int ContractsTotal,
    bool EnvironmentContract,
    bool TravelContract,
    bool ContentContract,
    bool TerrainContract,
    bool StreamingContract,
    bool WorldCompositionContract,
    bool WeatherContract,
    bool FrameContract,
    bool RadialContract,
    bool PhysicalContract,
    bool CurvedContract,
    bool PersistenceChain,
    bool TraversalChain,
    bool BoundedResidency,
    bool CrossPlanetIdentity,
    string Result)
{
    public string BuildSummary() =>
        $"contracts={ContractsPassed}/{ContractsTotal}; persistence={(PersistenceChain ? 1 : 0)}; " +
        $"traversal={(TraversalChain ? 1 : 0)}; bounded={(BoundedResidency ? 1 : 0)}; " +
        $"planetIdentity={(CrossPlanetIdentity ? 1 : 0)}";
}

/// <summary>
/// TASK-176 model-level closure of the complete planetary-surface stack.
/// It deliberately composes the already normative acceptance runners instead of
/// inventing a second implementation of their rules. The live Godot layer adds
/// residency/navigation/player/presentation checks in SalvageRepairSlice.
/// </summary>
public static class PlanetarySurfaceSubsystemAcceptanceRunner
{
    public const int ExpectedContractCount = 11;

    public static PlanetarySurfaceSubsystemModelAcceptanceReport Run(
        GameContentCatalog contentCatalog,
        PlanetEnvironmentCatalog environmentCatalog,
        EcologyCatalog ecologyCatalog,
        PlanetaryPoiCatalog poiCatalog,
        ShipSystemsCatalog shipCatalog,
        CraftingRecipeDefinition repairRecipe,
        params CraftingRecipeDefinition[] stationRecipes)
    {
        ArgumentNullException.ThrowIfNull(contentCatalog);
        ArgumentNullException.ThrowIfNull(environmentCatalog);
        ArgumentNullException.ThrowIfNull(ecologyCatalog);
        ArgumentNullException.ThrowIfNull(poiCatalog);
        ArgumentNullException.ThrowIfNull(shipCatalog);
        ArgumentNullException.ThrowIfNull(repairRecipe);
        stationRecipes ??= Array.Empty<CraftingRecipeDefinition>();

        try
        {
            PlanetEnvironmentAcceptanceReport environment =
                PlanetEnvironmentAcceptanceRunner.Run(
                    environmentCatalog,
                    ecologyCatalog);
            InterplanetaryTravelAcceptanceReport travel =
                InterplanetaryTravelAcceptanceRunner.Run(shipCatalog);
            PlanetSurfaceContentAcceptanceReport content =
                PlanetSurfaceContentAcceptanceRunner.Run(
                    environmentCatalog,
                    ecologyCatalog,
                    poiCatalog);
            PlanetSurfaceTerrainAcceptanceReport terrain =
                PlanetSurfaceTerrainAcceptanceRunner.Run(
                    environmentCatalog,
                    ecologyCatalog,
                    poiCatalog);
            PlanetSurfaceStreamingAcceptanceReport streaming =
                PlanetSurfaceStreamingAcceptanceRunner.Run(
                    environmentCatalog,
                    ecologyCatalog,
                    poiCatalog);
            PlanetSurfaceWorldCompositionAcceptanceReport world =
                PlanetSurfaceWorldCompositionAcceptanceRunner.Run(
                    contentCatalog,
                    environmentCatalog,
                    ecologyCatalog,
                    poiCatalog,
                    repairRecipe,
                    stationRecipes);

            PlanetEnvironmentRuntime environmentRuntime = new(
                environmentCatalog,
                ecologyCatalog);
            GalaxyNavigationRuntime galaxy = new();
            PlanetEnvironmentProfile[] profiles = galaxy.CurrentSystem.Planets
                .Select(planet => environmentRuntime.BuildProfile(
                    planet,
                    galaxy.CurrentSystem.StarType))
                .ToArray();
            PlanetEnvironmentProfile[] landable = profiles
                .Where(profile => profile.Landable)
                .ToArray();

            PlanetWeatherAcceptanceReport weather =
                PlanetWeatherAcceptanceRunner.Run(landable);
            PlanetSurfaceFrameAcceptanceReport frame =
                PlanetSurfaceFrameAcceptanceRunner.Run();
            PlanetSurfaceRadialFrameAcceptanceReport radial =
                PlanetSurfaceRadialFrameAcceptanceRunner.Run(profiles);
            PlanetSurfacePhysicalFrameAcceptanceReport physical =
                PlanetSurfacePhysicalFrameAcceptanceRunner.Run(profiles);
            PlanetSurfaceCurvedCollisionAcceptanceReport curved =
                PlanetSurfaceCurvedCollisionAcceptanceRunner.Run(profiles);

            bool[] contracts =
            {
                environment.Passed,
                travel.Passed,
                content.Passed,
                terrain.Passed,
                streaming.Passed,
                world.Passed,
                weather.Passed,
                frame.Passed,
                radial.Passed,
                physical.Passed,
                curved.Passed
            };
            int contractsPassed = contracts.Count(value => value);

            bool persistenceChain =
                environment.CurrentPlanetRoundTrip &&
                travel.TargetPersistence &&
                travel.TransferPersistence &&
                content.PerPlanetPersistence &&
                world.ColdRestoreDepletion &&
                world.UntouchedDeltaEmpty &&
                weather.SaveRestore &&
                frame.ColdRestoreStable &&
                frame.PlanetResetStable;

            bool traversalChain =
                streaming.TraversalPlans &&
                streaming.PlanetAddressing &&
                frame.RebaseCount >= 8 &&
                frame.LogicalContinuity &&
                frame.ChunkIdentityStable &&
                radial.FaceCoverage &&
                radial.SeamContinuous &&
                radial.WarpRoundTrip &&
                physical.SeamHandoff &&
                physical.WorldLogicalRoundTrip &&
                curved.RebaseContinuity &&
                curved.FacesCovered == 6;

            bool boundedResidency =
                streaming.BoundedResidency &&
                streaming.ActiveChunks == PlanetSurfaceStreamingRuntime.ExpectedActiveChunks &&
                streaming.CollisionChunks == PlanetSurfaceStreamingRuntime.ExpectedCollisionChunks &&
                radial.BoundedGameplayStreamer;

            bool crossPlanetIdentity =
                environment.StarterPlanets == 4 &&
                content.StarterPlanets == 4 &&
                content.DistinctBiomeProfiles == 4 &&
                content.DistinctRegions == 4 &&
                content.PerPlanetPersistence &&
                terrain.StarterPlanets == 4 &&
                streaming.StarterPlanets == 4 &&
                world.StarterPlanets == 4 &&
                world.PlanetScopedIdentity &&
                landable.Length == 4;

            bool passed =
                contractsPassed == ExpectedContractCount &&
                persistenceChain &&
                traversalChain &&
                boundedResidency &&
                crossPlanetIdentity;

            return new PlanetarySurfaceSubsystemModelAcceptanceReport(
                passed,
                environment.StarterPlanets,
                contractsPassed,
                ExpectedContractCount,
                environment.Passed,
                travel.Passed,
                content.Passed,
                terrain.Passed,
                streaming.Passed,
                world.Passed,
                weather.Passed,
                frame.Passed,
                radial.Passed,
                physical.Passed,
                curved.Passed,
                persistenceChain,
                traversalChain,
                boundedResidency,
                crossPlanetIdentity,
                passed
                    ? "planetary environment, travel, surface generation, persistence and spherical physics contracts form one coherent subsystem"
                    : BuildFailureSummary(
                        environment.Passed,
                        travel.Passed,
                        content.Passed,
                        terrain.Passed,
                        streaming.Passed,
                        world.Passed,
                        weather.Passed,
                        frame.Passed,
                        radial.Passed,
                        physical.Passed,
                        curved.Passed,
                        persistenceChain,
                        traversalChain,
                        boundedResidency,
                        crossPlanetIdentity));
        }
        catch (Exception exception)
        {
            return new PlanetarySurfaceSubsystemModelAcceptanceReport(
                false,
                0,
                0,
                ExpectedContractCount,
                false, false, false, false, false, false,
                false, false, false, false, false,
                false, false, false, false,
                $"acceptance exception: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static string BuildFailureSummary(
        bool environment,
        bool travel,
        bool content,
        bool terrain,
        bool streaming,
        bool world,
        bool weather,
        bool frame,
        bool radial,
        bool physical,
        bool curved,
        bool persistence,
        bool traversal,
        bool bounded,
        bool identity)
    {
        (string Name, bool Passed)[] checks =
        {
            ("environment", environment),
            ("travel", travel),
            ("content", content),
            ("terrain", terrain),
            ("streaming", streaming),
            ("world", world),
            ("weather", weather),
            ("frame", frame),
            ("radial", radial),
            ("physical", physical),
            ("curved", curved),
            ("persistence-chain", persistence),
            ("traversal-chain", traversal),
            ("bounded-residency", bounded),
            ("planet-identity", identity)
        };
        string[] failed = checks
            .Where(check => !check.Passed)
            .Select(check => check.Name)
            .ToArray();
        return failed.Length == 0
            ? "unknown planetary-surface subsystem invariant failed"
            : "failed: " + string.Join(",", failed);
    }
}
