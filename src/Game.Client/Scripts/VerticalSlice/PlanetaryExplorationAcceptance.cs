using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public sealed record PlanetaryExplorationAcceptanceReport(
    bool Passed,
    string Result,
    int PoiTypes,
    int Placements,
    bool Deterministic,
    bool Constraints,
    bool Spacing,
    bool QuestBias,
    bool InfrastructureClearance,
    bool ScanAll,
    bool ResolveAll,
    bool Naming,
    bool ColdRestore,
    bool LegacyFallback,
    bool ExactRoundTrip,
    bool LogWritten,
    SaveDatabaseDiagnostics Diagnostics,
    double ElapsedMilliseconds);

public static class PlanetaryExplorationAcceptanceRunner
{
    public static async Task<PlanetaryExplorationAcceptanceReport> RunAsync(
        string databasePath,
        string slotId,
        GameContentCatalog contentCatalog,
        PlanetaryPoiCatalog poiCatalog,
        CraftingRecipeDefinition repairRecipe,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentNullException.ThrowIfNull(contentCatalog);
        ArgumentNullException.ThrowIfNull(poiCatalog);
        ArgumentNullException.ThrowIfNull(repairRecipe);
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            DeleteTestArtifacts(databasePath);
            SaveDatabase.RegisterKnownInventoryDefinitions(
                contentCatalog.Items.Keys);
            string[] questTags = { "ancient", "salvage" };
            IReadOnlyList<PlanetaryPoiPlacement> firstPlan =
                PlanetaryPoiPlanner.Plan(poiCatalog, questTags);
            IReadOnlyList<PlanetaryPoiPlacement> secondPlan =
                PlanetaryPoiPlanner.Plan(poiCatalog, questTags);
            bool deterministic = string.Equals(
                JsonSerializer.Serialize(firstPlan),
                JsonSerializer.Serialize(secondPlan),
                StringComparison.Ordinal);
            bool constraints = firstPlan.All(placement =>
                PlanetaryPoiPlanner.MeetsDefinitionConstraints(
                    poiCatalog.GetDefinition(placement.PoiTypeId),
                    placement.Environment));
            bool spacing = HasValidSpacing(firstPlan, poiCatalog);
            bool questBias = firstPlan.Count(placement => placement.QuestBiased) >= 2;
            bool infrastructureClearance = firstPlan.All(placement =>
                PlanetaryPoiPlanner.ClearsVerticalSliceInfrastructure(
                    poiCatalog.GetDefinition(placement.PoiTypeId),
                    placement.PositionX,
                    placement.PositionZ));

            PlanetaryExplorationRuntime runtime = new(
                poiCatalog,
                firstPlan);
            foreach (PlanetaryPoiRuntimeState state in runtime.States)
            {
                PlanetaryPoiScanResult scan = runtime.Scan(
                    state.Placement.InstanceId,
                    out _);
                if (scan != PlanetaryPoiScanResult.Discovered)
                {
                    throw new InvalidOperationException(
                        $"Scan failed for {state.Placement.InstanceId}: {scan}.");
                }

                PlanetaryPoiRuntimeState scanned = runtime.GetState(
                    state.Placement.InstanceId);
                if (!scanned.Resolved)
                {
                    PlanetaryPoiInteractionResult interaction = runtime.Interact(
                        state.Placement.InstanceId,
                        out _);
                    if (interaction != PlanetaryPoiInteractionResult.Resolved)
                    {
                        throw new InvalidOperationException(
                            $"Interaction failed for {state.Placement.InstanceId}: " +
                            interaction);
                    }
                }
            }

            bool scanAll = runtime.DiscoveredCount ==
                PlanetaryPoiCatalog.ExpectedPoiTypeCount;
            bool resolveAll = runtime.ResolvedCount ==
                PlanetaryPoiCatalog.ExpectedPoiTypeCount;
            PlanetaryPoiRuntimeState renameTarget = runtime.States.First(state =>
                state.Definition.CanBeNamed);
            bool naming = runtime.TryRename(
                renameTarget.Placement.InstanceId,
                "Frontier Prime",
                out _) &&
                string.Equals(
                    runtime.GetState(renameTarget.Placement.InstanceId).CustomName,
                    "Frontier Prime",
                    StringComparison.Ordinal);

            StarterRepairSession session = new(repairRecipe);
            SaveGameSnapshot expected = StarterRepairSnapshotFactory.Create(
                slotId,
                revision: 1,
                session,
                playerPositionX: 1.0,
                playerPositionY: 2.0,
                playerPositionZ: 3.0,
                planetaryExploration: runtime.CreateSaveData());
            using SaveDatabase database = new(databasePath);
            using SaveAutosaveCoordinator autosave = new(
                database,
                new DomainEventBus(),
                TimeSpan.FromMilliseconds(60.0));
            await database.InitializeAsync(cancellationToken)
                .ConfigureAwait(false);
            await database.ResetSlotAsync(slotId, cancellationToken)
                .ConfigureAwait(false);
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
            PlanetaryExplorationRuntime restored = new(
                poiCatalog,
                firstPlan,
                loaded?.PlanetaryExploration);
            PlanetaryExplorationSaveData expectedExploration =
                runtime.CreateSaveData();
            PlanetaryExplorationSaveData restoredExploration =
                restored.CreateSaveData();
            bool coldRestore = loaded?.PlanetaryExploration is not null &&
                string.Equals(
                    JsonSerializer.Serialize(expectedExploration),
                    JsonSerializer.Serialize(restoredExploration),
                    StringComparison.Ordinal) &&
                restored.DiscoveredCount == runtime.DiscoveredCount &&
                restored.ResolvedCount == runtime.ResolvedCount &&
                restored.NamedCount == runtime.NamedCount &&
                restored.DiscoveryPoints == runtime.DiscoveryPoints;
            PlanetaryExplorationRuntime legacy = new(
                poiCatalog,
                firstPlan,
                saveData: null);
            bool legacyFallback = legacy.DiscoveredCount == 0 &&
                legacy.ResolvedCount == 0 &&
                legacy.NamedCount == 0 &&
                legacy.DiscoveryPoints == 0;

            SaveDatabaseDiagnostics diagnostics =
                await database.ReadDiagnosticsAsync(
                    slotId,
                    cancellationToken).ConfigureAwait(false);
            string logText = File.Exists(autosave.AutosaveLogPath)
                ? File.ReadAllText(autosave.AutosaveLogPath)
                : string.Empty;
            bool logWritten = logText.Contains(
                    "AUTOSAVE_COMPLETED",
                    StringComparison.Ordinal) &&
                logText.Contains(
                    nameof(AutosaveTrigger.DiscoveryChanged),
                    StringComparison.Ordinal);
            bool integrityOk = string.Equals(
                diagnostics.IntegrityResult,
                "ok",
                StringComparison.OrdinalIgnoreCase);
            bool passed = poiCatalog.Definitions.Count ==
                    PlanetaryPoiCatalog.ExpectedPoiTypeCount &&
                firstPlan.Count == PlanetaryPoiCatalog.ExpectedPoiTypeCount &&
                deterministic && constraints && spacing && questBias &&
                infrastructureClearance && scanAll && resolveAll && naming && coldRestore &&
                legacyFallback && exactRoundTrip && logWritten &&
                diagnostics.MaximumConcurrentWriters == 1 && integrityOk;
            List<string> failures = new();
            if (!deterministic) failures.Add("deterministic=0");
            if (!constraints) failures.Add("constraints=0");
            if (!spacing) failures.Add("spacing=0");
            if (!questBias) failures.Add("questBias=0");
            if (!infrastructureClearance) failures.Add("clearance=0");
            if (!scanAll) failures.Add("scanAll=0");
            if (!resolveAll) failures.Add("resolveAll=0");
            if (!naming) failures.Add("naming=0");
            if (!coldRestore) failures.Add("restore=0");
            if (!legacyFallback) failures.Add("legacy=0");
            if (!exactRoundTrip) failures.Add($"roundTrip=0({mismatch})");
            if (!logWritten) failures.Add("log=0");
            if (diagnostics.MaximumConcurrentWriters != 1)
                failures.Add("writers=0");
            if (!integrityOk) failures.Add("integrity=0");
            string result = passed
                ? "twenty deterministic planetary POI types were placed with " +
                  "biome, slope, height, water, danger, rarity and quest-aware " +
                  "constraints, scanned, resolved, named and restored exactly"
                : string.Join(", ", failures);
            stopwatch.Stop();
            return new PlanetaryExplorationAcceptanceReport(
                passed,
                result,
                poiCatalog.Definitions.Count,
                firstPlan.Count,
                deterministic,
                constraints,
                spacing,
                questBias,
                infrastructureClearance,
                scanAll,
                resolveAll,
                naming,
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
            return new PlanetaryExplorationAcceptanceReport(
                false,
                $"{exception.GetType().Name}: {exception.Message}",
                poiCatalog.Definitions.Count,
                0,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                new SaveDatabaseDiagnostics(
                    0,
                    "unknown",
                    false,
                    0,
                    0,
                    "error",
                    0,
                    0,
                    0,
                    0,
                    0,
                    0),
                stopwatch.Elapsed.TotalMilliseconds);
        }
        finally
        {
            DeleteTestArtifacts(databasePath);
        }
    }

    private static bool HasValidSpacing(
        IReadOnlyList<PlanetaryPoiPlacement> placements,
        PlanetaryPoiCatalog catalog)
    {
        for (int leftIndex = 0;
             leftIndex < placements.Count;
             leftIndex++)
        {
            PlanetaryPoiPlacement left = placements[leftIndex];
            for (int rightIndex = leftIndex + 1;
                 rightIndex < placements.Count;
                 rightIndex++)
            {
                PlanetaryPoiPlacement right = placements[rightIndex];
                double required = Math.Max(
                    catalog.MinimumPoiSpacing,
                    Math.Max(
                        catalog.GetDefinition(left.PoiTypeId).MinimumSpacing,
                        catalog.GetDefinition(right.PoiTypeId).MinimumSpacing));
                double dx = left.PositionX - right.PositionX;
                double dz = left.PositionZ - right.PositionZ;
                if (Math.Sqrt(dx * dx + dz * dz) < required)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void DeleteTestArtifacts(string databasePath)
    {
        string[] suffixes =
        {
            string.Empty,
            "-wal",
            "-shm",
            ".bak",
            ".bak-wal",
            ".bak-shm",
            ".autosave.log"
        };
        foreach (string suffix in suffixes)
        {
            string path = databasePath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
