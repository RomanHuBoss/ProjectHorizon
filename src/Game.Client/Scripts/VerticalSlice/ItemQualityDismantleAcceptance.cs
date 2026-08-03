using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed record ItemQualityDismantleAcceptanceReport(
    bool Passed,
    string Result,
    string RecipeId,
    int Quality,
    int Purity,
    int Stability,
    bool Deterministic,
    bool InRecipeRange,
    bool QualitySensitiveReturns,
    int DismantleReturns,
    bool ExactRoundTrip,
    bool LogWritten,
    SaveDatabaseDiagnostics Diagnostics,
    double ElapsedMilliseconds);

public static class ItemQualityDismantleAcceptanceRunner
{
    private const string RecipeId = "recipe.ship.power_coupler";

    public static async Task<ItemQualityDismantleAcceptanceReport> RunAsync(
        string databasePath,
        string slotId,
        GameContentCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentNullException.ThrowIfNull(catalog);
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            DeleteTestArtifacts(databasePath);
            SaveDatabase.RegisterKnownInventoryDefinitions(catalog.Items.Keys);
            CraftingRecipeDefinition recipe = catalog.GetRecipe(RecipeId);
            IndustryProcessEnvironment environment =
                ItemPropertyRuntime.CreateNominalEnvironment(recipe);
            IndustryItemProperties generated =
                ItemPropertyRuntime.CreateOutputProperties(
                    recipe,
                    processSequence: 42,
                    environment);
            IndustryItemProperties repeated =
                ItemPropertyRuntime.CreateOutputProperties(
                    recipe,
                    processSequence: 42,
                    environment);
            bool deterministic = generated == repeated;
            bool inRecipeRange =
                generated.Quality >= recipe.Quality.Minimum &&
                generated.Quality <= recipe.Quality.Maximum &&
                generated.Purity is >= 0 and <= 100 &&
                generated.Stability is >= 0 and <= 100;

            IndustryItemProperties high =
                IndustryItemProperties.Create(90, 85, 80);
            IndustryItemProperties low =
                IndustryItemProperties.Create(40, 40, 40);
            DismantleExecutionReport highReport =
                ItemPropertyRuntime.Dismantle(recipe, high);
            DismantleExecutionReport lowReport =
                ItemPropertyRuntime.Dismantle(recipe, low);
            int highReturns = highReport.Returns.Sum(stack => stack.Quantity);
            int lowReturns = lowReport.Returns.Sum(stack => stack.Quantity);
            int maximumReturns = recipe.DismantleReturns.Sum(
                stack => stack.Quantity);
            bool qualitySensitiveReturns =
                highReport.Succeeded && lowReport.Succeeded &&
                highReturns > lowReturns &&
                highReturns > 0 &&
                highReturns < maximumReturns;

            CraftingStackDefinition output = recipe.Outputs.Single();
            IndustryItemProperties recovered =
                ItemPropertyRuntime.CreateRecoveredProperties(high);
            string updatedUtc = DateTimeOffset.UtcNow.ToString(
                "O",
                CultureInfo.InvariantCulture);
            SaveGameSnapshot expected = new(
                slotId,
                1,
                1,
                SaveDatabase.CurrentContentVersion,
                updatedUtc,
                new PlayerSaveData(
                    "player.item-properties-test",
                    1.0,
                    2.0,
                    3.0,
                    StarterRepairSnapshotFactory.PlanetId),
                new ShipSaveData(
                    "ship.item-properties-test",
                    "ship.starter.repairable",
                    "Property Test Ship",
                    100.0,
                    50.0,
                    0.0,
                    0.0,
                    0.0),
                new[]
                {
                    new InventoryItemSaveData(
                        $"crafted.{output.DefinitionId}",
                        output.DefinitionId,
                        1,
                        1.0,
                        Quality: generated.Quality,
                        Purity: generated.Purity,
                        Stability: generated.Stability),
                    new InventoryItemSaveData(
                        "crafted." + highReport.Returns[0].DefinitionId,
                        highReport.Returns[0].DefinitionId,
                        highReport.Returns[0].Quantity,
                        1.0,
                        Quality: recovered.Quality,
                        Purity: recovered.Purity,
                        Stability: recovered.Stability)
                },
                new VisitedPlanetSaveData(
                    StarterRepairSnapshotFactory.PlanetId,
                    StarterRepairSnapshotFactory.SystemId,
                    updatedUtc,
                    1));

            using SaveDatabase database = new(databasePath);
            using SaveAutosaveCoordinator autosave = new(
                database,
                TimeSpan.FromMilliseconds(60.0));
            await database.InitializeAsync(cancellationToken)
                .ConfigureAwait(false);
            await database.ResetSlotAsync(slotId, cancellationToken)
                .ConfigureAwait(false);
            await autosave.FlushAsync(
                AutosaveTrigger.BaseChanged,
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
                    nameof(AutosaveTrigger.BaseChanged),
                    StringComparison.Ordinal);
            bool integrityOk = string.Equals(
                diagnostics.IntegrityResult,
                "ok",
                StringComparison.OrdinalIgnoreCase);
            bool passed = deterministic && inRecipeRange &&
                qualitySensitiveReturns && exactRoundTrip && logWritten &&
                diagnostics.MaximumConcurrentWriters == 1 && integrityOk;
            List<string> failures = new();
            if (!deterministic) failures.Add("deterministic=0");
            if (!inRecipeRange) failures.Add("range=0");
            if (!qualitySensitiveReturns) failures.Add("dismantle=0");
            if (!exactRoundTrip) failures.Add($"roundTrip={mismatch}");
            if (!logWritten) failures.Add("logWritten=0");
            if (diagnostics.MaximumConcurrentWriters != 1)
                failures.Add($"maxWriters={diagnostics.MaximumConcurrentWriters}");
            if (!integrityOk)
                failures.Add($"integrity={diagnostics.IntegrityResult}");

            stopwatch.Stop();
            return new ItemQualityDismantleAcceptanceReport(
                passed,
                passed
                    ? "deterministic quality, purity and stability affected partial dismantle returns and persisted exactly"
                    : $"item property criteria failed: {string.Join(", ", failures)}",
                recipe.RecipeId,
                generated.Quality,
                generated.Purity,
                generated.Stability,
                deterministic,
                inRecipeRange,
                qualitySensitiveReturns,
                highReturns,
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
            return new ItemQualityDismantleAcceptanceReport(
                false,
                $"{exception.GetType().Name}: {exception.Message}",
                RecipeId,
                0,
                0,
                0,
                false,
                false,
                false,
                0,
                false,
                false,
                new SaveDatabaseDiagnostics(
                    0, "unknown", false, 0, 0, "not-run", 0, 0, 0, 0, 0, 0),
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private static void DeleteTestArtifacts(string databasePath)
    {
        foreach (string path in new[]
        {
            databasePath,
            databasePath + "-wal",
            databasePath + "-shm",
            databasePath + ".backup",
            databasePath + ".backup-wal",
            databasePath + ".backup-shm"
        })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        string? directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            string logPath = Path.Combine(
                directory,
                "logs",
                $"{Path.GetFileNameWithoutExtension(databasePath)}.autosave.log");
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }
    }
}
