using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed record ChemicalProcessAcceptanceReport(
    bool Passed,
    string Result,
    string BatchRecipeId,
    string VacuumRecipeId,
    int RequestedBatches,
    bool EnergyRejected,
    bool TemperatureRejected,
    bool PressureRejected,
    bool VacuumRejected,
    bool MissingCatalystRejected,
    bool CatalystRetained,
    bool CatalystConsumed,
    bool ByproductsProduced,
    bool BatchOutputCorrect,
    bool HazardsExposed,
    double EnergyConsumed,
    bool ExactRoundTrip,
    bool LogWritten,
    SaveDatabaseDiagnostics Diagnostics,
    double ElapsedMilliseconds);

public static class ChemicalProcessAcceptanceRunner
{
    private const string BatchRecipeId =
        "recipe.chemistry.compotium_concentrate";
    private const string VacuumRecipeId =
        "recipe.chemistry.compotium_crystal";

    public static async Task<ChemicalProcessAcceptanceReport> RunAsync(
        string databasePath,
        string slotId,
        GameContentCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentNullException.ThrowIfNull(catalog);

        Stopwatch stopwatch = Stopwatch.StartNew();
        CraftingRecipeDefinition batchRecipe = catalog.GetRecipe(BatchRecipeId);
        CraftingRecipeDefinition vacuumRecipe = catalog.GetRecipe(VacuumRecipeId);
        CraftingStationDefinition batchStation = catalog.GetStation(
            batchRecipe.RequiredStation);
        CraftingStationDefinition vacuumStation = catalog.GetStation(
            vacuumRecipe.RequiredStation);
        SaveDatabase.RegisterKnownInventoryDefinitions(catalog.Items.Keys);

        try
        {
            DeleteTestArtifacts(databasePath);

            IndustryProcessRuntime lowEnergyRuntime = CreateRuntime(
                batchRecipe,
                initialEnergy: 100.0,
                initialSequence: 0,
                requestedBatches: 2);
            IndustryProcessResult energyResult = lowEnergyRuntime.Validate(
                batchRecipe,
                batchStation,
                new IndustryProcessEnvironment(300.0, 120.0, false),
                requestedBatches: 2,
                out _);
            bool energyRejected =
                energyResult == IndustryProcessResult.InsufficientEnergy;

            IndustryProcessRuntime badEnvironmentRuntime = CreateRuntime(
                batchRecipe,
                initialEnergy: 1000.0,
                initialSequence: 0,
                requestedBatches: 2);
            IndustryProcessResult environmentResult =
                badEnvironmentRuntime.Validate(
                    batchRecipe,
                    batchStation,
                    new IndustryProcessEnvironment(200.0, 120.0, false),
                    requestedBatches: 2,
                    out _);
            bool temperatureRejected =
                environmentResult == IndustryProcessResult.EnvironmentRejected;

            IndustryProcessRuntime badPressureRuntime = CreateRuntime(
                batchRecipe,
                initialEnergy: 1000.0,
                initialSequence: 0,
                requestedBatches: 2);
            IndustryProcessResult pressureResult = badPressureRuntime.Validate(
                batchRecipe,
                batchStation,
                new IndustryProcessEnvironment(300.0, 700.0, false),
                requestedBatches: 2,
                out _);
            bool pressureRejected =
                pressureResult == IndustryProcessResult.EnvironmentRejected;

            IndustryProcessRuntime missingCatalystRuntime = new(
                initialEnergy: 1000.0,
                isTechnologyUnlocked: static _ => true);
            foreach (CraftingStackDefinition input in batchRecipe.Inputs)
            {
                missingCatalystRuntime.AddInventory(
                    input.DefinitionId,
                    checked(input.Quantity * 2));
            }
            IndustryProcessResult missingCatalystResult =
                missingCatalystRuntime.Validate(
                    batchRecipe,
                    batchStation,
                    new IndustryProcessEnvironment(300.0, 120.0, false),
                    requestedBatches: 2,
                    out _);
            bool missingCatalystRejected =
                missingCatalystResult == IndustryProcessResult.MissingCatalysts;

            CatalystStackDefinition batchCatalyst = batchRecipe.Catalysts.Single();
            long retainedSequence = FindSequence(
                batchRecipe,
                batchCatalyst,
                shouldConsume: false);
            IndustryProcessRuntime retainedRuntime = CreateRuntime(
                batchRecipe,
                initialEnergy: 1000.0,
                initialSequence: retainedSequence,
                requestedBatches: 2);
            IndustryProcessExecutionReport batchReport = retainedRuntime.Execute(
                batchRecipe,
                batchStation,
                new IndustryProcessEnvironment(300.0, 120.0, false),
                requestedBatches: 2);
            bool catalystRetained =
                batchReport.Result == IndustryProcessResult.Completed &&
                batchReport.RetainedCatalysts.Count == 1 &&
                batchReport.ConsumedCatalysts.Count == 0 &&
                retainedRuntime.GetQuantity(batchCatalyst.DefinitionId) ==
                    batchCatalyst.Quantity;
            bool byproductsProduced = batchRecipe.Byproducts.All(byproduct =>
                retainedRuntime.GetQuantity(byproduct.DefinitionId) ==
                    byproduct.Quantity * 2) &&
                batchReport.Byproducts.Sum(byproduct => byproduct.Quantity) ==
                    batchRecipe.Byproducts.Sum(byproduct =>
                        byproduct.Quantity * 2);
            bool batchOutputCorrect = batchRecipe.Outputs.All(output =>
                retainedRuntime.GetQuantity(output.DefinitionId) ==
                    output.Quantity * batchRecipe.BatchSize * 2) &&
                batchReport.Outputs.Sum(output => output.Quantity) ==
                    batchRecipe.Outputs.Sum(output =>
                        output.Quantity * batchRecipe.BatchSize * 2);
            bool hazardsExposed = batchRecipe.Hazards.All(hazard =>
                batchReport.Hazards.Contains(hazard, StringComparer.Ordinal));
            double expectedBatchEnergy = batchRecipe.EnergyCost * 2;
            bool batchEnergyCorrect = Math.Abs(
                batchReport.EnergyConsumed - expectedBatchEnergy) < 0.000001;

            long consumedSequence = FindSequence(
                batchRecipe,
                batchCatalyst,
                shouldConsume: true);
            IndustryProcessRuntime consumedRuntime = CreateRuntime(
                batchRecipe,
                initialEnergy: 1000.0,
                initialSequence: consumedSequence,
                requestedBatches: 1);
            IndustryProcessExecutionReport consumedReport = consumedRuntime.Execute(
                batchRecipe,
                batchStation,
                new IndustryProcessEnvironment(300.0, 120.0, false),
                requestedBatches: 1);
            bool catalystConsumed =
                consumedReport.Result == IndustryProcessResult.Completed &&
                consumedReport.ConsumedCatalysts.Count == 1 &&
                consumedRuntime.GetQuantity(batchCatalyst.DefinitionId) == 0;

            CatalystStackDefinition vacuumCatalyst =
                vacuumRecipe.Catalysts.Single();
            long vacuumSequence = FindSequence(
                vacuumRecipe,
                vacuumCatalyst,
                shouldConsume: false);
            IndustryProcessRuntime vacuumRuntime = CreateRuntime(
                vacuumRecipe,
                initialEnergy: 500.0,
                initialSequence: vacuumSequence,
                requestedBatches: 1);
            IndustryProcessResult noVacuumResult = vacuumRuntime.Validate(
                vacuumRecipe,
                vacuumStation,
                new IndustryProcessEnvironment(300.0, 120.0, false),
                requestedBatches: 1,
                out _);
            bool vacuumRejected =
                noVacuumResult == IndustryProcessResult.EnvironmentRejected;
            IndustryProcessExecutionReport vacuumReport = vacuumRuntime.Execute(
                vacuumRecipe,
                vacuumStation,
                new IndustryProcessEnvironment(300.0, 120.0, true),
                requestedBatches: 1);
            bool vacuumCompleted =
                vacuumReport.Result == IndustryProcessResult.Completed &&
                vacuumRecipe.Outputs.All(output =>
                    vacuumRuntime.GetQuantity(output.DefinitionId) ==
                        output.Quantity * vacuumRecipe.BatchSize) &&
                vacuumRecipe.Hazards.All(hazard =>
                    vacuumReport.Hazards.Contains(
                        hazard,
                        StringComparer.Ordinal));

            IReadOnlyList<CraftingStackDefinition> persistedInventory =
                AggregateInventory(
                    retainedRuntime.Inventory,
                    vacuumRuntime.Inventory);
            SaveGameSnapshot expected = CreateSnapshot(
                slotId,
                persistedInventory);

            using SaveDatabase database = new(databasePath);
            using SaveAutosaveCoordinator autosave = new(
                database,
                TimeSpan.FromMilliseconds(60.0));
            await database.InitializeAsync(cancellationToken)
                .ConfigureAwait(false);
            await database.ResetSlotAsync(slotId, cancellationToken)
                .ConfigureAwait(false);
            await autosave.FlushAsync(
                AutosaveTrigger.QuestCompleted,
                expected,
                cancellationToken).ConfigureAwait(false);
            SaveGameSnapshot? loaded = await database.LoadAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);
            bool exactRoundTrip = SaveDatabase.SnapshotsEqual(
                expected,
                loaded,
                out string mismatch);
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
                    nameof(AutosaveTrigger.QuestCompleted),
                    StringComparison.Ordinal);
            bool autosaveOk =
                autosave.CompletedBatches == 1 &&
                autosave.FailedBatches == 0 &&
                autosave.HasObservedTrigger(AutosaveTrigger.QuestCompleted);
            bool integrityOk = string.Equals(
                diagnostics.IntegrityResult,
                "ok",
                StringComparison.OrdinalIgnoreCase);
            bool passed =
                energyRejected &&
                temperatureRejected &&
                pressureRejected &&
                vacuumRejected &&
                missingCatalystRejected &&
                catalystRetained &&
                catalystConsumed &&
                byproductsProduced &&
                batchOutputCorrect &&
                batchEnergyCorrect &&
                hazardsExposed &&
                vacuumCompleted &&
                exactRoundTrip &&
                logWritten &&
                autosaveOk &&
                diagnostics.MaximumConcurrentWriters == 1 &&
                integrityOk;

            List<string> failures = new();
            if (!energyRejected)
                failures.Add("energyRejected=0");
            if (!temperatureRejected)
                failures.Add("temperatureRejected=0");
            if (!pressureRejected)
                failures.Add("pressureRejected=0");
            if (!vacuumRejected)
                failures.Add("vacuumRejected=0");
            if (!missingCatalystRejected)
                failures.Add("missingCatalystRejected=0");
            if (!catalystRetained)
                failures.Add("catalystRetained=0");
            if (!catalystConsumed)
                failures.Add("catalystConsumed=0");
            if (!byproductsProduced)
                failures.Add("byproducts=0");
            if (!batchOutputCorrect)
                failures.Add("batchOutput=0");
            if (!batchEnergyCorrect)
                failures.Add("energyAccounting=0");
            if (!hazardsExposed)
                failures.Add("hazards=0");
            if (!vacuumCompleted)
                failures.Add("vacuumCompletion=0");
            if (!exactRoundTrip)
                failures.Add($"roundTrip={mismatch}");
            if (!logWritten)
                failures.Add("logWritten=0");
            if (!autosaveOk)
                failures.Add("autosave=0");
            if (diagnostics.MaximumConcurrentWriters != 1)
                failures.Add(
                    $"maxWriters={diagnostics.MaximumConcurrentWriters}");
            if (!integrityOk)
                failures.Add($"integrity={diagnostics.IntegrityResult}");

            stopwatch.Stop();
            return new ChemicalProcessAcceptanceReport(
                passed,
                passed
                    ? "extended chemical runtime enforced energy and environment, executed deterministic catalyst consumption, emitted byproducts and persisted batch outputs exactly"
                    : $"chemical process criteria failed: {string.Join(", ", failures)}",
                batchRecipe.RecipeId,
                vacuumRecipe.RecipeId,
                2,
                energyRejected,
                temperatureRejected,
                pressureRejected,
                vacuumRejected,
                missingCatalystRejected,
                catalystRetained,
                catalystConsumed,
                byproductsProduced,
                batchOutputCorrect,
                hazardsExposed,
                batchReport.EnergyConsumed,
                exactRoundTrip,
                logWritten,
                diagnostics,
                stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new ChemicalProcessAcceptanceReport(
                false,
                $"{exception.GetType().Name}: {exception.Message}",
                BatchRecipeId,
                VacuumRecipeId,
                2,
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
                0.0,
                false,
                false,
                new SaveDatabaseDiagnostics(
                    0, "unknown", false, 0, 0, "not-run", 0, 0, 0, 0, 0, 0),
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private static IndustryProcessRuntime CreateRuntime(
        CraftingRecipeDefinition recipe,
        double initialEnergy,
        long initialSequence,
        int requestedBatches)
    {
        IndustryProcessRuntime runtime = new(
            initialEnergy,
            static _ => true,
            initialSequence);
        foreach (CraftingStackDefinition input in recipe.Inputs)
        {
            runtime.AddInventory(
                input.DefinitionId,
                checked(input.Quantity * requestedBatches));
        }

        foreach (CatalystStackDefinition catalyst in recipe.Catalysts)
        {
            runtime.AddInventory(
                catalyst.DefinitionId,
                catalyst.Quantity);
        }

        return runtime;
    }

    private static long FindSequence(
        CraftingRecipeDefinition recipe,
        CatalystStackDefinition catalyst,
        bool shouldConsume)
    {
        for (long sequence = 0; sequence < 100000; sequence++)
        {
            bool actual = IndustryProcessRuntime.ShouldConsumeCatalyst(
                recipe.RecipeId,
                catalyst.DefinitionId,
                catalyst.ConsumptionChance,
                sequence);
            if (actual == shouldConsume)
            {
                return sequence;
            }
        }

        throw new InvalidOperationException(
            $"Could not find deterministic catalyst sequence for " +
            $"{recipe.RecipeId}; consume={(shouldConsume ? 1 : 0)}.");
    }

    private static IReadOnlyList<CraftingStackDefinition> AggregateInventory(
        params IReadOnlyList<CraftingStackDefinition>[] inventories)
    {
        Dictionary<string, int> quantities = new(StringComparer.Ordinal);
        foreach (CraftingStackDefinition item in inventories.SelectMany(
            inventory => inventory))
        {
            quantities.TryGetValue(item.DefinitionId, out int current);
            quantities[item.DefinitionId] = checked(current + item.Quantity);
        }

        return quantities
            .Where(pair => pair.Value > 0)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new CraftingStackDefinition(pair.Key, pair.Value))
            .ToArray();
    }

    private static SaveGameSnapshot CreateSnapshot(
        string slotId,
        IReadOnlyList<CraftingStackDefinition> inventory)
    {
        string updatedUtc = DateTimeOffset.UtcNow.ToString(
            "O",
            CultureInfo.InvariantCulture);
        InventoryItemSaveData[] items = inventory
            .Select((item, index) => new InventoryItemSaveData(
                $"industry.process.{index:000}",
                item.DefinitionId,
                item.Quantity,
                1.0))
            .ToArray();
        return new SaveGameSnapshot(
            slotId,
            Revision: 1,
            GeneratorVersion: 1,
            ContentVersion: SaveDatabase.CurrentContentVersion,
            updatedUtc,
            new PlayerSaveData(
                "player.chemical_acceptance",
                0.0,
                1.0,
                0.0,
                StarterRepairSnapshotFactory.PlanetId),
            new ShipSaveData(
                "ship.chemical_acceptance",
                "ship.starter.repairable",
                "Chemical Acceptance Vessel",
                100.0,
                35.0,
                0.0,
                1.0,
                0.0),
            items,
            new VisitedPlanetSaveData(
                StarterRepairSnapshotFactory.PlanetId,
                StarterRepairSnapshotFactory.SystemId,
                updatedUtc,
                1));
    }

    private static void DeleteTestArtifacts(string databasePath)
    {
        string fullPath = Path.GetFullPath(databasePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        string baseName = Path.GetFileNameWithoutExtension(fullPath);
        foreach (string path in Directory.EnumerateFiles(
            directory,
            $"{baseName}*",
            SearchOption.TopDirectoryOnly))
        {
            File.Delete(path);
        }

        string logPath = Path.Combine(
            directory,
            "logs",
            $"{baseName}.autosave.log");
        if (File.Exists(logPath))
        {
            File.Delete(logPath);
        }
    }
}
