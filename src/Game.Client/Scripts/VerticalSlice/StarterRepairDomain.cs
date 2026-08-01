using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public enum StarterRepairResult
{
    Repaired = 0,
    AlreadyRepaired = 1,
    InsufficientSalvage = 2
}

public sealed class StarterRepairSession
{
    public const string SalvageDefinitionId = "resource.salvage_alloy";
    public const int RequiredSalvage = 3;

    private readonly HashSet<string> _collectedNodeIds =
        new(StringComparer.Ordinal);

    public int SalvageQuantity { get; private set; }

    public bool ShipRepaired { get; private set; }

    public int CollectedNodeCount => _collectedNodeIds.Count;

    public IReadOnlyCollection<string> CollectedNodeIds => _collectedNodeIds;

    public bool TryCollect(
        string resourceNodeId,
        int quantity,
        out string result)
    {
        if (string.IsNullOrWhiteSpace(resourceNodeId))
        {
            throw new ArgumentException(
                "Resource node ID must not be empty.",
                nameof(resourceNodeId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Collected quantity must be positive.");
        }

        if (ShipRepaired)
        {
            result = "repair objective already completed";
            return false;
        }

        if (!_collectedNodeIds.Add(resourceNodeId))
        {
            result = $"resource node {resourceNodeId} was already collected";
            return false;
        }

        SalvageQuantity += quantity;
        result = $"salvage {SalvageQuantity}/{RequiredSalvage}";
        return true;
    }

    public StarterRepairResult TryRepair(out string result)
    {
        if (ShipRepaired)
        {
            result = "ship already repaired";
            return StarterRepairResult.AlreadyRepaired;
        }

        if (SalvageQuantity < RequiredSalvage)
        {
            result =
                $"need {RequiredSalvage - SalvageQuantity} more salvage";
            return StarterRepairResult.InsufficientSalvage;
        }

        SalvageQuantity -= RequiredSalvage;
        ShipRepaired = true;
        result = "starter ship repaired; objective completed";
        return StarterRepairResult.Repaired;
    }

    public static StarterRepairSession FromSnapshot(
        SaveGameSnapshot? snapshot,
        IReadOnlyList<string> orderedResourceNodeIds)
    {
        ArgumentNullException.ThrowIfNull(orderedResourceNodeIds);
        StarterRepairSession session = new();
        if (snapshot is null)
        {
            return session;
        }

        HashSet<string> knownNodeIds = new(
            orderedResourceNodeIds,
            StringComparer.Ordinal);
        foreach (InventoryItemSaveData item in snapshot.Inventory)
        {
            if (!string.Equals(
                item.DefinitionId,
                SalvageDefinitionId,
                StringComparison.Ordinal))
            {
                continue;
            }

            const string itemPrefix = "item.";
            if (!item.ItemId.StartsWith(itemPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            string nodeId = item.ItemId[itemPrefix.Length..];
            if (!knownNodeIds.Contains(nodeId))
            {
                // Ignore stale scene-binding artifacts such as the former
                // salvage.unassigned ID. Only a resource node that exists in
                // the current scene may contribute to this objective.
                continue;
            }

            session._collectedNodeIds.Add(nodeId);
            session.SalvageQuantity += Math.Max(0, item.Quantity);
        }

        session.ShipRepaired = snapshot.Ship.Health >= 99.0;
        if (session.ShipRepaired && session._collectedNodeIds.Count == 0)
        {
            foreach (string nodeId in orderedResourceNodeIds)
            {
                session._collectedNodeIds.Add(nodeId);
            }
        }

        return session;
    }
}

public static class StarterRepairSnapshotFactory
{
    public const string SlotId = "save_1";
    public const string PlanetId = "planet.vertical_slice";
    public const string SystemId = "system.vertical_slice";

    public static SaveGameSnapshot Create(
        string slotId,
        int revision,
        StarterRepairSession session,
        double playerPositionX,
        double playerPositionY,
        double playerPositionZ)
    {
        if (string.IsNullOrWhiteSpace(slotId))
        {
            throw new ArgumentException(
                "Slot ID must not be empty.",
                nameof(slotId));
        }

        ArgumentNullException.ThrowIfNull(session);
        if (revision <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(revision),
                "Revision must be positive.");
        }

        string updatedUtc = DateTimeOffset.UtcNow.ToString(
            "O",
            CultureInfo.InvariantCulture);
        return new SaveGameSnapshot(
            slotId,
            revision,
            GeneratorVersion: 1,
            ContentVersion: SaveDatabase.CurrentContentVersion,
            updatedUtc,
            new PlayerSaveData(
                "player.vertical_slice",
                playerPositionX,
                playerPositionY,
                playerPositionZ,
                PlanetId),
            new ShipSaveData(
                "ship.starter",
                "ship.starter.repairable",
                "Horizon Starter",
                session.ShipRepaired ? 100.0 : 28.0,
                35.0,
                0.0,
                1.0,
                -10.0),
            session.CollectedNodeIds
                .OrderBy(nodeId => nodeId, StringComparer.Ordinal)
                .Select(nodeId => new InventoryItemSaveData(
                    $"item.{nodeId}",
                    StarterRepairSession.SalvageDefinitionId,
                    session.ShipRepaired ? 0 : 1,
                    1.0))
                .ToArray(),
            new VisitedPlanetSaveData(
                PlanetId,
                SystemId,
                updatedUtc,
                1));
    }
}

public sealed record VerticalSliceAcceptanceReport(
    bool Passed,
    string Result,
    int ResourcesCollected,
    bool RepairBlockedBeforeResources,
    bool ShipRepaired,
    bool QuestAutosaveObserved,
    bool ExactRoundTrip,
    bool LogWritten,
    int Revision,
    SaveDatabaseDiagnostics Diagnostics,
    double ElapsedMilliseconds);

public static class VerticalSliceAcceptanceRunner
{
    public static async Task<VerticalSliceAcceptanceReport> RunAsync(
        string databasePath,
        string slotId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException(
                "Database path must not be empty.",
                nameof(databasePath));
        }

        System.Diagnostics.Stopwatch stopwatch =
            System.Diagnostics.Stopwatch.StartNew();
        try
        {
            DeleteTestArtifacts(databasePath);
            using SaveDatabase database = new(databasePath);
            using SaveAutosaveCoordinator autosave = new(
                database,
                TimeSpan.FromMilliseconds(60.0));

            await database.InitializeAsync(cancellationToken)
                .ConfigureAwait(false);
            await database.ResetSlotAsync(slotId, cancellationToken)
                .ConfigureAwait(false);

            StarterRepairSession session = new();
            StarterRepairResult blockedResult = session.TryRepair(out _);
            bool repairBlocked =
                blockedResult == StarterRepairResult.InsufficientSalvage;

            string[] nodeIds =
            {
                "salvage.alpha",
                "salvage.beta",
                "salvage.gamma"
            };
            foreach (string nodeId in nodeIds)
            {
                if (!session.TryCollect(nodeId, 1, out string collectResult))
                {
                    throw new InvalidOperationException(collectResult);
                }
            }

            StarterRepairResult repairResult = session.TryRepair(out _);
            bool shipRepaired = repairResult == StarterRepairResult.Repaired;
            SaveGameSnapshot expected = StarterRepairSnapshotFactory.Create(
                slotId,
                revision: 1,
                session,
                playerPositionX: 0.0,
                playerPositionY: 1.0,
                playerPositionZ: 4.0);
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

            bool questAutosaveObserved =
                autosave.HasObservedTrigger(AutosaveTrigger.QuestCompleted) &&
                autosave.CompletedBatches == 1 &&
                autosave.RequestedSaves == 1 &&
                autosave.FailedBatches == 0 &&
                autosave.LastCompletedTriggerSummary.Contains(
                    nameof(AutosaveTrigger.QuestCompleted),
                    StringComparison.Ordinal);
            string logText = File.Exists(autosave.AutosaveLogPath)
                ? File.ReadAllText(autosave.AutosaveLogPath)
                : string.Empty;
            bool logWritten =
                logText.Contains("AUTOSAVE_COMPLETED", StringComparison.Ordinal) &&
                logText.Contains(
                    nameof(AutosaveTrigger.QuestCompleted),
                    StringComparison.Ordinal);
            bool integrityOk = string.Equals(
                diagnostics.IntegrityResult,
                "ok",
                StringComparison.OrdinalIgnoreCase);
            bool passed =
                repairBlocked &&
                session.CollectedNodeCount == 3 &&
                session.SalvageQuantity == 0 &&
                shipRepaired &&
                questAutosaveObserved &&
                exactRoundTrip &&
                logWritten &&
                diagnostics.MaximumConcurrentWriters == 1 &&
                integrityOk &&
                loaded?.Ship.Health == 100.0;

            List<string> failedCriteria = new();
            if (!repairBlocked)
            {
                failedCriteria.Add("repairBlocked=0");
            }

            if (session.CollectedNodeCount != 3)
            {
                failedCriteria.Add(
                    $"resources={session.CollectedNodeCount}");
            }

            if (!shipRepaired)
            {
                failedCriteria.Add("shipRepaired=0");
            }

            if (!questAutosaveObserved)
            {
                failedCriteria.Add("questAutosave=0");
            }

            if (!exactRoundTrip)
            {
                failedCriteria.Add($"roundTrip={mismatch}");
            }

            if (!logWritten)
            {
                failedCriteria.Add("logWritten=0");
            }

            if (diagnostics.MaximumConcurrentWriters != 1)
            {
                failedCriteria.Add(
                    $"maxWriters={diagnostics.MaximumConcurrentWriters}");
            }

            if (!integrityOk)
            {
                failedCriteria.Add($"integrity={diagnostics.IntegrityResult}");
            }

            stopwatch.Stop();
            return new VerticalSliceAcceptanceReport(
                passed,
                passed
                    ? "resource collection completed the starter repair objective and persisted the repaired ship"
                    : $"vertical-slice criteria failed: {string.Join(", ", failedCriteria)}",
                session.CollectedNodeCount,
                repairBlocked,
                shipRepaired,
                questAutosaveObserved,
                exactRoundTrip,
                logWritten,
                expected.Revision,
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
            return new VerticalSliceAcceptanceReport(
                false,
                $"{exception.GetType().Name}: {exception.Message}",
                0,
                false,
                false,
                false,
                false,
                false,
                0,
                new SaveDatabaseDiagnostics(
                    0,
                    "unknown",
                    false,
                    0,
                    0,
                    "not-run",
                    0,
                    0,
                    0,
                    0,
                    0,
                    0),
                stopwatch.Elapsed.TotalMilliseconds);
        }
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
