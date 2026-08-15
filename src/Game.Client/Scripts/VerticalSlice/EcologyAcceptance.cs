using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public sealed record EcologyAcceptanceReport(
    bool Passed,
    string Result,
    int Biomes,
    int FloraModules,
    int FaunaArchetypes,
    bool MovementCoverage,
    bool BodyPlanCoverage,
    bool BehaviorCoverage,
    bool DeterministicPlacement,
    bool FloraInstancing,
    bool PopulationLimits,
    bool UpdateTiers,
    bool BehaviorRuntime,
    bool DiscoveryLifecycle,
    bool RegionDeltaOnly,
    bool Stress16Biomes,
    bool ColdRestore,
    bool LegacyFallback,
    bool ExactRoundTrip,
    bool LogWritten,
    SaveDatabaseDiagnostics Diagnostics,
    double ElapsedMilliseconds);

public static class EcologyAcceptanceRunner
{
    public static async Task<EcologyAcceptanceReport> RunAsync(
        string databasePath,
        string slotId,
        EcologyCatalog catalog,
        CraftingRecipeDefinition repairRecipe,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(repairRecipe);
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            DeleteTestArtifacts(databasePath);
            EcologyPlan left = EcologyPlanner.Plan(catalog);
            EcologyPlan right = EcologyPlanner.Plan(catalog);
            bool deterministicPlacement = string.Equals(
                JsonSerializer.Serialize(left),
                JsonSerializer.Serialize(right),
                StringComparison.Ordinal);

            int ground = catalog.Fauna.Values.Count(fauna =>
                fauna.MovementMode == "Ground");
            int flying = catalog.Fauna.Values.Count(fauna =>
                fauna.MovementMode == "Flying");
            int aquatic = catalog.Fauna.Values.Count(fauna =>
                fauna.MovementMode == "Aquatic");
            bool movementCoverage =
                ground == EcologyCatalog.ExpectedGroundFaunaCount &&
                flying == EcologyCatalog.ExpectedFlyingFaunaCount &&
                aquatic == EcologyCatalog.ExpectedAquaticFaunaCount;
            bool bodyPlanCoverage = catalog.Fauna.Values
                .Select(fauna => fauna.BodyPlan)
                .Distinct(StringComparer.Ordinal)
                .Count() == 6;
            string[] expectedBehaviors =
            {
                "Idle", "Wander", "Graze", "Drink", "Sleep", "Investigate",
                "Flee", "Threaten", "Attack", "ReturnToTerritory", "FollowGroup"
            };
            bool behaviorCoverage = expectedBehaviors.All(behavior =>
                catalog.Fauna.Values.Any(fauna =>
                    fauna.Behaviors.Contains(
                        behavior,
                        StringComparer.Ordinal)));

            bool floraInstancing =
                left.Flora.Count == EcologyPlanner.GameplayFloraInstanceCount &&
                left.Flora.Select(item => item.FloraId)
                    .Distinct(StringComparer.Ordinal).Count() <=
                    EcologyCatalog.ExpectedFloraCount &&
                left.Flora.All(item =>
                    EcologyPlanner.HasInfrastructureClearance(
                        item.PositionX,
                        item.PositionZ));
            bool populationLimits =
                left.ActiveFauna.Count == catalog.ActiveFaunaLimit &&
                left.SimplifiedFauna.Count == catalog.SimplifiedFaunaLimit &&
                left.ActiveFauna.All(spawn => !spawn.Simplified) &&
                left.SimplifiedFauna.All(spawn => spawn.Simplified);
            bool updateTiers =
                Math.Abs(EcologyRuntime.GetUpdateFrequencyHz(8.0) - 10.0) <
                    0.001 &&
                Math.Abs(EcologyRuntime.GetUpdateFrequencyHz(35.0) - 2.0) <
                    0.001 &&
                Math.Abs(EcologyRuntime.GetUpdateFrequencyHz(80.0)) < 0.001;

            EcologyFaunaDefinition attacker = catalog.Fauna.Values.First(
                fauna => fauna.Aggression >= 0.60 &&
                    fauna.Behaviors.Contains("Attack", StringComparer.Ordinal));
            EcologyFaunaDefinition grazer = catalog.Fauna.Values.First(
                fauna => fauna.Diet == "Herbivore" &&
                    fauna.Behaviors.Contains("Graze", StringComparer.Ordinal));
            EcologyFaunaDefinition follower = catalog.Fauna.Values.First(
                fauna => fauna.Behaviors.Contains(
                    "FollowGroup",
                    StringComparer.Ordinal));
            bool behaviorRuntime =
                EcologyRuntime.SelectBehavior(
                    attacker,
                    new EcologyBehaviorContext(
                        4.0, 0.2, 0.2, 0.2, 2.0, 1.0, false, true)) ==
                    "Attack" &&
                EcologyRuntime.SelectBehavior(
                    grazer,
                    new EcologyBehaviorContext(
                        30.0, 0.9, 0.2, 0.2, 2.0, 1.0, false, false)) ==
                    "Graze" &&
                EcologyRuntime.SelectBehavior(
                    follower,
                    new EcologyBehaviorContext(
                        30.0, 0.1, 0.1, 0.1, 20.0, 1.0, false, false)) ==
                    "FollowGroup" &&
                EcologyRuntime.SelectBehavior(
                    grazer,
                    new EcologyBehaviorContext(
                        5.0, 0.1, 0.1, 0.1, 2.0, 1.0, false, true)) ==
                    "Flee" &&
                EcologyRuntime.SelectBehavior(
                    grazer,
                    new EcologyBehaviorContext(
                        30.0, 0.1, 0.1, 0.1, 2.0, 200.0, false, false)) ==
                    "ReturnToTerritory";

            bool stress16Biomes = true;
            foreach (EcologyBiomeDefinition biome in catalog.Biomes.Values)
            {
                for (int iteration = 0; iteration < 8; iteration++)
                {
                    var placements = EcologyPlanner.PlanBiome(
                        catalog,
                        biome.BiomeId,
                        (iteration + 1) * 7919L,
                        24);
                    stress16Biomes &= placements.Count == 24 &&
                        placements.All(placement =>
                            string.Equals(
                                placement.BiomeId,
                                biome.BiomeId,
                                StringComparison.Ordinal) &&
                            catalog.GetFlora(placement.FloraId).BiomeIds.Contains(
                                biome.BiomeId,
                                StringComparer.Ordinal));
                }
            }

            EcologyRuntime runtime = new(catalog, left);
            EcologyFloraPlacement floraPlacement = left.Flora[0];
            EcologyFaunaSpawn faunaSpawn = left.ActiveFauna[0];
            bool floraScanned = runtime.TryScanFlora(
                floraPlacement.InstanceId,
                out _,
                out _);
            bool faunaScanned = runtime.TryScanFauna(
                faunaSpawn.InstanceId,
                out _,
                out _);
            bool harvested = runtime.TryHarvestFlora(
                floraPlacement.InstanceId,
                out _,
                out _);
            bool duplicateHarvestRejected = !runtime.TryHarvestFlora(
                floraPlacement.InstanceId,
                out _,
                out _);
            bool discoveryLifecycle = floraScanned && faunaScanned &&
                harvested && duplicateHarvestRejected &&
                runtime.DiscoveredFloraCount == 1 &&
                runtime.DiscoveredFaunaCount == 1 &&
                runtime.RemovedFloraCount == 1;

            EcologySaveData ecologySave = runtime.CreateSaveData();
            string ecologyJson = JsonSerializer.Serialize(ecologySave);
            bool regionDeltaOnly =
                ecologySave.RemovedFloraInstanceIds.Count == 1 &&
                ecologySave.DiscoveredFloraIds.Count == 1 &&
                ecologySave.DiscoveredFaunaIds.Count == 1 &&
                !ecologyJson.Contains(
                    "ecology.fauna.",
                    StringComparison.Ordinal) &&
                !ecologyJson.Contains(
                    "\"Position",
                    StringComparison.Ordinal);

            StarterRepairSession session = new(repairRecipe);
            using SaveDatabase database = new(databasePath);
            using SaveAutosaveCoordinator autosave = new(
                database,
                new DomainEventBus(),
                TimeSpan.FromMilliseconds(60.0));
            await database.InitializeAsync(cancellationToken)
                .ConfigureAwait(false);
            await database.ResetSlotAsync(slotId, cancellationToken)
                .ConfigureAwait(false);
            SaveGameSnapshot expected = StarterRepairSnapshotFactory.Create(
                slotId,
                revision: 1,
                session,
                playerPositionX: 0.0,
                playerPositionY: 1.05,
                playerPositionZ: 5.5,
                ecology: ecologySave);
            await autosave.FlushAsync(
                AutosaveTrigger.DiscoveryChanged,
                expected,
                cancellationToken).ConfigureAwait(false);
            SaveGameSnapshot? loaded = await database.LoadAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);
            bool exactRoundTrip = SaveDatabase.SnapshotsEqual(
                expected,
                loaded,
                out string mismatch);
            EcologyRuntime restored = new(
                catalog,
                left,
                loaded?.Ecology);
            bool coldRestore =
                loaded?.Ecology is not null &&
                exactRoundTrip &&
                string.Equals(
                    JsonSerializer.Serialize(ecologySave),
                    JsonSerializer.Serialize(restored.CreateSaveData()),
                    StringComparison.Ordinal);
            EcologyRuntime legacy = new(catalog, left, saveData: null);
            bool legacyFallback =
                legacy.DiscoveredFloraCount == 0 &&
                legacy.DiscoveredFaunaCount == 0 &&
                legacy.RemovedFloraCount == 0;

            SaveDatabaseDiagnostics diagnostics =
                await database.ReadDiagnosticsAsync(
                    slotId,
                    cancellationToken).ConfigureAwait(false);
            string autosaveLog = autosave.AutosaveLogPath;
            bool logWritten = File.Exists(autosaveLog) &&
                File.ReadAllText(autosaveLog).Contains(
                    "DiscoveryChanged",
                    StringComparison.Ordinal);

            bool passed =
                catalog.Biomes.Count == EcologyCatalog.ExpectedBiomeCount &&
                catalog.Flora.Count == EcologyCatalog.ExpectedFloraCount &&
                catalog.Fauna.Count == EcologyCatalog.ExpectedFaunaCount &&
                movementCoverage &&
                bodyPlanCoverage &&
                behaviorCoverage &&
                deterministicPlacement &&
                floraInstancing &&
                populationLimits &&
                updateTiers &&
                behaviorRuntime &&
                discoveryLifecycle &&
                regionDeltaOnly &&
                stress16Biomes &&
                coldRestore &&
                legacyFallback &&
                exactRoundTrip &&
                logWritten &&
                diagnostics.MaximumConcurrentWriters <= 1 &&
                string.Equals(
                    diagnostics.IntegrityResult,
                    "ok",
                    StringComparison.OrdinalIgnoreCase);
            string result = passed
                ? "sixteen biomes, sixty instanced flora modules and twenty fauna archetypes regenerated deterministically, respected population/update tiers, executed utility behavior and persisted discovery/harvest deltas without storing procedural animals"
                : $"ecology mismatch={mismatch}; deterministic={deterministicPlacement}; population={populationLimits}; behavior={behaviorRuntime}; roundTrip={exactRoundTrip}";

            stopwatch.Stop();
            return new EcologyAcceptanceReport(
                passed,
                result,
                catalog.Biomes.Count,
                catalog.Flora.Count,
                catalog.Fauna.Count,
                movementCoverage,
                bodyPlanCoverage,
                behaviorCoverage,
                deterministicPlacement,
                floraInstancing,
                populationLimits,
                updateTiers,
                behaviorRuntime,
                discoveryLifecycle,
                regionDeltaOnly,
                stress16Biomes,
                coldRestore,
                legacyFallback,
                exactRoundTrip,
                logWritten,
                diagnostics,
                stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            throw new InvalidOperationException(
                "TASK-116 ecology acceptance failed.",
                exception);
        }
    }

    private static void DeleteTestArtifacts(string databasePath)
    {
        string directory = Path.GetDirectoryName(databasePath) ?? ".";
        string baseName = Path.GetFileNameWithoutExtension(databasePath);
        string[] paths =
        {
            databasePath,
            databasePath + "-wal",
            databasePath + "-shm",
            databasePath + ".bak",
            Path.Combine(directory, baseName + ".autosave.log"),
            Path.Combine(directory, "logs", baseName + ".autosave.log"),
            Path.Combine(directory, baseName + ".recovery.log")
        };
        foreach (string path in paths)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
