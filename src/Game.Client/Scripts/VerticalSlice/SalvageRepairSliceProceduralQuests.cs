using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

public partial class SalvageRepairSlice
{
    private ProceduralQuestCatalog? _proceduralQuestCatalog;
    private ProceduralQuestRuntime? _proceduralQuestRuntime;
    private PanelContainer? _missionJournalPanel;
    private Label? _missionJournalLabel;
    private bool _missionJournalOpen;
    private int _missionJournalSelection;
    private string _missionJournalFeedback =
        "Enter accept/return/claim • Q/Esc close";
    private Task<ProceduralQuestAcceptanceReport>? _proceduralQuestAcceptanceTask;
    private ProceduralQuestAcceptanceReport? _proceduralQuestAcceptanceReport;
    private string _proceduralQuestAcceptanceHud = "READY";

    private ProceduralQuestCatalog ProceduralQuestCatalog =>
        _proceduralQuestCatalog ??
        throw new InvalidOperationException(
            "Procedural quest catalog is unavailable.");

    private ProceduralQuestRuntime ProceduralQuests =>
        _proceduralQuestRuntime ??
        throw new InvalidOperationException(
            "Procedural quest runtime is unavailable.");

    private void BindProceduralQuestSceneNodes()
    {
        _missionJournalPanel = GetNodeOrNull<PanelContainer>(
            "Hud/MissionJournal");
        _missionJournalLabel = GetNodeOrNull<Label>(
            "Hud/MissionJournal/Label");
        if (_missionJournalPanel is null || _missionJournalLabel is null)
        {
            throw new InvalidOperationException(
                "Vertical slice scene is missing the procedural mission journal HUD.");
        }
    }

    private static ProceduralQuestCatalog LoadProceduralQuestCatalog(
        StationServicesCatalog stationServicesCatalog)
    {
        const string path = "res://Content/procedural_quests.json";
        using Godot.FileAccess file = Godot.FileAccess.Open(
            path,
            Godot.FileAccess.ModeFlags.Read) ??
            throw new InvalidOperationException($"Unable to open {path}.");
        ProceduralQuestCatalog catalog = ProceduralQuestCatalog.LoadFromJson(
            file.GetAsText(),
            stationServicesCatalog);
        GD.Print(
            "TASK-118 procedural quest catalog READY: " +
            $"schema={catalog.SchemaVersion}; objectiveTypes={catalog.Profiles.Count}; " +
            $"board={catalog.BoardSize}; maxActive={catalog.MaximumActive}; " +
            $"seed={catalog.WorldSeed}; graph=objective>return>claim; " +
            "feasibility=capability-gated.");
        return catalog;
    }

    private ProceduralQuestCapabilities BuildProceduralQuestCapabilities(
        bool includeCombatTargets)
    {
        return ProceduralQuestCapabilityFactory.Create(
            ContentCatalog,
            StationServiceCatalog,
            BaseConstructionCatalog,
            PlanetaryPoiCatalog,
            EcologyCatalog,
            GalaxyNavigation,
            includeCombatTargets,
            defeatTargetIds: includeCombatTargets
                ? NpcFactionCatalog.DefeatTargetIds
                : null,
            protectTargetIds: includeCombatTargets
                ? NpcFactionCatalog.ProtectTargetIds
                : null);
    }

    private void InitializeProceduralQuestRuntime(
        ProceduralQuestSaveData? saveData)
    {
        _proceduralQuestRuntime = new ProceduralQuestRuntime(
            ProceduralQuestCatalog,
            BuildProceduralQuestCapabilities(includeCombatTargets: true),
            saveData);
        _missionJournalOpen = false;
        _missionJournalSelection = 0;
        _missionJournalFeedback = saveData is null
            ? "fresh/legacy deterministic mission board generated"
            : "mission board state restored exactly";
        if (_missionJournalPanel is not null)
        {
            _missionJournalPanel.Visible = false;
        }
        GD.Print(
            "TASK-118 procedural quests restore PASS: " +
            $"board={ProceduralQuests.Board.Count}; " +
            $"active={ProceduralQuests.AcceptedCount}; " +
            $"ready={ProceduralQuests.ReadyCount}; " +
            $"completed={ProceduralQuests.CompletedCount}; " +
            $"legacyFallback={(saveData is null ? 1 : 0)}.");
    }

    private bool HandleProceduralQuestInput(Key physical, Key logical)
    {
        if (_missionJournalOpen)
        {
            if (Matches(physical, logical, Key.Q) ||
                Matches(physical, logical, Key.Escape))
            {
                CloseMissionJournal(L("ui.quest.closed"));
            }
            else if (Matches(physical, logical, Key.Up))
            {
                MoveMissionJournalSelection(-1);
            }
            else if (Matches(physical, logical, Key.Down))
            {
                MoveMissionJournalSelection(1);
            }
            else if (Matches(physical, logical, Key.Enter))
            {
                ExecuteSelectedMissionAction();
            }
            return true;
        }

        // Q remains the legacy static-quest tab while Station Services is open
        // and remains ship roll input while the player is piloting.
        if (_stationServicesOpen || _selectorStation is not null ||
            _baseBuildMode || _discoveryCatalogOpen || _shipManagementOpen ||
            _galaxyMapOpen || _ecologyCatalogOpen ||
            _stageOneVoyageRuntime?.Piloted == true)
        {
            return false;
        }

        if (Matches(physical, logical, Key.Q) &&
            (_state == SalvageRepairSliceState.Ready ||
             _state == SalvageRepairSliceState.Passed))
        {
            OpenMissionJournal();
            return true;
        }
        return false;
    }

    private void OpenMissionJournal()
    {
        CloseRecipeSelector();
        CloseStationServices();
        CloseBaseBuildMode();
        CloseDiscoveryCatalog();
        CloseShipManagement();
        CloseGalaxyMap();
        CloseEcologyCatalog();
        _missionJournalOpen = true;
        _missionJournalSelection = Math.Clamp(
            _missionJournalSelection,
            0,
            Math.Max(0, ProceduralQuests.Views.Count - 1));
        _missionJournalPanel!.Visible = true;
        UpdateMissionJournalPanel();
        _status = L("ui.quest.journal_opened");
    }

    private void CloseMissionJournal(string status = "")
    {
        _missionJournalOpen = false;
        if (_missionJournalPanel is not null)
        {
            _missionJournalPanel.Visible = false;
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            _status = status;
        }
    }

    private void MoveMissionJournalSelection(int delta)
    {
        int count = ProceduralQuests.Views.Count;
        if (count <= 0)
        {
            _missionJournalSelection = 0;
            return;
        }
        _missionJournalSelection =
            (_missionJournalSelection + delta + count) % count;
        _missionJournalFeedback = string.Empty;
        UpdateMissionJournalPanel();
    }

    private ProceduralQuestView? GetSelectedMission()
    {
        IReadOnlyList<ProceduralQuestView> views = ProceduralQuests.Views;
        if (views.Count == 0)
        {
            return null;
        }
        _missionJournalSelection = Math.Clamp(
            _missionJournalSelection,
            0,
            views.Count - 1);
        return views[_missionJournalSelection];
    }

    private void ExecuteSelectedMissionAction()
    {
        ProceduralQuestView? selected = GetSelectedMission();
        if (selected is null)
        {
            return;
        }

        string questId = selected.Definition.QuestId;
        switch (selected.Status)
        {
            case ProceduralQuestStatus.Offered:
                if (ProceduralQuests.TryAccept(questId, out string accepted))
                {
                    _missionJournalFeedback = accepted;
                    _lastDomainEvent = $"ProceduralQuestAccepted({questId})";
                    QueueCurrentSnapshot(AutosaveTrigger.QuestCompleted);
                    GD.Print(
                        "TASK-118 player procedural quest accept PASS: " +
                        $"quest={questId}; type={selected.Definition.ObjectiveType}; " +
                        $"target={selected.Definition.TargetDefinitionId}; " +
                        $"active={ProceduralQuests.AcceptedCount}/" +
                        $"{ProceduralQuestCatalog.MaximumActive}.");
                }
                else
                {
                    _missionJournalFeedback = accepted;
                }
                break;

            case ProceduralQuestStatus.Accepted:
                if (selected.Definition.ObjectiveType ==
                    ProceduralQuestObjectiveType.DeliverItem)
                {
                    TryDeliverSelectedMission(selected);
                }
                else if (selected.Definition.ObjectiveType ==
                    ProceduralQuestObjectiveType.ReturnToNpc)
                {
                    TryReturnSelectedMission(selected);
                }
                else
                {
                    _missionJournalFeedback = L("ui.quest.objective_active");
                }
                break;

            case ProceduralQuestStatus.ReturnRequired:
                TryReturnSelectedMission(selected);
                break;

            case ProceduralQuestStatus.ReadyToClaim:
                TryClaimSelectedMission(selected);
                break;

            case ProceduralQuestStatus.Completed:
                _missionJournalFeedback = L("ui.quest.already_completed");
                break;
        }
        UpdateMissionJournalPanel();
    }

    private bool IsAtProceduralQuestGiver(ProceduralQuestDefinition definition)
    {
        if (!string.Equals(
                definition.GiverNpcId,
                StationServices.NpcId,
                StringComparison.Ordinal))
        {
            return false;
        }
        if (_stationServicesOpen ||
            StageOneVoyage.Location == StageOneVoyageLocation.OrbitalStation)
        {
            return true;
        }
        return _player is not null && _stationServicesNpc is not null &&
            _player.GlobalPosition.DistanceTo(_stationServicesNpc.GlobalPosition) <= 5.0f;
    }

    private void TryReturnSelectedMission(ProceduralQuestView selected)
    {
        if (!IsAtProceduralQuestGiver(selected.Definition))
        {
            _missionJournalFeedback = LF("ui.quest.return_to", ("npc", selected.Definition.GiverNpcId));
            return;
        }
        int changed = ProceduralQuests.RecordReturnToNpc(
            selected.Definition.GiverNpcId,
            out IReadOnlyList<string> changedIds);
        if (changed <= 0)
        {
            _missionJournalFeedback = L("ui.quest.return_no_change");
            return;
        }
        _missionJournalFeedback = L("ui.quest.return_ready");
        _lastDomainEvent =
            $"ProceduralQuestReturned({string.Join(",", changedIds)})";
        QueueCurrentSnapshot(AutosaveTrigger.QuestCompleted);
        GD.Print(
            "TASK-118 player procedural quest return PASS: " +
            $"giver={selected.Definition.GiverNpcId}; changed={changed}; " +
            $"ready={ProceduralQuests.ReadyCount}.");
    }

    private void TryDeliverSelectedMission(ProceduralQuestView selected)
    {
        if (!IsAtProceduralQuestGiver(selected.Definition))
        {
            _missionJournalFeedback = LF("ui.quest.deliver_at", ("npc", selected.Definition.GiverNpcId));
            return;
        }
        int remaining = Math.Max(
            0,
            selected.Definition.RequiredQuantity - selected.Progress);
        if (remaining <= 0)
        {
            TryReturnSelectedMission(selected);
            return;
        }
        if (!TryConsumeSharedInventory(
                selected.Definition.TargetDefinitionId,
                remaining,
                out string inventoryResult))
        {
            _missionJournalFeedback = inventoryResult;
            return;
        }
        RecordProceduralQuestObjective(
            ProceduralQuestObjectiveType.DeliverItem,
            selected.Definition.TargetDefinitionId,
            remaining,
            queueAutosave: false);
        ProceduralQuests.RecordReturnToNpc(
            selected.Definition.GiverNpcId,
            out _);
        _missionJournalFeedback = LF("ui.quest.delivered", ("quantity", remaining), ("item", selected.Definition.TargetDefinitionId));
        QueueCurrentSnapshot(AutosaveTrigger.QuestCompleted);
        GD.Print(
            "TASK-118 player procedural delivery PASS: " +
            $"quest={selected.Definition.QuestId}; " +
            $"item={selected.Definition.TargetDefinitionId}; quantity={remaining}.");
    }

    private void TryClaimSelectedMission(ProceduralQuestView selected)
    {
        if (!IsAtProceduralQuestGiver(selected.Definition))
        {
            _missionJournalFeedback = LF("ui.quest.claim_at", ("npc", selected.Definition.GiverNpcId));
            return;
        }
        if (!ProceduralQuests.TryClaim(
                selected.Definition.QuestId,
                out int credits,
                out int reputation,
                out string factionId,
                out string result))
        {
            _missionJournalFeedback = result;
            return;
        }
        string rewardResult = StationServices.GrantExternalQuestReward(
            factionId,
            credits,
            reputation);
        if (_npcFactionRuntime is not null &&
            NpcFactionCatalog.Factions.ContainsKey(factionId))
        {
            NpcFactions.ApplyReputationDelta(factionId, reputation);
        }
        _missionJournalFeedback = LF("ui.quest.reward", ("result", result), ("credits", credits), ("reputation", reputation));
        _lastDomainEvent =
            $"ProceduralQuestCompleted({selected.Definition.QuestId})";
        QueueCurrentSnapshot(AutosaveTrigger.QuestCompleted);
        GD.Print(
            "TASK-118 player procedural quest completion PASS: " +
            $"quest={selected.Definition.QuestId}; type={selected.Definition.ObjectiveType}; " +
            $"credits={credits}; reputation={reputation}; faction={factionId}; " +
            $"completed={ProceduralQuests.CompletedCount}/{ProceduralQuests.Board.Count}; " +
            $"economy={rewardResult}.");
    }

    private int RecordProceduralQuestObjective(
        ProceduralQuestObjectiveType type,
        string targetDefinitionId,
        int quantity,
        bool queueAutosave = true)
    {
        if (_proceduralQuestRuntime is null || quantity <= 0)
        {
            return 0;
        }
        int changed = ProceduralQuests.RecordObjective(
            type,
            targetDefinitionId,
            quantity,
            out IReadOnlyList<string> changedIds);
        if (changed <= 0)
        {
            return 0;
        }
        _lastDomainEvent =
            $"ProceduralQuestProgress({type},{targetDefinitionId},{changed})";
        if (queueAutosave)
        {
            QueueCurrentSnapshot(AutosaveTrigger.QuestCompleted);
        }
        GD.Print(
            "TASK-118 player procedural quest progress PASS: " +
            $"type={type}; target={targetDefinitionId}; quantity={quantity}; " +
            $"changed={changed}; quests={string.Join(",", changedIds)}; " +
            $"active={ProceduralQuests.AcceptedCount}; ready={ProceduralQuests.ReadyCount}.");
        if (_missionJournalOpen)
        {
            UpdateMissionJournalPanel();
        }
        return changed;
    }

    private void RecordProceduralQuestReturnAtCurrentNpc()
    {
        if (_proceduralQuestRuntime is null || _stationServicesNpc is null)
        {
            return;
        }
        int changed = ProceduralQuests.RecordReturnToNpc(
            StationServices.NpcId,
            out IReadOnlyList<string> changedIds);
        if (changed <= 0)
        {
            return;
        }
        QueueCurrentSnapshot(AutosaveTrigger.QuestCompleted);
        GD.Print(
            "TASK-118 player procedural quest return PASS: " +
            $"giver={StationServices.NpcId}; changed={changed}; " +
            $"quests={string.Join(",", changedIds)}; ready={ProceduralQuests.ReadyCount}.");
    }

    private void UpdateMissionJournalPanel()
    {
        if (!_missionJournalOpen || _missionJournalLabel is null) return;
        IReadOnlyList<ProceduralQuestView> views = ProceduralQuests.Views;
        if (views.Count == 0)
        {
            _missionJournalLabel.Text = L("ui.quest.no_missions");
            return;
        }
        _missionJournalSelection = Math.Clamp(_missionJournalSelection, 0, views.Count - 1);
        int start = Math.Max(0, _missionJournalSelection - 6);
        int end = Math.Min(views.Count, start + 13);
        start = Math.Max(0, end - 13);
        List<string> lines = new();
        for (int index = start; index < end; index++)
        {
            ProceduralQuestView view = views[index];
            string marker = index == _missionJournalSelection ? ">" : " ";
            string status = L($"ui.quest.state.{view.Status.ToString().ToLowerInvariant()}");
            lines.Add($"{marker} " + LF("ui.quest.row",
                ("number", (index + 1).ToString("00", CultureInfo.InvariantCulture)), ("status", status),
                ("objective", view.Definition.ObjectiveType), ("target", GetShortContentId(view.Definition.TargetDefinitionId)),
                ("progress", view.Progress), ("required", view.Definition.RequiredQuantity), ("credits", view.Definition.RewardCredits)));
        }
        ProceduralQuestView selected = views[_missionJournalSelection];
        string factionRep = string.Join(" • ", StationServiceCatalog.Factions.Keys.OrderBy(value => value, StringComparer.Ordinal)
            .Select(faction => $"{GetShortContentId(faction)}={ProceduralQuests.GetFactionReputation(faction)}"));
        _missionJournalLabel.Text = string.Join("\n", new[]
        {
            L("ui.quest.header"),
            LF("ui.quest.board_summary", ("board", views.Count), ("active", ProceduralQuests.AcceptedCount), ("maxActive", ProceduralQuestCatalog.MaximumActive), ("ready", ProceduralQuests.ReadyCount), ("completed", ProceduralQuests.CompletedCount)),
            LF("ui.quest.faction_reputation", ("reputation", factionRep)),
            "",
            string.Join("\n", lines),
            "",
            L("ui.quest.selected"),
            selected.Definition.QuestId,
            LF("ui.quest.selected_detail", ("faction", selected.Definition.FactionId), ("giver", selected.Definition.GiverNpcId), ("target", selected.Definition.TargetDefinitionId), ("credits", selected.Definition.RewardCredits), ("reputation", selected.Definition.ReputationReward)),
            L("ui.quest.controls"),
            LF("ui.quest.status", ("status", _missionJournalFeedback))
        });
    }

    private string BuildProceduralQuestHudLine()
    {
        if (_proceduralQuestRuntime is null)
        {
            return L("ui.hud.missions.unavailable");
        }
        return LF(
            "ui.hud.missions.summary",
            ("board", ProceduralQuests.Board.Count),
            ("active", ProceduralQuests.AcceptedCount),
            ("maxActive", ProceduralQuestCatalog.MaximumActive),
            ("ready", ProceduralQuests.ReadyCount),
            ("completed", ProceduralQuests.CompletedCount));
    }

    private void BeginProceduralQuestAcceptance(string directory)
    {
        string testPath = Path.Combine(
            directory,
            "save_1.procedural-quests-test.db");
        _proceduralQuestAcceptanceHud = "RUNNING";
        _proceduralQuestAcceptanceReport = null;
        _proceduralQuestAcceptanceTask = ProceduralQuestAcceptanceRunner.RunAsync(
            testPath,
            SlotId,
            ProceduralQuestCatalog,
            BuildProceduralQuestCapabilities(includeCombatTargets: true),
            BuildProceduralQuestCapabilities(includeCombatTargets: true),
            RepairRecipe,
            _lifetimeCancellation.Token);
    }

    private void PollProceduralQuestAcceptanceTask()
    {
        if (_proceduralQuestAcceptanceTask is null ||
            !_proceduralQuestAcceptanceTask.IsCompleted)
        {
            return;
        }
        try
        {
            _proceduralQuestAcceptanceReport =
                _proceduralQuestAcceptanceTask.GetAwaiter().GetResult();
            ProceduralQuestAcceptanceReport report =
                _proceduralQuestAcceptanceReport;
            _proceduralQuestAcceptanceHud = report.Passed
                ? $"PASS types={report.ObjectiveTypes}, generated={report.GeneratedQuests}, deterministic={(report.Deterministic ? 1 : 0)}, feasibility={(report.Feasibility ? 1 : 0)}, lifecycle={(report.ObjectiveLifecycle ? 1 : 0)}, restore={(report.ColdRestore ? 1 : 0)}"
                : $"FAIL: {report.Result}";
            string line =
                $"TASK-118 procedural quests acceptance {(report.Passed ? "PASS" : "FAIL")}: " +
                $"objectiveTypes={report.ObjectiveTypes}; generated={report.GeneratedQuests}; " +
                $"deterministic={(report.Deterministic ? 1 : 0)}; " +
                $"allTypes={(report.AllTypesSupported ? 1 : 0)}; " +
                $"feasibility={(report.Feasibility ? 1 : 0)}; " +
                $"infeasibleRejected={(report.InfeasibleRejected ? 1 : 0)}; " +
                $"activeLimit={(report.ActiveLimit ? 1 : 0)}; " +
                $"lifecycle={(report.ObjectiveLifecycle ? 1 : 0)}; " +
                $"return={(report.ReturnLifecycle ? 1 : 0)}; " +
                $"rewards={(report.RewardLifecycle ? 1 : 0)}; " +
                $"gameplayBoard={(report.GeneratedBoardPlayable ? 1 : 0)}; " +
                $"coldRestore={(report.ColdRestore ? 1 : 0)}; " +
                $"legacyFallback={(report.LegacyFallback ? 1 : 0)}; " +
                $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}; " +
                $"logWritten={(report.LogWritten ? 1 : 0)}; " +
                $"maxWriters={report.Diagnostics.MaximumConcurrentWriters}; " +
                $"integrity={report.Diagnostics.IntegrityResult}; " +
                $"elapsedMs={report.ElapsedMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)}; " +
                $"result={report.Result}";
            if (report.Passed)
            {
                GD.Print(line);
            }
            else
            {
                GD.PushError(line);
            }
            UpdateCombinedCatalogAndShipAcceptanceState();
        }
        catch (Exception exception)
        {
            _proceduralQuestAcceptanceHud = $"FAIL: {exception.Message}";
            GD.PushError(
                "TASK-118 procedural quests acceptance FAIL: " +
                exception.Message);
        }
        finally
        {
            _proceduralQuestAcceptanceTask = null;
        }
    }
}
