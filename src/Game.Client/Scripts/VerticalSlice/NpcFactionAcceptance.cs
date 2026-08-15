using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public sealed record NpcFactionAcceptanceReport(
    bool Passed,
    string Result,
    int Factions,
    int Archetypes,
    int Agents,
    int DialogueTemplates,
    bool FactionCoverage,
    bool RelationMatrix,
    bool DialogueCoverage,
    bool InteractionRuntime,
    bool ReputationRuntime,
    bool CombatRuntime,
    bool QuestTargets,
    bool DeltaOnly,
    bool ColdRestore,
    bool LegacyFallback,
    bool ExactRoundTrip,
    bool RepeatedSave,
    bool LogWritten,
    SaveDatabaseDiagnostics Diagnostics,
    double ElapsedMilliseconds);

public static class NpcFactionAcceptanceRunner
{
    public static async Task<NpcFactionAcceptanceReport> RunAsync(
        string databasePath,
        string slotId,
        NpcFactionCatalog catalog,
        CraftingRecipeDefinition repairRecipe,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentNullException.ThrowIfNull(catalog);
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
        string mismatch = "not-run";
        try
        {
            bool factionCoverage =
                catalog.Factions.Count == NpcFactionCatalog.ExpectedFactionCount &&
                catalog.Archetypes.Count == NpcFactionCatalog.ExpectedArchetypeCount &&
                catalog.Agents.Count == NpcFactionCatalog.ExpectedAgentCount &&
                Enum.GetValues<NpcArchetype>().All(type =>
                    catalog.Agents.Values.Count(agent => agent.Archetype == type) == 1);

            bool relationMatrix = catalog.Factions.Values.All(faction =>
                faction.Relations.Count == catalog.Factions.Count &&
                faction.Relations.TryGetValue(faction.FactionId, out int self) &&
                self == 100 &&
                catalog.Factions.Keys.All(other =>
                    faction.Relations.TryGetValue(other, out int relation) &&
                    catalog.Factions[other].Relations.TryGetValue(
                        faction.FactionId,
                        out int reverse) &&
                    relation == reverse));

            bool dialogueCoverage =
                catalog.Dialogues.Count == NpcFactionCatalog.ExpectedArchetypeCount &&
                Enum.GetValues<NpcArchetype>().All(type =>
                    catalog.Dialogues.Values.Count(dialogue => dialogue.Archetype == type) == 1) &&
                catalog.Dialogues.Values.All(dialogue =>
                    !string.IsNullOrWhiteSpace(dialogue.GreetingEn) &&
                    !string.IsNullOrWhiteSpace(dialogue.GreetingRu) &&
                    dialogue.Options.Count > 0 &&
                    dialogue.Options.All(option =>
                        !string.IsNullOrWhiteSpace(option.Condition) &&
                        !string.IsNullOrWhiteSpace(option.TextEn) &&
                        !string.IsNullOrWhiteSpace(option.TextRu) &&
                        !string.IsNullOrWhiteSpace(option.ConsequenceEn) &&
                        !string.IsNullOrWhiteSpace(option.ConsequenceRu))) &&
                catalog.Dialogues.Values.Single(dialogue =>
                    dialogue.Archetype == NpcArchetype.Trader).Options.Any(option =>
                    string.Equals(option.Action, "OpenTrade", StringComparison.Ordinal)) &&
                catalog.Dialogues.Values.Single(dialogue =>
                    dialogue.Archetype == NpcArchetype.GuildRepresentative).Options.Any(option =>
                    string.Equals(option.Action, "OpenMissions", StringComparison.Ordinal));

            NpcFactionAgentDefinition technician = catalog.Agents.Values.Single(
                agent => agent.Archetype == NpcArchetype.Technician);
            NpcFactionAgentDefinition pilot = catalog.Agents.Values.Single(
                agent => agent.Archetype == NpcArchetype.Pilot);
            NpcFactionAgentDefinition guard = catalog.Agents.Values.Single(
                agent => agent.Archetype == NpcArchetype.Guard);
            NpcFactionAgentDefinition opponent = catalog.Agents.Values.Single(
                agent => agent.Archetype == NpcArchetype.Opponent);

            NpcFactionRuntime runtime = new(catalog);
            NpcDialogueOptionDefinition technicianOption = catalog
                .GetDialogue(technician.DialogueId).Options.First(option =>
                    !string.Equals(option.Action, "Close", StringComparison.Ordinal));
            NpcDialogueOutcome firstInteraction = runtime.ChooseDialogueOption(
                technician.NpcId,
                technicianOption.OptionId);
            NpcDialogueOutcome repeatedInteraction = runtime.ChooseDialogueOption(
                technician.NpcId,
                technicianOption.OptionId);
            bool interactionRuntime = firstInteraction.Applied &&
                firstInteraction.FirstMeaningfulInteraction &&
                !repeatedInteraction.FirstMeaningfulInteraction &&
                runtime.GetAgent(technician.NpcId).Interacted;
            bool reputationRuntime = firstInteraction.AppliedReputationDelta ==
                    technicianOption.ReputationDelta &&
                repeatedInteraction.AppliedReputationDelta == 0 &&
                runtime.GetFactionReputation(technician.FactionId) ==
                    technicianOption.ReputationDelta;

            int guardReputationBefore = runtime.GetFactionReputation(guard.FactionId);
            NpcFactionCombatOutcome friendlyHit = runtime.ApplyDamage(guard.NpcId, 25.0);
            dialogueCoverage &= runtime.GetAvailableDialogueOptions(guard.NpcId).All(option =>
                !string.Equals(option.Action, "GuardBriefing", StringComparison.Ordinal));
            NpcFactionCombatOutcome enemyHit = runtime.ApplyDamage(opponent.NpcId, 25.0);
            runtime.ApplyDamage(opponent.NpcId, 25.0);
            runtime.ApplyDamage(opponent.NpcId, 25.0);
            NpcFactionCombatOutcome enemyDefeat = runtime.ApplyDamage(opponent.NpcId, 25.0);
            bool combatRuntime = friendlyHit.HealthAfter < friendlyHit.HealthBefore &&
                friendlyHit.AppliedReputationDelta < 0 &&
                runtime.GetFactionReputation(guard.FactionId) < guardReputationBefore &&
                enemyHit.HealthAfter < enemyHit.HealthBefore &&
                enemyDefeat.DefeatedNow && enemyDefeat.Respawned &&
                enemyDefeat.DefeatCount == 1 &&
                Math.Abs(runtime.GetAgent(opponent.NpcId).Health - opponent.Health) < 0.001;

            bool questTargets = catalog.DefeatTargetIds.Count > 0 &&
                catalog.ProtectTargetIds.Count >= 2 &&
                catalog.DefeatTargetIds.Contains(opponent.NpcId, StringComparer.Ordinal) &&
                catalog.ProtectTargetIds.All(id => catalog.GetAgent(id).CanBeProtected);

            NpcFactionSaveData firstSaveData = runtime.CreateSaveData();
            bool deltaOnly = firstSaveData.Reputations.Count < catalog.Factions.Count &&
                firstSaveData.Agents.Count < catalog.Agents.Count &&
                firstSaveData.Agents.Any(state =>
                    string.Equals(state.NpcId, opponent.NpcId, StringComparison.Ordinal) &&
                    state.DefeatCount == 1) &&
                firstSaveData.Agents.All(state =>
                    state.NpcId.StartsWith("npc.", StringComparison.Ordinal));

            string? directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
            StarterRepairSession session = new(
                repairRecipe,
                static _ => true,
                Array.Empty<CraftingRecipeDefinition>());
            using SaveDatabase database = new(databasePath);
            using SaveAutosaveCoordinator autosave = new(
                database,
                TimeSpan.FromMilliseconds(25));
            if (File.Exists(autosave.AutosaveLogPath))
            {
                File.Delete(autosave.AutosaveLogPath);
            }
            await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await database.ResetSlotAsync(slotId, cancellationToken).ConfigureAwait(false);

            SaveGameSnapshot first = StarterRepairSnapshotFactory.Create(
                slotId,
                revision: 1,
                session,
                0.0,
                1.0,
                0.0,
                npcFactions: firstSaveData);
            await autosave.FlushAsync(
                AutosaveTrigger.NpcChanged,
                first,
                cancellationToken).ConfigureAwait(false);

            NpcDialogueOptionDefinition pilotOption = catalog
                .GetDialogue(pilot.DialogueId).Options.First(option =>
                    !string.Equals(option.Action, "Close", StringComparison.Ordinal));
            runtime.ChooseDialogueOption(pilot.NpcId, pilotOption.OptionId);
            runtime.ApplyDamage(opponent.NpcId, 25.0);
            SaveGameSnapshot expected = StarterRepairSnapshotFactory.Create(
                slotId,
                revision: 2,
                session,
                0.0,
                1.0,
                0.0,
                npcFactions: runtime.CreateSaveData());
            await autosave.FlushAsync(
                AutosaveTrigger.NpcChanged,
                expected,
                cancellationToken).ConfigureAwait(false);

            SaveGameSnapshot? loaded = await database.LoadAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);
            bool exactRoundTrip = SaveDatabase.SnapshotsEqual(
                expected,
                loaded,
                out mismatch);
            NpcFactionRuntime restored = new(catalog, loaded?.NpcFactions);
            bool coldRestore = loaded?.NpcFactions is not null && exactRoundTrip &&
                string.Equals(
                    JsonSerializer.Serialize(runtime.CreateSaveData()),
                    JsonSerializer.Serialize(restored.CreateSaveData()),
                    StringComparison.Ordinal);
            bool repeatedSave = loaded?.Revision == 2 && loaded.NpcFactions is not null;

            NpcFactionRuntime legacy = new(catalog, saveData: null);
            bool legacyFallback = legacy.AgentCount == catalog.Agents.Count &&
                legacy.FactionReputation.Values.All(value => value == 0) &&
                legacy.CreateSaveData().Agents.Count == 0 &&
                legacy.CreateSaveData().Reputations.Count == 0;

            SaveDatabaseDiagnostics diagnostics = await database.ReadDiagnosticsAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);
            string autosaveLog = autosave.AutosaveLogPath;
            bool logWritten = File.Exists(autosaveLog) &&
                File.ReadAllText(autosaveLog).Contains(
                    "NpcChanged",
                    StringComparison.Ordinal);

            bool passed = factionCoverage && relationMatrix && dialogueCoverage &&
                interactionRuntime && reputationRuntime && combatRuntime && questTargets &&
                deltaOnly && coldRestore && legacyFallback && exactRoundTrip && repeatedSave &&
                logWritten && diagnostics.MaximumConcurrentWriters <= 1 &&
                string.Equals(diagnostics.IntegrityResult, "ok", StringComparison.OrdinalIgnoreCase);
            stopwatch.Stop();
            string result = passed
                ? "three factions, eight NPC archetypes, localized dialogue conditions/consequences, reputation, combat targets and delta persistence completed exactly"
                : $"NPC/faction acceptance failed; mismatch={mismatch}";
            return new NpcFactionAcceptanceReport(
                passed,
                result,
                catalog.Factions.Count,
                catalog.Archetypes.Count,
                catalog.Agents.Count,
                catalog.Dialogues.Count,
                factionCoverage,
                relationMatrix,
                dialogueCoverage,
                interactionRuntime,
                reputationRuntime,
                combatRuntime,
                questTargets,
                deltaOnly,
                coldRestore,
                legacyFallback,
                exactRoundTrip,
                repeatedSave,
                logWritten,
                diagnostics,
                stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new NpcFactionAcceptanceReport(
                false,
                exception.Message,
                catalog.Factions.Count,
                catalog.Archetypes.Count,
                catalog.Agents.Count,
                catalog.Dialogues.Count,
                false, false, false, false, false, false, false, false,
                false, false, false, false, false,
                emptyDiagnostics,
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }
}
