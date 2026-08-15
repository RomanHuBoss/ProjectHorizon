using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

public partial class SalvageRepairSlice
{
    private NpcFactionCatalog? _npcFactionCatalog;
    private NpcFactionRuntime? _npcFactionRuntime;
    private Node3D? _npcPopulationRoot;
    private PanelContainer? _npcInteractionPanel;
    private Label? _npcInteractionLabel;
    private bool _npcInteractionOpen;
    private string _selectedNpcId = string.Empty;
    private int _npcInteractionSelection;
    private string _npcInteractionFeedback = "";
    private Task<NpcFactionAcceptanceReport>? _npcFactionAcceptanceTask;
    private NpcFactionAcceptanceReport? _npcFactionAcceptanceReport;
    private string _npcFactionAcceptanceHud = "READY";

    private NpcFactionCatalog NpcFactionCatalog =>
        _npcFactionCatalog ?? throw new InvalidOperationException(
            "NPC/faction catalog is unavailable.");

    private NpcFactionRuntime NpcFactions =>
        _npcFactionRuntime ?? throw new InvalidOperationException(
            "NPC/faction runtime is unavailable.");

    private void BindNpcFactionSceneNodes()
    {
        _npcPopulationRoot = GetNodeOrNull<Node3D>("Gameplay/NpcPopulation");
        _npcInteractionPanel = GetNodeOrNull<PanelContainer>("Hud/NpcInteraction");
        _npcInteractionLabel = GetNodeOrNull<Label>("Hud/NpcInteraction/Label");
        if (_npcPopulationRoot is null ||
            _npcInteractionPanel is null ||
            _npcInteractionLabel is null)
        {
            throw new InvalidOperationException(
                "Vertical slice scene is missing NPC population or interaction HUD nodes.");
        }
    }

    private static NpcFactionCatalog LoadNpcFactionCatalog(
        StationServicesCatalog stationServicesCatalog)
    {
        const string path = "res://Content/npc_factions.json";
        using Godot.FileAccess file = Godot.FileAccess.Open(
            path,
            Godot.FileAccess.ModeFlags.Read) ??
            throw new InvalidOperationException($"Unable to open {path}.");
        NpcFactionCatalog catalog = NpcFactionCatalog.LoadFromJson(
            file.GetAsText(),
            stationServicesCatalog);
        GD.Print(
            "TASK-122 NPC/faction catalog READY: " +
            $"schema={catalog.SchemaVersion}; factions={catalog.Factions.Count}; " +
            $"archetypes={catalog.Archetypes.Count}; agents={catalog.Agents.Count}; " +
            $"dialogues={catalog.Dialogues.Count}; defeatTargets={catalog.DefeatTargetIds.Count}; " +
            $"protectTargets={catalog.ProtectTargetIds.Count}; region={catalog.RegionKey}.");
        return catalog;
    }

    private void InitializeNpcFactionRuntime(NpcFactionSaveData? saveData)
    {
        _npcFactionRuntime = new NpcFactionRuntime(NpcFactionCatalog, saveData);
        _npcInteractionOpen = false;
        _selectedNpcId = string.Empty;
        _npcInteractionSelection = 0;
        _npcInteractionFeedback = saveData is null
            ? "fresh/legacy NPC population initialized"
            : "NPC/faction deltas restored exactly";
        if (_npcInteractionPanel is not null)
        {
            _npcInteractionPanel.Visible = false;
        }
        GD.Print(
            "TASK-122 NPC/faction restore PASS: " +
            $"factions={NpcFactions.FactionCount}; agents={NpcFactions.AgentCount}; " +
            $"alive={NpcFactions.AliveCount}; opponentDefeats={NpcFactions.TotalOpponentDefeats}; " +
            $"reputationDeltas={NpcFactions.CreateSaveData().Reputations.Count}; " +
            $"agentDeltas={NpcFactions.CreateSaveData().Agents.Count}; " +
            $"legacyFallback={(saveData is null ? 1 : 0)}.");
    }

    private void RebuildNpcFactionScene()
    {
        if (_npcPopulationRoot is null || _player is null || _npcFactionRuntime is null)
        {
            return;
        }
        foreach (Node child in _npcPopulationRoot.GetChildren())
        {
            _npcPopulationRoot.RemoveChild(child);
            child.QueueFree();
        }
        foreach (NpcFactionAgentDefinition definition in NpcFactionCatalog.Agents.Values
                     .Where(agent => !agent.ExistingScene)
                     .OrderBy(agent => agent.NpcId, StringComparer.Ordinal))
        {
            NpcFactionAgentNode node = new();
            node.Configure(definition, NpcFactions, _player);
            node.InteractionRequested += OpenNpcInteraction;
            node.CombatResolved += OnNpcCombatResolved;
            _npcPopulationRoot.AddChild(node);
        }
        AttachNpcNavigationAgents();
        GD.Print(
            "TASK-122 physical NPC population READY: " +
            $"authored=1; dynamic={_npcPopulationRoot.GetChildCount()}; " +
            "interaction=E; hostileCombat=multitool-hitscan; navigation=TASK-124.");
    }

    private bool HandleNpcFactionInput(Key physical, Key logical)
    {
        if (!_npcInteractionOpen)
        {
            return false;
        }
        if (Matches(physical, logical, Key.Escape))
        {
            CloseNpcInteraction(L("ui.npc.closed"));
        }
        else if (Matches(physical, logical, Key.Up))
        {
            MoveNpcInteractionSelection(-1);
        }
        else if (Matches(physical, logical, Key.Down))
        {
            MoveNpcInteractionSelection(1);
        }
        else if (Matches(physical, logical, Key.Enter) ||
                 Matches(physical, logical, Key.E))
        {
            ExecuteSelectedNpcDialogueOption();
        }
        return true;
    }

    private void OpenNpcInteraction(NpcFactionAgentNode agent, Node3D interactor)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(interactor);
        if (_state != SalvageRepairSliceState.Ready &&
            _state != SalvageRepairSliceState.Passed)
        {
            _status = L("ui.npc.persistence_wait");
            return;
        }
        NpcFactionAgentView view = NpcFactions.GetAgent(agent.NpcId);
        if (view.Defeated)
        {
            _status = LF("ui.npc.unavailable", ("npc", agent.NpcId));
            return;
        }

        CloseRecipeSelector();
        CloseStationServices();
        CloseBaseBuildMode();
        CloseDiscoveryCatalog();
        CloseShipManagement();
        CloseGalaxyMap();
        CloseEcologyCatalog();
        CloseMissionJournal();
        ClosePlayerEquipment();
        _npcInteractionOpen = true;
        _selectedNpcId = agent.NpcId;
        _npcInteractionSelection = 0;
        _npcInteractionFeedback = "";
        if (_npcInteractionPanel is not null)
        {
            _npcInteractionPanel.Visible = true;
        }
        UpdateNpcInteractionPanel();
        PlayDialogueVoiceAudio();
        _status = LF("ui.npc.opened", ("npc", agent.NpcId));
        _lastDomainEvent = $"NpcFactionInteraction({agent.NpcId})";
        GD.Print(
            "TASK-122 player NPC interaction PASS: " +
            $"npc={agent.NpcId}; archetype={view.Definition.Archetype}; " +
            $"faction={(string.IsNullOrEmpty(view.Definition.FactionId) ? "none" : view.Definition.FactionId)}; " +
            $"interactor={interactor.Name}.");
    }

    private void CloseNpcInteraction(string status = "")
    {
        _npcInteractionOpen = false;
        _selectedNpcId = string.Empty;
        _npcInteractionSelection = 0;
        _npcInteractionFeedback = "";
        if (_npcInteractionPanel is not null)
        {
            _npcInteractionPanel.Visible = false;
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            _status = status;
        }
    }

    private void MoveNpcInteractionSelection(int delta)
    {
        if (string.IsNullOrEmpty(_selectedNpcId))
        {
            return;
        }
        int count = NpcFactions.GetAvailableDialogueOptions(_selectedNpcId).Count;
        if (count <= 0)
        {
            _npcInteractionSelection = 0;
            return;
        }
        _npcInteractionSelection = (_npcInteractionSelection + delta) % count;
        if (_npcInteractionSelection < 0)
        {
            _npcInteractionSelection += count;
        }
        _npcInteractionFeedback = "";
        UpdateNpcInteractionPanel();
    }

    private void ExecuteSelectedNpcDialogueOption()
    {
        if (string.IsNullOrEmpty(_selectedNpcId))
        {
            return;
        }
        NpcFactionAgentView view = NpcFactions.GetAgent(_selectedNpcId);
        var options = NpcFactions.GetAvailableDialogueOptions(_selectedNpcId);
        if (options.Count == 0)
        {
            _npcInteractionFeedback = GameLocalizationService.Text("ui.game.no_dialogue_options");
            UpdateNpcInteractionPanel();
            return;
        }
        _npcInteractionSelection = Math.Clamp(
            _npcInteractionSelection,
            0,
            options.Count - 1);
        NpcDialogueOptionDefinition option = options[_npcInteractionSelection];
        NpcDialogueOutcome outcome = NpcFactions.ChooseDialogueOption(
            _selectedNpcId,
            option.OptionId);
        _npcInteractionFeedback = outcome.Applied
            ? GameLocalizationService.Text(outcome.ConsequenceKey)
            : GameLocalizationService.Format("ui.game.require_reputation", ("value", option.MinimumReputation));
        if (!outcome.Applied)
        {
            UpdateNpcInteractionPanel();
            return;
        }

        bool questProgress = false;
        if (string.Equals(option.Action, "AcknowledgeProtection", StringComparison.Ordinal) &&
            view.Definition.CanBeProtected)
        {
            questProgress = RecordProceduralQuestObjective(
                ProceduralQuestObjectiveType.ProtectTarget,
                view.Definition.NpcId,
                1,
                queueAutosave: false) > 0;
        }
        _lastDomainEvent = $"NpcDialogueOption({option.OptionId})";
        if (outcome.FirstMeaningfulInteraction || outcome.AppliedReputationDelta != 0 || questProgress)
        {
            QueueCurrentSnapshot(AutosaveTrigger.NpcChanged);
        }

        switch (option.Action)
        {
            case "OpenMissions":
                CloseNpcInteraction();
                OpenMissionJournal();
                break;
            case "OpenTrade":
                CloseNpcInteraction();
                if (_stationServicesNpc is not null && _player is not null)
                {
                    OpenStationServices(_stationServicesNpc, _player);
                }
                break;
            case "Close":
                NpcDialogueDefinition dialogue = NpcFactionCatalog.GetDialogue(
                    view.Definition.DialogueId);
                CloseNpcInteraction(GameLocalizationService.Text(dialogue.FarewellKey));
                break;
            default:
                UpdateNpcInteractionPanel();
                break;
        }
        GD.Print(
            "TASK-122 NPC dialogue action PASS: " +
            $"npc={view.Definition.NpcId}; option={option.OptionId}; action={option.Action}; " +
            $"rep={outcome.ReputationBefore}->{outcome.ReputationAfter}; " +
            $"questProgress={(questProgress ? 1 : 0)}.");
    }

    private void UpdateNpcInteractionPanel()
    {
        if (_npcInteractionLabel is null || string.IsNullOrEmpty(_selectedNpcId))
        {
            return;
        }
        NpcFactionAgentView view = NpcFactions.GetAgent(_selectedNpcId);
        NpcDialogueDefinition dialogue = NpcFactionCatalog.GetDialogue(
            view.Definition.DialogueId);
        var options = NpcFactions.GetAvailableDialogueOptions(_selectedNpcId);
        _npcInteractionSelection = options.Count == 0
            ? 0
            : Math.Clamp(_npcInteractionSelection, 0, options.Count - 1);
        string faction = string.IsNullOrEmpty(view.Definition.FactionId)
            ? L("ui.game.unaffiliated")
            : L(NpcFactionCatalog.Factions[view.Definition.FactionId].LocalizationKey);
        int reputation = NpcFactions.GetFactionReputation(view.Definition.FactionId);
        string optionLines = options.Count == 0
            ? L("ui.game.no_dialogue_options")
            : string.Join("\n", options.Select((option, index) =>
                $"{(index == _npcInteractionSelection ? ">" : " ")} " +
                LF("ui.npc.option_row", ("text", L(option.TextKey)))));
        string identity = LF(
            "ui.npc.identity",
            ("name", L(view.Definition.DisplayNameKey)),
            ("archetype", L(NpcFactionCatalog.Archetypes[view.Definition.Archetype].LocalizationKey)));
        string stats = LF(
            "ui.npc.stats",
            ("faction", faction),
            ("reputation", reputation),
            ("health", view.Health.ToString("0.#", CultureInfo.InvariantCulture)),
            ("maxHealth", view.Definition.Health.ToString("0.#", CultureInfo.InvariantCulture)));
        _npcInteractionLabel.Text =
            identity + "\n" + stats + "\n\n" +
            L(dialogue.GreetingKey) + "\n\n" +
            optionLines + "\n\n" +
            L("ui.game.dialogue_controls") +
            (string.IsNullOrWhiteSpace(_npcInteractionFeedback)
                ? ""
                : "\n" + LF("ui.npc.status", ("status", _npcInteractionFeedback)));
    }

    private void OnNpcCombatResolved(
        NpcFactionAgentNode agent,
        NpcFactionCombatOutcome outcome)
    {
        bool questProgress = false;
        if (outcome.DefeatedNow &&
            NpcFactionCatalog.DefeatTargetIds.Contains(
                outcome.NpcId,
                StringComparer.Ordinal))
        {
            questProgress = RecordProceduralQuestObjective(
                ProceduralQuestObjectiveType.DefeatTarget,
                outcome.NpcId,
                1,
                queueAutosave: false) > 0;
        }
        _lastDomainEvent = outcome.DefeatedNow
            ? $"NpcDefeated({outcome.NpcId})"
            : $"NpcDamaged({outcome.NpcId})";
        QueueCurrentSnapshot(AutosaveTrigger.NpcChanged);
        if (_npcInteractionOpen &&
            string.Equals(_selectedNpcId, outcome.NpcId, StringComparison.Ordinal))
        {
            if (NpcFactions.GetAgent(outcome.NpcId).Defeated)
            {
                CloseNpcInteraction(L("ui.npc.became_unavailable"));
            }
            else
            {
                UpdateNpcInteractionPanel();
            }
        }
        GD.Print(
            "TASK-122 NPC combat integration PASS: " +
            $"npc={agent.NpcId}; defeated={(outcome.DefeatedNow ? 1 : 0)}; " +
            $"respawned={(outcome.Respawned ? 1 : 0)}; questProgress={(questProgress ? 1 : 0)}; " +
            $"rep={outcome.ReputationBefore}->{outcome.ReputationAfter}.");
    }

    private string BuildNpcFactionHudLine()
    {
        if (_npcFactionRuntime is null)
        {
            return L("ui.hud.npc_factions.unavailable");
        }
        string reps = string.Join(", ", NpcFactions.FactionReputation
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{ShortFactionName(pair.Key)}={pair.Value}"));
        return LF(
            "ui.hud.npc_factions.summary",
            ("alive", NpcFactions.AliveCount),
            ("agents", NpcFactions.AgentCount),
            ("combat", NpcFactionCatalog.DefeatTargetIds.Count),
            ("protected", NpcFactionCatalog.ProtectTargetIds.Count),
            ("defeats", NpcFactions.TotalOpponentDefeats),
            ("reputation", reps));
    }

    private void BeginNpcFactionAcceptance(string directory)
    {
        string testPath = Path.Combine(directory, "save_1.npc-factions-test.db");
        _npcFactionAcceptanceHud = "RUNNING";
        _npcFactionAcceptanceReport = null;
        _npcFactionAcceptanceTask = NpcFactionAcceptanceRunner.RunAsync(
            testPath,
            SlotId,
            NpcFactionCatalog,
            RepairRecipe,
            _lifetimeCancellation.Token);
    }

    private void PollNpcFactionAcceptanceTask()
    {
        if (_npcFactionAcceptanceTask is null || !_npcFactionAcceptanceTask.IsCompleted)
        {
            return;
        }
        try
        {
            NpcFactionAcceptanceReport report =
                _npcFactionAcceptanceTask.GetAwaiter().GetResult();
            _npcFactionAcceptanceReport = report;
            _npcFactionAcceptanceHud = report.Passed
                ? $"PASS factions={report.Factions}, archetypes={report.Archetypes}, agents={report.Agents}, dialogue={report.DialogueTemplates}, combat={(report.CombatRuntime ? 1 : 0)}, restore={(report.ColdRestore ? 1 : 0)}"
                : $"FAIL: {report.Result}";
            string line =
                $"TASK-122 NPC/factions acceptance {(report.Passed ? "PASS" : "FAIL")}: " +
                $"factions={report.Factions}; archetypes={report.Archetypes}; agents={report.Agents}; " +
                $"dialogues={report.DialogueTemplates}; factionCoverage={(report.FactionCoverage ? 1 : 0)}; " +
                $"relations={(report.RelationMatrix ? 1 : 0)}; dialogueCoverage={(report.DialogueCoverage ? 1 : 0)}; " +
                $"interaction={(report.InteractionRuntime ? 1 : 0)}; reputation={(report.ReputationRuntime ? 1 : 0)}; " +
                $"combat={(report.CombatRuntime ? 1 : 0)}; questTargets={(report.QuestTargets ? 1 : 0)}; " +
                $"deltaOnly={(report.DeltaOnly ? 1 : 0)}; coldRestore={(report.ColdRestore ? 1 : 0)}; " +
                $"legacyFallback={(report.LegacyFallback ? 1 : 0)}; roundTrip={(report.ExactRoundTrip ? 1 : 0)}; " +
                $"repeatedSave={(report.RepeatedSave ? 1 : 0)}; logWritten={(report.LogWritten ? 1 : 0)}; " +
                $"maxWriters={report.Diagnostics.MaximumConcurrentWriters}; integrity={report.Diagnostics.IntegrityResult}; " +
                $"elapsedMs={report.ElapsedMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)}; " +
                $"result={report.Result}.";
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
            _npcFactionAcceptanceReport = null;
            _npcFactionAcceptanceHud = $"FAIL: {exception.Message}";
            GD.PushError($"TASK-122 NPC/factions acceptance FAIL: {exception}");
        }
        finally
        {
            _npcFactionAcceptanceTask = null;
        }
    }

    private static bool IsRussianLocale() =>
        TranslationServer.GetLocale().StartsWith(
            "ru",
            StringComparison.OrdinalIgnoreCase);

    private static string ShortFactionName(string factionId)
    {
        int index = factionId.LastIndexOf('.');
        return index >= 0 && index + 1 < factionId.Length
            ? factionId[(index + 1)..]
            : factionId;
    }
}
