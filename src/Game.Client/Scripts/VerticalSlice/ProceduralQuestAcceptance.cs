using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public sealed record ProceduralQuestAcceptanceReport(
    bool Passed,
    string Result,
    int ObjectiveTypes,
    int GeneratedQuests,
    bool Deterministic,
    bool AllTypesSupported,
    bool Feasibility,
    bool InfeasibleRejected,
    bool ActiveLimit,
    bool ObjectiveLifecycle,
    bool ReturnLifecycle,
    bool RewardLifecycle,
    bool GeneratedBoardPlayable,
    bool ColdRestore,
    bool LegacyFallback,
    bool ExactRoundTrip,
    bool LogWritten,
    SaveDatabaseDiagnostics Diagnostics,
    double ElapsedMilliseconds);

public static class ProceduralQuestAcceptanceRunner
{
    public static async Task<ProceduralQuestAcceptanceReport> RunAsync(
        string databasePath,
        string slotId,
        ProceduralQuestCatalog catalog,
        ProceduralQuestCapabilities fullCapabilities,
        ProceduralQuestCapabilities gameplayCapabilities,
        CraftingRecipeDefinition repairRecipe,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(fullCapabilities);
        ArgumentNullException.ThrowIfNull(gameplayCapabilities);
        ArgumentNullException.ThrowIfNull(repairRecipe);
        Stopwatch stopwatch = Stopwatch.StartNew();
        SaveDatabaseDiagnostics emptyDiagnostics = new(
            SaveDatabase.CurrentSchemaVersion,
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
            0);
        try
        {
            IReadOnlyList<ProceduralQuestDefinition> first =
                ProceduralQuestGenerator.Generate(catalog, fullCapabilities);
            IReadOnlyList<ProceduralQuestDefinition> second =
                ProceduralQuestGenerator.Generate(catalog, fullCapabilities);
            bool deterministic = string.Equals(
                JsonSerializer.Serialize(first),
                JsonSerializer.Serialize(second),
                StringComparison.Ordinal);
            bool allTypesSupported = first
                .Select(quest => quest.ObjectiveType)
                .Distinct()
                .Count() == ProceduralQuestCatalog.ExpectedObjectiveTypeCount;
            bool feasibility = first.All(quest =>
                ProceduralQuestGenerator.ValidateFeasibility(
                    quest,
                    fullCapabilities,
                    out _));

            ProceduralQuestCapabilities impossible = fullCapabilities with
            {
                DefeatTargetIds = Array.Empty<string>(),
                ProtectTargetIds = Array.Empty<string>(),
                EquipmentTier = 0
            };
            ProceduralQuestDefinition defeat = first.First(quest =>
                quest.ObjectiveType == ProceduralQuestObjectiveType.DefeatTarget);
            bool infeasibleRejected =
                !ProceduralQuestGenerator.ValidateFeasibility(
                    defeat,
                    impossible,
                    out _);

            ProceduralQuestRuntime limitRuntime = new(
                catalog,
                fullCapabilities,
                saveData: null);
            bool activeLimit = true;
            ProceduralQuestView[] limitViews = limitRuntime.Views.ToArray();
            for (int index = 0; index < catalog.MaximumActive; index++)
            {
                activeLimit &= limitRuntime.TryAccept(
                    limitViews[index].Definition.QuestId,
                    out _);
            }
            activeLimit &= !limitRuntime.TryAccept(
                limitViews[catalog.MaximumActive].Definition.QuestId,
                out _);

            ProceduralQuestRuntime lifecycle = new(
                catalog,
                fullCapabilities,
                saveData: null);
            bool objectiveLifecycle = lifecycle.Instances.Count == catalog.BoardSize &&
                lifecycle.Instances.Select(instance => instance.QuestId)
                    .Distinct(StringComparer.Ordinal).Count() == catalog.BoardSize;
            bool returnLifecycle = true;
            bool rewardLifecycle = true;
            int rewardCredits = 0;
            int reputation = 0;
            foreach (ProceduralQuestView view in lifecycle.Views.ToArray())
            {
                ProceduralQuestDefinition quest = view.Definition;
                objectiveLifecycle &= lifecycle.TryAccept(quest.QuestId, out _);
                int changed = lifecycle.RecordObjective(
                    quest.ObjectiveType,
                    quest.TargetDefinitionId,
                    quest.RequiredQuantity,
                    out _);
                objectiveLifecycle &= changed == 1;
                if (quest.RequiresReturnToGiver)
                {
                    returnLifecycle &= lifecycle.RecordReturnToNpc(
                        quest.GiverNpcId,
                        out _) >= 1;
                }
                ProceduralQuestView ready = lifecycle.Views.First(candidate =>
                    string.Equals(
                        candidate.Definition.QuestId,
                        quest.QuestId,
                        StringComparison.Ordinal));
                objectiveLifecycle &= ready.Status == ProceduralQuestStatus.ReadyToClaim;
                rewardLifecycle &= lifecycle.TryClaim(
                    quest.QuestId,
                    out int credits,
                    out int reputationReward,
                    out string factionId,
                    out _);
                rewardLifecycle &= credits == quest.RewardCredits &&
                    reputationReward == quest.ReputationReward &&
                    string.Equals(factionId, quest.FactionId, StringComparison.Ordinal);
                rewardCredits += credits;
                reputation += reputationReward;
            }
            objectiveLifecycle &= lifecycle.CompletedCount == catalog.BoardSize;
            rewardLifecycle &= rewardCredits > 0 && reputation > 0;

            IReadOnlyList<ProceduralQuestDefinition> gameplayBoard =
                ProceduralQuestGenerator.Generate(catalog, gameplayCapabilities);
            bool generatedBoardPlayable =
                gameplayBoard.Count == catalog.BoardSize &&
                gameplayBoard.All(quest =>
                    ProceduralQuestGenerator.ValidateFeasibility(
                        quest,
                        gameplayCapabilities,
                        out _)) &&
                gameplayBoard.Any(quest =>
                    quest.ObjectiveType == ProceduralQuestObjectiveType.DefeatTarget) &&
                gameplayBoard.Any(quest =>
                    quest.ObjectiveType == ProceduralQuestObjectiveType.ProtectTarget);

            ProceduralQuestRuntime persistenceRuntime = new(
                catalog,
                gameplayCapabilities,
                saveData: null);
            ProceduralQuestDefinition persistentQuest =
                persistenceRuntime.Views[0].Definition;
            persistenceRuntime.TryAccept(persistentQuest.QuestId, out _);
            persistenceRuntime.RecordObjective(
                persistentQuest.ObjectiveType,
                persistentQuest.TargetDefinitionId,
                persistentQuest.RequiredQuantity,
                out _);
            if (persistentQuest.RequiresReturnToGiver)
            {
                persistenceRuntime.RecordReturnToNpc(
                    persistentQuest.GiverNpcId,
                    out _);
            }
            ProceduralQuestSaveData questSave = persistenceRuntime.CreateSaveData();

            string? directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
            string legacyAutosaveLog = Path.Combine(
                directory ?? ".",
                Path.GetFileNameWithoutExtension(databasePath) + ".autosave.log");
            if (File.Exists(legacyAutosaveLog))
            {
                File.Delete(legacyAutosaveLog);
            }

            StarterRepairSession session = new(
                repairRecipe,
                static _ => true,
                Array.Empty<CraftingRecipeDefinition>());
            using SaveDatabase database = new(databasePath);
            using SaveAutosaveCoordinator autosave = new(
                database,
                new DomainEventBus(),
                TimeSpan.FromMilliseconds(50));
            await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await database.ResetSlotAsync(slotId, cancellationToken)
                .ConfigureAwait(false);
            SaveGameSnapshot expected = StarterRepairSnapshotFactory.Create(
                slotId,
                revision: 1,
                session,
                playerPositionX: 0.0,
                playerPositionY: 1.0,
                playerPositionZ: 0.0,
                proceduralQuests: questSave);
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
            ProceduralQuestRuntime restored = new(
                catalog,
                gameplayCapabilities,
                loaded?.ProceduralQuests);
            bool coldRestore = loaded?.ProceduralQuests is not null &&
                exactRoundTrip &&
                string.Equals(
                    JsonSerializer.Serialize(questSave),
                    JsonSerializer.Serialize(restored.CreateSaveData()),
                    StringComparison.Ordinal);
            ProceduralQuestRuntime legacy = new(
                catalog,
                gameplayCapabilities,
                saveData: null);
            bool legacyFallback = legacy.AcceptedCount == 0 &&
                legacy.CompletedCount == 0 &&
                legacy.Views.All(view =>
                    view.Status == ProceduralQuestStatus.Offered &&
                    view.Progress == 0);
            SaveDatabaseDiagnostics diagnostics = await database.ReadDiagnosticsAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);
            string autosaveLog = autosave.AutosaveLogPath;
            bool logWritten = File.Exists(autosaveLog) &&
                File.ReadAllText(autosaveLog).Contains(
                    "QuestCompleted",
                    StringComparison.Ordinal);

            bool passed =
                catalog.Profiles.Count == ProceduralQuestCatalog.ExpectedObjectiveTypeCount &&
                first.Count == ProceduralQuestCatalog.ExpectedBoardSize &&
                deterministic && allTypesSupported && feasibility &&
                infeasibleRejected && activeLimit && objectiveLifecycle &&
                returnLifecycle && rewardLifecycle && generatedBoardPlayable &&
                coldRestore && legacyFallback && exactRoundTrip && logWritten &&
                diagnostics.MaximumConcurrentWriters <= 1 &&
                string.Equals(
                    diagnostics.IntegrityResult,
                    "ok",
                    StringComparison.OrdinalIgnoreCase);
            stopwatch.Stop();
            string result = passed
                ? "all fifteen objective types generated deterministic feasible state-graph quests; the gameplay board excluded unavailable combat objectives and quest progress persisted exactly"
                : $"procedural quest acceptance failed; mismatch={mismatch}";
            return new ProceduralQuestAcceptanceReport(
                passed,
                result,
                catalog.Profiles.Count,
                first.Count,
                deterministic,
                allTypesSupported,
                feasibility,
                infeasibleRejected,
                activeLimit,
                objectiveLifecycle,
                returnLifecycle,
                rewardLifecycle,
                generatedBoardPlayable,
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
            return new ProceduralQuestAcceptanceReport(
                false,
                exception.Message,
                catalog.Profiles.Count,
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
                false,
                emptyDiagnostics,
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }
}
