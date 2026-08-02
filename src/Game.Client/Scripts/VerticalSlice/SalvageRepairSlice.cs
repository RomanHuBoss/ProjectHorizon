using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;

public enum SalvageRepairSliceState
{
    Initializing = 0,
    Loading = 1,
    Ready = 2,
    Saving = 3,
    Testing = 4,
    Passed = 5,
    Failed = 6,
    Exiting = 7
}

public enum SalvageRepairHudMode
{
    Detailed = 0,
    Compact = 1,
    Hidden = 2
}

public enum StationSelectorMode
{
    Recipes = 0,
    Research = 1
}

public partial class SalvageRepairSlice : Node3D
{
    private sealed record GracefulExitResult(
        bool Saved,
        int Revision);

    private const string SlotId = StarterRepairSnapshotFactory.SlotId;
    private const int DefaultResearchPoints =
        TechnologyRecipeSelectorAcceptanceRunner.DefaultResearchPoints;

    [Export(PropertyHint.Range, "5.0,600.0,5.0")]
    public double AutosaveIntervalSeconds { get; set; } = 60.0;

    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly List<SalvageResourceNode> _resourceNodes = new();
    private readonly List<PortableCraftingStation> _craftingStations = new();
    private readonly Dictionary<string, CraftingRecipeDefinition>
        _stationRecipes = new(StringComparer.Ordinal);
    private readonly DataDrivenCraftTimer _craftTimer = new();
    private SaveDatabase? _database;
    private SaveAutosaveCoordinator? _autosave;
    private GameContentCatalog? _contentCatalog;
    private TechnologyProgression? _technologyProgression;
    private CraftingRecipeDefinition? _repairRecipe;
    private CraftingRecipeDefinition? _launchCapacitorRecipe;
    private CraftingRecipeDefinition? _navigationArrayRecipe;
    private CraftingRecipeDefinition? _coolantRegulatorRecipe;
    private CraftingRecipeDefinition? _powerCouplerRecipe;
    private StarterRepairSession? _session;
    private StarterShipRepairTerminal? _shipTerminal;
    private PortableCraftingStation? _activeCraftingStation;
    private PlayerController? _player;
    private MarginContainer? _hudMargin;
    private Label? _hudLabel;
    private PanelContainer? _hudHiddenHint;
    private PanelContainer? _recipeSelectorPanel;
    private Label? _recipeSelectorLabel;
    private Task<SaveDatabaseDiagnostics>? _initializeTask;
    private Task<SaveGameSnapshot?>? _loadTask;
    private Task? _resetTask;
    private Task<VerticalSliceAcceptanceReport>? _acceptanceTask;
    private Task<DataDrivenContentAcceptanceReport>? _contentAcceptanceTask;
    private Task<CraftingExpansionAcceptanceReport>? _craftingAcceptanceTask;
    private Task<CraftTimeAcceptanceReport>? _craftTimeAcceptanceTask;
    private Task<ThirdCraftingPathAcceptanceReport>? _thirdCraftingAcceptanceTask;
    private Task<FourthCraftingPathAcceptanceReport>? _fourthCraftingAcceptanceTask;
    private Task<CatalogCraftingMatrixAcceptanceReport>? _catalogMatrixAcceptanceTask;
    private Task<TechnologyRecipeSelectorAcceptanceReport>?
        _technologySelectorAcceptanceTask;
    private Task<ChemicalProcessAcceptanceReport>?
        _chemicalProcessAcceptanceTask;
    private Task<ProductionQueueAcceptanceReport>?
        _productionQueueAcceptanceTask;
    private Task<GracefulExitResult>? _gracefulExitTask;
    private SaveDatabaseDiagnostics? _diagnostics;
    private VerticalSliceAcceptanceReport? _acceptanceReport;
    private DataDrivenContentAcceptanceReport? _contentAcceptanceReport;
    private CraftingExpansionAcceptanceReport? _craftingAcceptanceReport;
    private CraftTimeAcceptanceReport? _craftTimeAcceptanceReport;
    private ThirdCraftingPathAcceptanceReport? _thirdCraftingAcceptanceReport;
    private FourthCraftingPathAcceptanceReport? _fourthCraftingAcceptanceReport;
    private CatalogCraftingMatrixAcceptanceReport? _catalogMatrixAcceptanceReport;
    private TechnologyRecipeSelectorAcceptanceReport?
        _technologySelectorAcceptanceReport;
    private ChemicalProcessAcceptanceReport?
        _chemicalProcessAcceptanceReport;
    private ProductionQueueAcceptanceReport?
        _productionQueueAcceptanceReport;
    private SalvageRepairSliceState _state =
        SalvageRepairSliceState.Initializing;
    private SalvageRepairHudMode _hudMode =
        SalvageRepairHudMode.Detailed;
    private int _revision;
    private int _observedAutosaveBatches;
    private int _observedAutosaveFailures;
    private double _autosaveElapsedSeconds;
    private bool _closeRequested;
    private bool _previousAutoAcceptQuit = true;
    private string _status = "initializing SQLite";
    private string _acceptanceHud = "READY";
    private string _contentAcceptanceHud = "READY";
    private string _craftingAcceptanceHud = "READY";
    private string _craftTimeAcceptanceHud = "READY";
    private string _thirdCraftingAcceptanceHud = "READY";
    private string _fourthCraftingAcceptanceHud = "READY";
    private string _catalogMatrixAcceptanceHud = "READY";
    private string _industryCatalogAcceptanceHud = "READY";
    private string _technologySelectorAcceptanceHud = "READY";
    private string _chemicalProcessAcceptanceHud = "READY";
    private string _productionQueueAcceptanceHud = "READY";
    private PortableCraftingStation? _selectorStation;
    private Node3D? _selectorInteractor;
    private StationSelectorMode _selectorMode = StationSelectorMode.Recipes;
    private int _selectorIndex;
    private string _selectorFeedback = "";
    private ulong _selectorOpenedTicks;
    private string _craftingInteractorName = "unknown";
    private string _lastDomainEvent = "none";

    private StarterRepairSession Session => _session ??
        throw new InvalidOperationException("Starter repair session is unavailable.");

    private GameContentCatalog ContentCatalog => _contentCatalog ??
        throw new InvalidOperationException("Game content catalog is unavailable.");

    private TechnologyProgression TechnologyProgress =>
        _technologyProgression ??
        throw new InvalidOperationException(
            "Technology progression is unavailable.");

    private CraftingRecipeDefinition RepairRecipe => _repairRecipe ??
        throw new InvalidOperationException("Starter repair recipe is unavailable.");

    private CraftingRecipeDefinition LaunchCapacitorRecipe =>
        _launchCapacitorRecipe ??
        throw new InvalidOperationException(
            "Launch capacitor recipe is unavailable.");

    private CraftingRecipeDefinition NavigationArrayRecipe =>
        _navigationArrayRecipe ??
        throw new InvalidOperationException(
            "Navigation array recipe is unavailable.");

    private CraftingRecipeDefinition CoolantRegulatorRecipe =>
        _coolantRegulatorRecipe ??
        throw new InvalidOperationException(
            "Coolant regulator recipe is unavailable.");

    private CraftingRecipeDefinition PowerCouplerRecipe =>
        _powerCouplerRecipe ??
        throw new InvalidOperationException(
            "Power coupler recipe is unavailable.");

    private IReadOnlyList<CraftingRecipeDefinition> StationRecipes =>
        _stationRecipes.Values
            .OrderBy(recipe => recipe.RecipeId, StringComparer.Ordinal)
            .ToArray();

    public override void _Ready()
    {
        _hudMargin = GetNodeOrNull<MarginContainer>(
            "Hud/MarginContainer");
        _hudLabel = GetNodeOrNull<Label>(
            "Hud/MarginContainer/PanelContainer/Label");
        _hudHiddenHint = GetNodeOrNull<PanelContainer>(
            "Hud/HiddenHint");
        _recipeSelectorPanel = GetNodeOrNull<PanelContainer>(
            "Hud/RecipeSelector");
        _recipeSelectorLabel = GetNodeOrNull<Label>(
            "Hud/RecipeSelector/Label");
        _shipTerminal = GetNodeOrNull<StarterShipRepairTerminal>(
            "Gameplay/DamagedShip");
        _player = GetNodeOrNull<PlayerController>("Player");
        if (_hudMargin is null || _hudLabel is null ||
            _hudHiddenHint is null || _recipeSelectorPanel is null ||
            _recipeSelectorLabel is null || _shipTerminal is null ||
            _player is null)
        {
            throw new InvalidOperationException(
                "Vertical slice scene is missing HUD, player or ship.");
        }

        GameContentCatalog catalog = LoadContentCatalog();
        SaveDatabase.RegisterKnownInventoryDefinitions(catalog.Items.Keys);
        CraftingRecipeDefinition repairRecipe = catalog.GetRecipe(
            StarterRepairContentIds.RecipeId);
        CraftingRecipeDefinition[] stationRecipes = catalog.Recipes.Values
            .Where(recipe =>
                recipe.RuntimeEnabled &&
                string.Equals(
                    recipe.Application.Type,
                    "StoreOutputs",
                    StringComparison.Ordinal))
            .OrderBy(recipe => recipe.RecipeId, StringComparer.Ordinal)
            .ToArray();
        if (stationRecipes.Length == 0)
        {
            throw new InvalidOperationException(
                "Content catalog has no station recipes.");
        }

        foreach (CraftingRecipeDefinition recipe in stationRecipes)
        {
            if (!_stationRecipes.TryAdd(recipe.RecipeId, recipe))
            {
                throw new InvalidOperationException(
                    $"Duplicate station recipe {recipe.RecipeId}.");
            }
        }

        CraftingRecipeDefinition launchCapacitorRecipe = catalog.GetRecipe(
            VerticalSliceContentIds.LaunchCapacitorRecipeId);
        CraftingRecipeDefinition navigationArrayRecipe = catalog.GetRecipe(
            VerticalSliceContentIds.NavigationArrayRecipeId);
        CraftingRecipeDefinition coolantRegulatorRecipe = catalog.GetRecipe(
            VerticalSliceContentIds.CoolantRegulatorRecipeId);
        CraftingRecipeDefinition powerCouplerRecipe = catalog.GetRecipe(
            VerticalSliceContentIds.PowerCouplerRecipeId);
        TechnologyProgression technologyProgression = new(
            catalog.Technologies,
            DefaultResearchPoints);
        _contentCatalog = catalog;
        _technologyProgression = technologyProgression;
        _repairRecipe = repairRecipe;
        _launchCapacitorRecipe = launchCapacitorRecipe;
        _navigationArrayRecipe = navigationArrayRecipe;
        _coolantRegulatorRecipe = coolantRegulatorRecipe;
        _powerCouplerRecipe = powerCouplerRecipe;
        _session = new StarterRepairSession(
            repairRecipe,
            technologyProgression.IsUnlocked,
            stationRecipes);

        foreach (Node node in GetTree().GetNodesInGroup(
            "vertical_slice_resource"))
        {
            if (node is SalvageResourceNode resourceNode)
            {
                _resourceNodes.Add(resourceNode);
            }
        }

        foreach (Node node in GetTree().GetNodesInGroup(
            "vertical_slice_crafting_station"))
        {
            if (node is PortableCraftingStation station)
            {
                _craftingStations.Add(station);
            }
        }

        _resourceNodes.Sort(
            (left, right) => string.Compare(
                left.ResourceNodeId,
                right.ResourceNodeId,
                StringComparison.Ordinal));
        _craftingStations.Sort(
            (left, right) =>
            {
                int stationComparison = string.Compare(
                    left.StationId,
                    right.StationId,
                    StringComparison.Ordinal);
                return stationComparison != 0
                    ? stationComparison
                    : string.Compare(
                        left.Name.ToString(),
                        right.Name.ToString(),
                        StringComparison.Ordinal);
            });
        ValidateResourceNodeBindings();

        string userDirectory = ProjectSettings.GlobalizePath("user://");
        string databasePath = Path.Combine(
            userDirectory,
            "profiles",
            "profile_vertical_slice",
            "save_1.db");
        SaveDatabase database = new(databasePath);
        _database = database;
        _autosave = new SaveAutosaveCoordinator(database);
        _initializeTask = database.InitializeAsync(
            _lifetimeCancellation.Token);

        SceneTree tree = GetTree();
        _previousAutoAcceptQuit = tree.AutoAcceptQuit;
        tree.AutoAcceptQuit = false;
        ApplyHudMode();
        UpdateHud();
        string craftTimes = string.Join(
            "/",
            StationRecipes.Select(recipe =>
                recipe.CraftTimeSeconds.ToString(
                    "0.##",
                    CultureInfo.InvariantCulture)));
        GD.Print(
            "TASK-076 data-driven crafting catalog initializing. " +
            $"repairRecipe={RepairRecipe.RecipeId}; " +
            $"required={Session.RequiredSalvage} x {Session.SalvageDefinitionId}; " +
            $"stationRecipes={StationRecipes.Count}; " +
            $"craftTimes={craftTimes}s. " +
            "Press F1 for production queue acceptance, F2 for chemical " +
            "runtime acceptance, F3 for " +
            "selector/research acceptance, F4 for the complete Industry " +
            "Content v2 structural acceptance, F5 for " +
            "the playable runtime matrix, F6/F7/F9/F10/F11/F12 for " +
            "regressions or F8 to reset.");
        GD.Print(
            "TASK-090 production queue READY: " +
            $"stations={ContentCatalog.Stations.Count}; " +
            $"parallelStations={ContentCatalog.Stations.Values.Count(
                station => station.ParallelSlots > 1)}; " +
            $"maxSlots={ContentCatalog.Stations.Values.Max(
                station => station.ParallelSlots)}; " +
            "reservation=enqueue; cancellationRefund=full; " +
            "gracefulExit=freeze-and-resume; offlineProgress=0.");
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            CancelTimedCraft("graceful exit requested");
            _closeRequested = true;
            TryBeginGracefulExit();
        }
    }

    public override void _ExitTree()
    {
        GetTree().AutoAcceptQuit = _previousAutoAcceptQuit;
        _lifetimeCancellation.Cancel();
        _autosave?.Dispose();
        _database?.Dispose();
        _lifetimeCancellation.Dispose();
    }

    public override void _Process(double delta)
    {
        PollInitializeTask();
        PollLoadTask();
        PollResetTask();
        PollAcceptanceTask();
        PollContentAcceptanceTask();
        PollCraftingAcceptanceTask();
        PollCraftTimeAcceptanceTask();
        PollThirdCraftingAcceptanceTask();
        PollFourthCraftingAcceptanceTask();
        PollCatalogMatrixAcceptanceTask();
        PollTechnologySelectorAcceptanceTask();
        PollChemicalProcessAcceptanceTask();
        PollProductionQueueAcceptanceTask();
        UpdateTimedCraft(delta);
        PollAutosave();
        PollGracefulExitTask();
        UpdatePeriodicAutosave(delta);
        TryBeginGracefulExit();
        UpdateHud();
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey keyEvent ||
            !keyEvent.Pressed ||
            keyEvent.Echo)
        {
            return;
        }

        Key physical = keyEvent.PhysicalKeycode;
        Key logical = keyEvent.Keycode;
        if (_selectorStation is not null)
        {
            if (Matches(physical, logical, Key.Escape))
            {
                CloseRecipeSelector("recipe selector closed");
            }
            else if (Matches(physical, logical, Key.Up))
            {
                MoveSelector(-1);
            }
            else if (Matches(physical, logical, Key.Down))
            {
                MoveSelector(1);
            }
            else if (Matches(physical, logical, Key.Tab) ||
                     Matches(physical, logical, Key.R))
            {
                ToggleSelectorMode();
            }
            else if (Matches(physical, logical, Key.Enter) ||
                     (Matches(physical, logical, Key.E) &&
                      Time.GetTicksMsec() > _selectorOpenedTicks + 120))
            {
                ConfirmSelectorSelection();
            }

            GetViewport().SetInputAsHandled();
            return;
        }

        if (Matches(physical, logical, Key.H))
        {
            _hudMode = (SalvageRepairHudMode)(((int)_hudMode + 1) % 3);
            ApplyHudMode();
            GD.Print($"Vertical slice HUD mode: {_hudMode}.");
            GetViewport().SetInputAsHandled();
            return;
        }

        if (Matches(physical, logical, Key.F1) && CanStartCommand())
        {
            BeginProductionQueueAcceptance();
            GetViewport().SetInputAsHandled();
        }
        else if (Matches(physical, logical, Key.F2) && CanStartCommand())
        {
            BeginChemicalProcessAcceptance();
            GetViewport().SetInputAsHandled();
        }
        else if (Matches(physical, logical, Key.F3) && CanStartCommand())
        {
            BeginTechnologySelectorAcceptance();
            GetViewport().SetInputAsHandled();
        }
        else if (Matches(physical, logical, Key.F4) && CanStartCommand())
        {
            RunIndustryCatalogAcceptance();
            GetViewport().SetInputAsHandled();
        }
        else if (Matches(physical, logical, Key.F5) && CanStartCommand())
        {
            BeginCatalogMatrixAcceptance();
            GetViewport().SetInputAsHandled();
        }
        else if (Matches(physical, logical, Key.F6) && CanStartCommand())
        {
            BeginFourthCraftingAcceptance();
            GetViewport().SetInputAsHandled();
        }
        else if (Matches(physical, logical, Key.F7) && CanStartCommand())
        {
            BeginAcceptance();
            GetViewport().SetInputAsHandled();
        }
        else if (Matches(physical, logical, Key.F8) && CanStartCommand())
        {
            BeginReset();
            GetViewport().SetInputAsHandled();
        }
        else if (Matches(physical, logical, Key.F9) && CanStartCommand())
        {
            BeginContentAcceptance();
            GetViewport().SetInputAsHandled();
        }
        else if (Matches(physical, logical, Key.F10) && CanStartCommand())
        {
            BeginCraftingAcceptance();
            GetViewport().SetInputAsHandled();
        }
        else if (Matches(physical, logical, Key.F11) && CanStartCommand())
        {
            BeginCraftTimeAcceptance();
            GetViewport().SetInputAsHandled();
        }
        else if (Matches(physical, logical, Key.F12) && CanStartCommand())
        {
            BeginThirdCraftingAcceptance();
            GetViewport().SetInputAsHandled();
        }
    }

    public void OpenRecipeSelector(
        PortableCraftingStation station,
        Node3D interactor)
    {
        ArgumentNullException.ThrowIfNull(station);
        ArgumentNullException.ThrowIfNull(interactor);
        if (_state != SalvageRepairSliceState.Ready &&
            _state != SalvageRepairSliceState.Passed)
        {
            _status = "wait until the current persistence operation completes";
            return;
        }

        if (_craftTimer.IsRunning)
        {
            _status = $"recipe {_craftTimer.RecipeId} is already processing";
            return;
        }

        IReadOnlyList<CraftingRecipeDefinition> recipes =
            GetSelectorRecipes(station.StationId);
        if (recipes.Count == 0)
        {
            _status = $"station {station.StationId} has no runtime recipes";
            return;
        }

        bool sameOpenStation = ReferenceEquals(_selectorStation, station);
        _selectorStation = station;
        _selectorInteractor = interactor;
        if (!sameOpenStation)
        {
            _selectorMode = StationSelectorMode.Recipes;
            _selectorIndex = 0;
            _selectorFeedback = "";
        }

        if (!sameOpenStation)
        {
            _selectorOpenedTicks = Time.GetTicksMsec();
        }

        if (_recipeSelectorPanel is not null)
        {
            _recipeSelectorPanel.Visible = true;
        }

        UpdateRecipeSelector();
        _status = $"recipe selector opened: {recipes.Count} recipes at " +
            station.Name;
    }

    private void CloseRecipeSelector(string status = "")
    {
        _selectorStation = null;
        _selectorInteractor = null;
        _selectorIndex = 0;
        _selectorFeedback = "";
        if (_recipeSelectorPanel is not null)
        {
            _recipeSelectorPanel.Visible = false;
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            _status = status;
        }
    }

    private IReadOnlyList<CraftingRecipeDefinition> GetSelectorRecipes(
        string stationId)
    {
        StationRecipeSelectorModel selector = new(
            ContentCatalog,
            Session,
            TechnologyProgress);
        return selector.GetRecipes(stationId);
    }

    private IReadOnlyList<StationRecipeSelectorEntry>
        GetSelectorRecipeEntries(string stationId)
    {
        StationRecipeSelectorModel selector = new(
            ContentCatalog,
            Session,
            TechnologyProgress);
        return selector.GetRecipeEntries(stationId);
    }

    private IReadOnlyList<TechnologyDefinition> GetSelectorTechnologies(
        string stationId)
    {
        StationRecipeSelectorModel selector = new(
            ContentCatalog,
            Session,
            TechnologyProgress);
        return selector.GetResearchEntries(stationId);
    }

    private void MoveSelector(int delta)
    {
        PortableCraftingStation? station = _selectorStation;
        if (station is null)
        {
            return;
        }

        int count = _selectorMode == StationSelectorMode.Recipes
            ? GetSelectorRecipes(station.StationId).Count
            : GetSelectorTechnologies(station.StationId).Count;
        if (count == 0)
        {
            _selectorIndex = 0;
            return;
        }

        _selectorIndex = (_selectorIndex + delta) % count;
        if (_selectorIndex < 0)
        {
            _selectorIndex += count;
        }

        _selectorFeedback = "";
        UpdateRecipeSelector();
    }

    private void ToggleSelectorMode()
    {
        if (_selectorStation is null)
        {
            return;
        }

        _selectorMode = _selectorMode == StationSelectorMode.Recipes
            ? StationSelectorMode.Research
            : StationSelectorMode.Recipes;
        _selectorIndex = 0;
        _selectorFeedback = "";
        UpdateRecipeSelector();
    }

    private void ConfirmSelectorSelection()
    {
        PortableCraftingStation? station = _selectorStation;
        Node3D? interactor = _selectorInteractor;
        if (station is null || interactor is null)
        {
            return;
        }

        if (_selectorMode == StationSelectorMode.Research)
        {
            IReadOnlyList<TechnologyDefinition> technologies =
                GetSelectorTechnologies(station.StationId);
            if (technologies.Count == 0)
            {
                _selectorFeedback = "No relevant technologies.";
                UpdateRecipeSelector();
                return;
            }

            TechnologyDefinition technology =
                technologies[Math.Clamp(_selectorIndex, 0, technologies.Count - 1)];
            TechnologyUnlockResult unlockResult = TechnologyProgress.TryUnlock(
                technology.TechnologyId,
                out string result);
            _selectorFeedback = result;
            _status = result;
            if (unlockResult == TechnologyUnlockResult.Unlocked)
            {
                _lastDomainEvent =
                    $"TechnologyUnlocked({technology.TechnologyId})";
                GD.Print(
                    "TASK-082 technology unlocked: " +
                    $"technology={technology.TechnologyId}; " +
                    $"tier={technology.Tier}; cost={technology.ResearchCost}; " +
                    $"remaining={TechnologyProgress.ResearchPoints}.");
                QueueCurrentSnapshot(AutosaveTrigger.QuestCompleted);
                CloseRecipeSelector(result);
                return;
            }

            UpdateRecipeSelector();
            return;
        }

        IReadOnlyList<StationRecipeSelectorEntry> entries =
            GetSelectorRecipeEntries(station.StationId);
        if (entries.Count == 0)
        {
            _selectorFeedback = "No runtime recipes.";
            UpdateRecipeSelector();
            return;
        }

        StationRecipeSelectorEntry entry =
            entries[Math.Clamp(_selectorIndex, 0, entries.Count - 1)];
        if (entry.Crafted)
        {
            _selectorFeedback =
                $"Recipe {entry.Recipe.RecipeId} is already completed.";
            UpdateRecipeSelector();
            return;
        }

        if (!entry.TechnologyUnlocked)
        {
            _selectorFeedback =
                $"LOCKED: research {entry.Recipe.RequiredTechnology}.";
            UpdateRecipeSelector();
            return;
        }

        if (!entry.InputsAvailable)
        {
            _selectorFeedback =
                $"Missing {entry.MissingInputQuantity} input unit(s).";
            UpdateRecipeSelector();
            return;
        }

        string recipeId = entry.Recipe.RecipeId;
        string stationId = station.StationId;
        CloseRecipeSelector();
        TryCraftAtStation(
            station,
            recipeId,
            stationId,
            interactor);
    }

    private void UpdateRecipeSelector()
    {
        PortableCraftingStation? station = _selectorStation;
        if (_recipeSelectorLabel is null || station is null)
        {
            return;
        }

        string stationId = station.StationId;
        List<string> lines = new()
        {
            $"INDUSTRY TERMINAL - {station.Name}",
            $"Station: {stationId}",
            $"Mode: {_selectorMode} | RP: {TechnologyProgress.ResearchPoints} | " +
            $"Unlocked: {TechnologyProgress.UnlockedCount}/{ContentCatalog.Technologies.Count}",
            ""
        };

        if (_selectorMode == StationSelectorMode.Recipes)
        {
            IReadOnlyList<StationRecipeSelectorEntry> entries =
                GetSelectorRecipeEntries(stationId);
            for (int index = 0; index < entries.Count; index++)
            {
                StationRecipeSelectorEntry entry = entries[index];
                CraftingRecipeDefinition recipe = entry.Recipe;
                string status = entry.Crafted
                    ? "DONE"
                    : !entry.TechnologyUnlocked
                        ? $"LOCKED {recipe.RequiredTechnology}"
                        : !entry.InputsAvailable
                            ? $"MISSING {entry.MissingInputQuantity}"
                            : "READY";
                string inputs = string.Join(
                    " + ",
                    recipe.Inputs.Select(input =>
                        $"{Session.GetAvailableQuantity(input.DefinitionId)}/" +
                        $"{input.Quantity} {GetShortContentId(input.DefinitionId)}"));
                string outputs = string.Join(
                    " + ",
                    recipe.Outputs.Select(output =>
                        $"{output.Quantity} {GetShortContentId(output.DefinitionId)}"));
                string cursor = index == _selectorIndex ? ">" : " ";
                lines.Add(
                    $"{cursor} [{status}] {GetShortContentId(recipe.RecipeId)} " +
                    $"T{recipe.TechnologyTier} {recipe.CraftTimeSeconds:0.##}s");
                lines.Add($"    {inputs} -> {outputs}");
            }
        }
        else
        {
            IReadOnlyList<TechnologyDefinition> technologies =
                GetSelectorTechnologies(stationId);
            for (int index = 0; index < technologies.Count; index++)
            {
                TechnologyDefinition technology = technologies[index];
                IReadOnlyList<string> missing =
                    TechnologyProgress.GetMissingPrerequisites(
                        technology.TechnologyId);
                string status = TechnologyProgress.IsUnlocked(
                    technology.TechnologyId)
                    ? "UNLOCKED"
                    : missing.Count > 0
                        ? $"LOCKED requires {string.Join(",", missing)}"
                        : TechnologyProgress.ResearchPoints >=
                            technology.ResearchCost
                            ? $"AVAILABLE {technology.ResearchCost} RP"
                            : $"NEED {technology.ResearchCost} RP";
                string cursor = index == _selectorIndex ? ">" : " ";
                lines.Add(
                    $"{cursor} [{status}] {technology.TechnologyId} " +
                    $"(tier {technology.Tier})");
            }
        }

        if (!string.IsNullOrWhiteSpace(_selectorFeedback))
        {
            lines.Add("");
            lines.Add($"Result: {_selectorFeedback}");
        }

        lines.Add("");
        lines.Add(
            "Up/Down - select | Tab/R - Recipes/Research | " +
            "Enter/E - confirm | Esc - close");
        _recipeSelectorLabel.Text = string.Join("\n", lines);
    }

    public bool TryCollectResource(
        SalvageResourceNode source,
        string resourceNodeId,
        string definitionId,
        int quantity,
        Node3D interactor)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(interactor);
        if (_state != SalvageRepairSliceState.Ready &&
            _state != SalvageRepairSliceState.Passed)
        {
            _status = "wait until the current persistence operation completes";
            return false;
        }

        if (!Session.TryCollect(
            resourceNodeId,
            definitionId,
            quantity,
            out string result))
        {
            _status = result;
            return false;
        }

        _lastDomainEvent =
            $"ResourceCollected({resourceNodeId}, definition={definitionId}, " +
            $"quantity={quantity})";
        _status = result;
        GD.Print(
            $"Vertical slice domain event: {_lastDomainEvent}; " +
            $"available={Session.GetAvailableQuantity(definitionId)}; " +
            $"interactor={interactor.Name}");
        return true;
    }

    public void TryRepairShip(Node3D interactor)
    {
        ArgumentNullException.ThrowIfNull(interactor);
        if (_state != SalvageRepairSliceState.Ready &&
            _state != SalvageRepairSliceState.Passed)
        {
            _status = "wait until the current persistence operation completes";
            return;
        }

        StarterRepairResult repairResult = Session.TryRepair(out string result);
        _status = result;
        if (repairResult == StarterRepairResult.InsufficientSalvage)
        {
            _lastDomainEvent = "ShipRepairBlocked";
            GD.Print(
                $"Vertical slice domain event: ShipRepairBlocked; " +
                $"salvage={Session.SalvageQuantity}/" +
                $"{Session.RequiredSalvage}");
            return;
        }

        if (repairResult == StarterRepairResult.AlreadyRepaired)
        {
            return;
        }

        _shipTerminal?.SetRepaired(true);
        _lastDomainEvent = "StarterRepairQuestCompleted";
        QueueCurrentSnapshot(AutosaveTrigger.QuestCompleted);
        GD.Print(
            "Vertical slice domain event: StarterRepairQuestCompleted; " +
            $"autosaveTrigger={AutosaveTrigger.QuestCompleted}; " +
            $"revision={_revision}; interactor={interactor.Name}");
    }

    public void TryCraftAtStation(
        PortableCraftingStation source,
        string recipeId,
        string stationId,
        Node3D interactor)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(interactor);
        if (_state != SalvageRepairSliceState.Ready &&
            _state != SalvageRepairSliceState.Passed)
        {
            _status = "wait until the current persistence operation completes";
            return;
        }

        if (!TryResolveStationRecipe(recipeId, out CraftingRecipeDefinition recipe))
        {
            _status = $"unknown station recipe {recipeId}";
            return;
        }

        if (_craftTimer.IsRunning)
        {
            _lastDomainEvent = "StationCraftRunning";
            _status = $"recipe {_craftTimer.RecipeId} is already processing; " +
                $"remaining={_craftTimer.RemainingSeconds:0.0}s";
            return;
        }

        StationCraftResult validation = Session.ValidateCraft(
            recipeId,
            stationId,
            out string validationResult);
        _status = validationResult;
        if (validation != StationCraftResult.Ready)
        {
            if (validation != StationCraftResult.AlreadyCrafted)
            {
                _lastDomainEvent = "StationCraftBlocked";
                CraftingStackDefinition input = recipe.Inputs[0];
                GD.Print(
                    $"{GetCraftTaskId(recipeId)} station craft blocked: " +
                    $"result={validation}; recipe={recipeId}; station={stationId}; " +
                    $"available={Session.GetAvailableQuantity(input.DefinitionId)}.");
            }

            return;
        }

        if (recipe.CraftTimeSeconds <= 0.0)
        {
            CompleteStationCraft(
                source,
                recipeId,
                stationId,
                interactor.Name.ToString(),
                timed: false);
            return;
        }

        if (!_craftTimer.TryStart(
            recipe,
            stationId,
            out string startResult))
        {
            _status = startResult;
            _lastDomainEvent = "StationCraftBlocked";
            return;
        }

        _activeCraftingStation = source;
        _craftingInteractorName = interactor.Name.ToString();
        source.SetCrafting(true);
        _lastDomainEvent = BuildCraftEventName(
            recipeId,
            "CraftStarted");
        _status = $"crafting {recipeId}: 0.0/" +
            $"{_craftTimer.DurationSeconds:0.0}s";
        GD.Print(
            $"{GetCraftTaskId(recipeId)} timed craft started: " +
            $"recipe={recipeId}; station={stationId}; " +
            $"duration={_craftTimer.DurationSeconds.ToString("0.0", CultureInfo.InvariantCulture)}; " +
            "inputsHeld=1; output=0.");
    }

    private void UpdateTimedCraft(double delta)
    {
        if (!_craftTimer.IsRunning)
        {
            return;
        }

        CraftTimerAdvanceResult advanceResult = _craftTimer.Advance(
            delta,
            out _);
        _status = $"crafting {_craftTimer.RecipeId}: " +
            $"{_craftTimer.ElapsedSeconds:0.0}/" +
            $"{_craftTimer.DurationSeconds:0.0}s";
        if (advanceResult != CraftTimerAdvanceResult.Completed)
        {
            return;
        }

        if (_activeCraftingStation is null)
        {
            string cancelledRecipeId = _craftTimer.RecipeId;
            double cancelledElapsed = _craftTimer.ElapsedSeconds;
            double duration = _craftTimer.DurationSeconds;
            _craftTimer.Reset();
            _craftingInteractorName = "unknown";
            _lastDomainEvent = "StationCraftCancelled";
            _state = SalvageRepairSliceState.Failed;
            _status = "timed craft failed: crafting station became unavailable";
            GD.PushError(
                $"{GetCraftTaskId(cancelledRecipeId)} timed craft cancelled safely: " +
                $"recipe={cancelledRecipeId}; " +
                $"elapsed={cancelledElapsed.ToString("0.0", CultureInfo.InvariantCulture)}; " +
                $"duration={duration.ToString("0.0", CultureInfo.InvariantCulture)}; " +
                "inputsConsumed=0; reason=crafting station became unavailable.");
            return;
        }

        PortableCraftingStation station = _activeCraftingStation;
        string recipeId = _craftTimer.RecipeId;
        string stationId = _craftTimer.StationId;
        double configuredDuration = _craftTimer.DurationSeconds;
        double elapsed = _craftTimer.ElapsedSeconds;
        _craftTimer.Reset();
        _activeCraftingStation = null;
        CompleteStationCraft(
            station,
            recipeId,
            stationId,
            _craftingInteractorName,
            timed: true,
            configuredDuration: configuredDuration,
            elapsed: elapsed);
    }

    private void CompleteStationCraft(
        PortableCraftingStation source,
        string recipeId,
        string stationId,
        string interactorName,
        bool timed,
        double configuredDuration = 0.0,
        double elapsed = 0.0)
    {
        CraftingRecipeDefinition recipe = ResolveStationRecipe(recipeId);
        StationCraftResult craftResult = Session.TryCraft(
            recipeId,
            stationId,
            out string result);
        _status = result;
        source.SetCrafting(false);
        if (craftResult != StationCraftResult.Crafted)
        {
            _lastDomainEvent = "StationCraftBlocked";
            GD.PushWarning(
                $"{GetCraftTaskId(recipeId)} timed craft completion blocked: " +
                $"result={craftResult}; recipe={recipeId}; station={stationId}.");
            return;
        }

        CraftingRecipeDefinition[] sourceRecipes = StationRecipes
            .Where(candidate => string.Equals(
                candidate.RequiredStation,
                source.StationId,
                StringComparison.Ordinal))
            .ToArray();
        source.SetCrafted(
            sourceRecipes.Length > 0 &&
            sourceRecipes.All(candidate =>
                Session.IsRecipeCrafted(candidate.RecipeId)));
        _lastDomainEvent = BuildCraftEventName(
            recipeId,
            "Crafted");
        QueueCurrentSnapshot(AutosaveTrigger.QuestCompleted);
        int outputQuantity = recipe.Outputs.Sum(output => output.Quantity);
        if (timed)
        {
            string prefix =
                $"{GetCraftTaskId(recipeId)} timed craft completion PASS: ";
            GD.Print(
                prefix +
                $"recipe={recipeId}; station={stationId}; " +
                $"configured={configuredDuration.ToString("0.0", CultureInfo.InvariantCulture)}; " +
                $"elapsed={elapsed.ToString("0.0", CultureInfo.InvariantCulture)}; " +
                $"inputsHeldUntilCompletion=1; completedOnce=1; output={outputQuantity}; " +
                $"autosaveTrigger={AutosaveTrigger.QuestCompleted}; " +
                $"revision={_revision}; interactor={interactorName}");
            return;
        }

        GD.Print(
            $"Vertical slice domain event: {_lastDomainEvent}; " +
            $"recipe={recipeId}; output={recipe.Outputs[0].DefinitionId}; " +
            $"autosaveTrigger={AutosaveTrigger.QuestCompleted}; " +
            $"revision={_revision}; interactor={interactorName}");
    }

    private void CancelTimedCraft(string reason)
    {
        if (!_craftTimer.IsRunning)
        {
            return;
        }

        string recipeId = _craftTimer.RecipeId;
        double elapsed = _craftTimer.ElapsedSeconds;
        double duration = _craftTimer.DurationSeconds;
        _craftTimer.Reset();
        _activeCraftingStation?.SetCrafting(false);
        _activeCraftingStation = null;
        _lastDomainEvent = "StationCraftCancelled";
        _status = $"timed craft cancelled: {reason}";
        GD.Print(
            $"{GetCraftTaskId(recipeId)} timed craft cancelled safely: " +
            $"recipe={recipeId}; elapsed={elapsed.ToString("0.0", CultureInfo.InvariantCulture)}; " +
            $"duration={duration.ToString("0.0", CultureInfo.InvariantCulture)}; " +
            $"inputsConsumed=0; reason={reason}.");
    }

    private bool TryResolveStationRecipe(
        string recipeId,
        out CraftingRecipeDefinition recipe)
    {
        return _stationRecipes.TryGetValue(recipeId, out recipe!);
    }

    private CraftingRecipeDefinition ResolveStationRecipe(string recipeId)
    {
        if (TryResolveStationRecipe(recipeId, out CraftingRecipeDefinition recipe))
        {
            return recipe;
        }

        throw new InvalidOperationException(
            $"Unknown station recipe {recipeId}.");
    }

    private static string GetCraftTaskId(string recipeId)
    {
        return recipeId switch
        {
            VerticalSliceContentIds.LaunchCapacitorRecipeId => "TASK-068",
            VerticalSliceContentIds.NavigationArrayRecipeId => "TASK-070",
            VerticalSliceContentIds.CoolantRegulatorRecipeId => "TASK-072",
            VerticalSliceContentIds.PowerCouplerRecipeId => "TASK-074",
            _ => "TASK-076"
        };
    }

    private void ValidateResourceNodeBindings()
    {
        if (_shipTerminal is null || !string.Equals(
            _shipTerminal.StationId,
            RepairRecipe.RequiredStation,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Recipe {RepairRecipe.RecipeId} requires station " +
                $"{RepairRecipe.RequiredStation}, but scene terminal is " +
                $"{_shipTerminal?.StationId ?? "missing"}.");
        }

        if (_craftingStations.Count == 0)
        {
            throw new InvalidOperationException(
                "Vertical slice has no crafting stations.");
        }

        CraftingRecipeDefinition[] stationRecipes = StationRecipes.ToArray();
        string[] requiredStationIds = stationRecipes
            .Select(recipe => recipe.RequiredStation)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        foreach (string stationId in requiredStationIds)
        {
            int physicalCount = _craftingStations.Count(station =>
                string.Equals(
                    station.StationId,
                    stationId,
                    StringComparison.Ordinal));
            if (physicalCount != 1)
            {
                throw new InvalidOperationException(
                    $"Station {stationId} requires exactly one physical " +
                    $"selector terminal, but scene provides {physicalCount}.");
            }
        }

        foreach (PortableCraftingStation station in _craftingStations)
        {
            if (!ContentCatalog.Stations.ContainsKey(station.StationId))
            {
                throw new InvalidOperationException(
                    $"Scene station {station.Name} references unknown " +
                    $"{station.StationId}.");
            }
        }

        foreach (CraftingRecipeDefinition recipe in stationRecipes)
        {
            if (!double.IsFinite(recipe.CraftTimeSeconds) ||
                recipe.CraftTimeSeconds <= 0.0)
            {
                throw new InvalidOperationException(
                    $"Recipe {recipe.RecipeId} must define a positive " +
                    "CraftTimeSeconds value.");
            }

            if (!ContentCatalog.Technologies.ContainsKey(
                    recipe.RequiredTechnology))
            {
                throw new InvalidOperationException(
                    $"Recipe {recipe.RecipeId} references unknown technology " +
                    $"{recipe.RequiredTechnology}.");
            }
        }

        if (_resourceNodes.Count == 0)
        {
            throw new InvalidOperationException(
                "Vertical slice has no resource nodes.");
        }

        string[] actualIds = _resourceNodes
            .Select(node => node.ResourceNodeId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        if (actualIds.Distinct(StringComparer.Ordinal).Count() !=
            actualIds.Length)
        {
            throw new InvalidOperationException(
                "Vertical-slice ResourceNodeId bindings contain duplicates: " +
                $"[{string.Join(", ", actualIds)}].");
        }

        Dictionary<string, int> availableByDefinition =
            new(StringComparer.Ordinal);
        foreach (SalvageResourceNode node in _resourceNodes)
        {
            GameResourceDefinition definition =
                ContentCatalog.GetResource(node.ResourceDefinitionId);
            node.ConfigureDefinition(definition);
            availableByDefinition.TryGetValue(
                definition.ItemDefinitionId,
                out int current);
            availableByDefinition[definition.ItemDefinitionId] =
                current + node.Quantity;
        }

        foreach (CraftingRecipeDefinition recipe in
            new[] { RepairRecipe }.Concat(stationRecipes))
        {
            foreach (CraftingStackDefinition input in recipe.Inputs)
            {
                availableByDefinition.TryGetValue(
                    input.DefinitionId,
                    out int available);
                if (available < input.Quantity)
                {
                    throw new InvalidOperationException(
                        $"Recipe {recipe.RecipeId} requires " +
                        $"{input.Quantity} x {input.DefinitionId}, but scene " +
                        $"provides only {available}.");
                }
            }
        }

        int primaryAvailable = availableByDefinition.TryGetValue(
            Session.SalvageDefinitionId,
            out int quantityAvailable)
            ? quantityAvailable
            : 0;
        string[] repairResourceIds = _resourceNodes
            .Where(node => string.Equals(
                ContentCatalog.GetResource(node.ResourceDefinitionId)
                    .ItemDefinitionId,
                Session.SalvageDefinitionId,
                StringComparison.Ordinal))
            .Select(node => node.ResourceNodeId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        GD.Print(
            "TASK-062 scene binding PASS: " +
            $"resourceIds={string.Join(",", repairResourceIds)}; unique=1.");
        GD.Print(
            "TASK-064 content binding PASS: " +
            $"schema={ContentCatalog.SchemaVersion}; " +
            $"recipe={RepairRecipe.RecipeId}; " +
            $"resource={Session.SalvageDefinitionId}; " +
            $"required={Session.RequiredSalvage}; " +
            $"available={primaryAvailable}; " +
            $"items={ContentCatalog.Items.Count}; " +
            $"resources={ContentCatalog.Resources.Count}; " +
            $"recipes={ContentCatalog.Recipes.Count}; " +
            $"station={RepairRecipe.RequiredStation}.");

        foreach (CraftingRecipeDefinition recipe in stationRecipes)
        {
            CraftingStackDefinition input = recipe.Inputs[0];
            availableByDefinition.TryGetValue(
                input.DefinitionId,
                out int available);
            GD.Print(
                $"{GetCraftTaskId(recipe.RecipeId)} crafting binding PASS: " +
                $"recipe={recipe.RecipeId}; resource={input.DefinitionId}; " +
                $"required={input.Quantity}; available={available}; " +
                $"station={recipe.RequiredStation}; " +
                $"technology={recipe.RequiredTechnology}; " +
                $"craftTime={recipe.CraftTimeSeconds.ToString("0.0", CultureInfo.InvariantCulture)}.");
        }

        GD.Print(
            "TASK-076 crafting catalog binding PASS: " +
            $"items={ContentCatalog.Items.Count}; " +
            $"resources={ContentCatalog.Resources.Count}; " +
            $"recipes={ContentCatalog.Recipes.Count}; " +
            $"stations={ContentCatalog.Stations.Count}; " +
            $"technologies={ContentCatalog.Technologies.Count}; " +
            $"runtimeRecipes={stationRecipes.Length}; " +
            $"sceneStations={_craftingStations.Count}; " +
            $"resourceNodes={_resourceNodes.Count}; " +
            "allInputsCovered=1; allCraftTimesPositive=1.");
        GD.Print(
            "TASK-082 station selector binding PASS: " +
            $"physicalStations={_craftingStations.Count}; " +
            $"selectorRecipes={stationRecipes.Length}; " +
            $"researchPoints={TechnologyProgress.ResearchPoints}; " +
            $"initiallyUnlocked={stationRecipes.Count(recipe => TechnologyProgress.IsUnlocked(recipe.RequiredTechnology))}; " +
            $"initiallyLocked={stationRecipes.Count(recipe => !TechnologyProgress.IsUnlocked(recipe.RequiredTechnology))}.");
    }

    private static GameContentCatalog LoadContentCatalog()
    {
        const string itemsPath = "res://Content/items.json";
        const string resourcesPath = "res://Content/resources.json";
        const string recipesPath = "res://Content/recipes.json";
        const string stationsPath = "res://Content/stations.json";
        const string technologiesPath = "res://Content/technologies.json";
        string itemsJson = Godot.FileAccess.GetFileAsString(itemsPath);
        string resourcesJson = Godot.FileAccess.GetFileAsString(resourcesPath);
        string recipesJson = Godot.FileAccess.GetFileAsString(recipesPath);
        string stationsJson = Godot.FileAccess.GetFileAsString(stationsPath);
        string technologiesJson = Godot.FileAccess.GetFileAsString(
            technologiesPath);
        GameContentCatalog catalog = GameContentCatalog.LoadFromJson(
            itemsJson,
            resourcesJson,
            recipesJson,
            stationsJson,
            technologiesJson);
        IndustryCatalogAnalysis analysis = catalog.AnalyzeIndustry();
        GD.Print(
            "TASK-064 content catalog READY: " +
            $"schema={catalog.SchemaVersion}; " +
            $"items={catalog.Items.Count}; " +
            $"resources={catalog.Resources.Count}; " +
            $"recipes={catalog.Recipes.Count}; " +
            $"stations={catalog.Stations.Count}; " +
            $"technologies={catalog.Technologies.Count}.");
        GD.Print(
            "TASK-080 industry catalog READY: " +
            $"schema={catalog.SchemaVersion}; " +
            $"items={analysis.ItemCount}; " +
            $"resources={analysis.ResourceCount}; " +
            $"recipes={analysis.RecipeCount}; " +
            $"stations={analysis.StationCount}; " +
            $"technologies={analysis.TechnologyCount}; " +
            $"runtimeEnabled={analysis.RuntimeEnabledRecipes}; " +
            $"chemistry={analysis.ChemistryRecipes}; " +
            $"compotium={analysis.CompotiumRecipes}; " +
            $"paraffinium={analysis.ParaffiniumRecipes}; " +
            $"cycles={analysis.DependencyCycles}; " +
            $"unreachable={analysis.UnreachableRecipes}.");
        GD.Print(
            "TASK-083 chemical runtime READY: " +
            $"catalysts={analysis.RecipesWithCatalysts}; " +
            $"byproducts={analysis.RecipesWithByproducts}; " +
            $"environments={analysis.RecipesWithEnvironmentControls}; " +
            "batch=enabled; energy=enabled; hazards=enabled; mode=atomic.");
        return catalog;
    }

    private IReadOnlyDictionary<string, ResourceNodeBinding>
        BuildResourceBindings()
    {
        return _resourceNodes.ToDictionary(
            node => node.ResourceNodeId,
            node =>
            {
                GameResourceDefinition definition =
                    ContentCatalog.GetResource(node.ResourceDefinitionId);
                return new ResourceNodeBinding(
                    node.ResourceNodeId,
                    definition.ItemDefinitionId,
                    node.Quantity);
            },
            StringComparer.Ordinal);
    }

    private bool CanStartCommand()
    {
        return _database is not null &&
            _autosave is not null &&
            _initializeTask is null &&
            _loadTask is null &&
            _resetTask is null &&
            _acceptanceTask is null &&
            _contentAcceptanceTask is null &&
            _craftingAcceptanceTask is null &&
            _craftTimeAcceptanceTask is null &&
            _thirdCraftingAcceptanceTask is null &&
            _fourthCraftingAcceptanceTask is null &&
            _catalogMatrixAcceptanceTask is null &&
            _technologySelectorAcceptanceTask is null &&
            _chemicalProcessAcceptanceTask is null &&
            _productionQueueAcceptanceTask is null &&
            _gracefulExitTask is null &&
            _selectorStation is null &&
            !_craftTimer.IsRunning &&
            !_autosave.IsBusy &&
            !_closeRequested;
    }

    private void RunIndustryCatalogAcceptance()
    {
        _industryCatalogAcceptanceHud = "RUNNING";
        _status = "TASK-080 industry catalog acceptance running";
        IndustryCatalogAcceptanceReport report =
            IndustryCatalogAcceptanceRunner.Run(ContentCatalog);
        IndustryCatalogAnalysis analysis = report.Analysis;
        _industryCatalogAcceptanceHud =
            $"{(report.Passed ? "PASS" : "FAIL")} " +
            $"recipes={analysis.RecipeCount}, " +
            $"chemistry={analysis.ChemistryRecipes}, " +
            $"compotium={analysis.CompotiumRecipes}, " +
            $"stations={analysis.StationCount}, " +
            $"tech={analysis.TechnologyCount}, " +
            $"cycles={analysis.DependencyCycles}, " +
            $"unreachable={analysis.UnreachableRecipes}";
        _status = report.Result;
        string line =
            $"TASK-080 industry catalog acceptance " +
            $"{(report.Passed ? "PASS" : "FAIL")}: " +
            $"schema={ContentCatalog.SchemaVersion}; " +
            $"items={analysis.ItemCount}; " +
            $"resources={analysis.ResourceCount}; " +
            $"recipes={analysis.RecipeCount}; " +
            $"stations={analysis.StationCount}; " +
            $"technologies={analysis.TechnologyCount}; " +
            $"runtimeEnabled={analysis.RuntimeEnabledRecipes}; " +
            $"chemistry={analysis.ChemistryRecipes}; " +
            $"compotium={analysis.CompotiumRecipes}; " +
            $"paraffinium={analysis.ParaffiniumRecipes}; " +
            $"catalysts={analysis.RecipesWithCatalysts}; " +
            $"byproducts={analysis.RecipesWithByproducts}; " +
            $"environments={analysis.RecipesWithEnvironmentControls}; " +
            $"cycles={analysis.DependencyCycles}; " +
            $"unreachable={analysis.UnreachableRecipes}; " +
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
    }

    private void BeginAcceptance()
    {
        if (_database is null)
        {
            return;
        }

        string directory = Path.GetDirectoryName(_database.DatabasePath) ??
            throw new InvalidOperationException(
                "Vertical slice database directory could not be resolved.");
        string testPath = Path.Combine(
            directory,
            "save_1.vertical-slice-test.db");
        _state = SalvageRepairSliceState.Testing;
        _status = "TASK-062 acceptance running";
        _acceptanceHud = "RUNNING";
        _acceptanceReport = null;
        _acceptanceTask = VerticalSliceAcceptanceRunner.RunAsync(
            testPath,
            SlotId,
            RepairRecipe,
            BuildResourceBindings().Values
                .OrderBy(binding => binding.ResourceNodeId, StringComparer.Ordinal)
                .ToArray(),
            _lifetimeCancellation.Token);
    }

    private void BeginContentAcceptance()
    {
        _state = SalvageRepairSliceState.Testing;
        _status = "TASK-064 content acceptance running";
        _contentAcceptanceHud = "RUNNING";
        _contentAcceptanceReport = null;
        _contentAcceptanceTask = Task.Run(
            () => DataDrivenContentAcceptanceRunner.Run(ContentCatalog),
            _lifetimeCancellation.Token);
    }

    private void BeginCraftingAcceptance()
    {
        if (_database is null)
        {
            return;
        }

        string directory = Path.GetDirectoryName(_database.DatabasePath) ??
            throw new InvalidOperationException(
                "Vertical slice database directory could not be resolved.");
        string testPath = Path.Combine(
            directory,
            "save_1.crafting-expansion-test.db");
        _state = SalvageRepairSliceState.Testing;
        _status = "TASK-066 crafting acceptance running";
        _craftingAcceptanceHud = "RUNNING";
        _craftingAcceptanceReport = null;
        _craftingAcceptanceTask = CraftingExpansionAcceptanceRunner.RunAsync(
            testPath,
            SlotId,
            RepairRecipe,
            LaunchCapacitorRecipe,
            BuildResourceBindings().Values
                .OrderBy(binding => binding.ResourceNodeId, StringComparer.Ordinal)
                .ToArray(),
            _lifetimeCancellation.Token);
    }

    private void BeginCraftTimeAcceptance()
    {
        CraftingRecipeDefinition repairRecipe = RepairRecipe;
        CraftingRecipeDefinition craftingRecipe = LaunchCapacitorRecipe;
        ResourceNodeBinding[] bindings = BuildResourceBindings().Values
            .OrderBy(binding => binding.ResourceNodeId, StringComparer.Ordinal)
            .ToArray();
        _state = SalvageRepairSliceState.Testing;
        _status = "TASK-068 craft-time acceptance running";
        _craftTimeAcceptanceHud = "RUNNING";
        _craftTimeAcceptanceReport = null;
        _craftTimeAcceptanceTask = Task.Run(
            () => CraftTimeAcceptanceRunner.Run(
                repairRecipe,
                craftingRecipe,
                bindings),
            _lifetimeCancellation.Token);
    }

    private void BeginThirdCraftingAcceptance()
    {
        if (_database is null)
        {
            return;
        }

        string directory = Path.GetDirectoryName(_database.DatabasePath) ??
            throw new InvalidOperationException(
                "Vertical slice database directory could not be resolved.");
        string testPath = Path.Combine(
            directory,
            "save_1.third-crafting-path-test.db");
        _state = SalvageRepairSliceState.Testing;
        _status = "TASK-070 third crafting path acceptance running";
        _thirdCraftingAcceptanceHud = "RUNNING";
        _thirdCraftingAcceptanceReport = null;
        _thirdCraftingAcceptanceTask =
            ThirdCraftingPathAcceptanceRunner.RunAsync(
                testPath,
                SlotId,
                RepairRecipe,
                LaunchCapacitorRecipe,
                NavigationArrayRecipe,
                BuildResourceBindings().Values
                    .OrderBy(
                        binding => binding.ResourceNodeId,
                        StringComparer.Ordinal)
                    .ToArray(),
                _lifetimeCancellation.Token);
    }

    private void BeginFourthCraftingAcceptance()
    {
        if (_database is null)
        {
            return;
        }

        string directory = Path.GetDirectoryName(_database.DatabasePath) ??
            throw new InvalidOperationException(
                "Vertical slice database directory could not be resolved.");
        string testPath = Path.Combine(
            directory,
            "save_1.fourth-crafting-path-test.db");
        _state = SalvageRepairSliceState.Testing;
        _status = "TASK-072 fourth crafting path acceptance running";
        _fourthCraftingAcceptanceHud = "RUNNING";
        _fourthCraftingAcceptanceReport = null;
        _fourthCraftingAcceptanceTask =
            FourthCraftingPathAcceptanceRunner.RunAsync(
                testPath,
                SlotId,
                RepairRecipe,
                LaunchCapacitorRecipe,
                NavigationArrayRecipe,
                CoolantRegulatorRecipe,
                BuildResourceBindings().Values
                    .OrderBy(
                        binding => binding.ResourceNodeId,
                        StringComparer.Ordinal)
                    .ToArray(),
                _lifetimeCancellation.Token);
    }

    private void BeginProductionQueueAcceptance()
    {
        if (_database is null)
        {
            return;
        }

        CloseRecipeSelector();
        string directory = Path.GetDirectoryName(_database.DatabasePath) ??
            throw new InvalidOperationException(
                "Vertical slice database directory could not be resolved.");
        string testPath = Path.Combine(
            directory,
            "save_1.production-queue-test.db");
        _state = SalvageRepairSliceState.Testing;
        _status = "TASK-090 production queue acceptance running";
        _productionQueueAcceptanceHud = "RUNNING";
        _productionQueueAcceptanceReport = null;
        _productionQueueAcceptanceTask =
            ProductionQueueAcceptanceRunner.RunAsync(
                testPath,
                SlotId,
                ContentCatalog,
                _lifetimeCancellation.Token);
    }

    private void BeginChemicalProcessAcceptance()
    {
        if (_database is null)
        {
            return;
        }

        CloseRecipeSelector();
        string directory = Path.GetDirectoryName(_database.DatabasePath) ??
            throw new InvalidOperationException(
                "Vertical slice database directory could not be resolved.");
        string testPath = Path.Combine(
            directory,
            "save_1.chemical-process-runtime-test.db");
        _state = SalvageRepairSliceState.Testing;
        _status = "TASK-083 chemical process runtime acceptance running";
        _chemicalProcessAcceptanceHud = "RUNNING";
        _chemicalProcessAcceptanceReport = null;
        _chemicalProcessAcceptanceTask =
            ChemicalProcessAcceptanceRunner.RunAsync(
                testPath,
                SlotId,
                ContentCatalog,
                _lifetimeCancellation.Token);
    }

    private void BeginTechnologySelectorAcceptance()
    {
        if (_database is null)
        {
            return;
        }

        CloseRecipeSelector();
        string directory = Path.GetDirectoryName(_database.DatabasePath) ??
            throw new InvalidOperationException(
                "Vertical slice database directory could not be resolved.");
        string testPath = Path.Combine(
            directory,
            "save_1.technology-selector-test.db");
        _state = SalvageRepairSliceState.Testing;
        _status = "TASK-082 station selector and research acceptance running";
        _technologySelectorAcceptanceHud = "RUNNING";
        _technologySelectorAcceptanceReport = null;
        _technologySelectorAcceptanceTask =
            TechnologyRecipeSelectorAcceptanceRunner.RunAsync(
                testPath,
                SlotId,
                ContentCatalog,
                BuildResourceBindings().Values
                    .OrderBy(
                        binding => binding.ResourceNodeId,
                        StringComparer.Ordinal)
                    .ToArray(),
                _lifetimeCancellation.Token);
    }

    private void BeginCatalogMatrixAcceptance()
    {
        if (_database is null)
        {
            return;
        }

        string directory = Path.GetDirectoryName(_database.DatabasePath) ??
            throw new InvalidOperationException(
                "Vertical slice database directory could not be resolved.");
        string testPath = Path.Combine(
            directory,
            "save_1.catalog-crafting-matrix-test.db");
        _state = SalvageRepairSliceState.Testing;
        _status = "TASK-076 catalog crafting matrix running";
        _catalogMatrixAcceptanceHud = "RUNNING";
        _catalogMatrixAcceptanceReport = null;
        _catalogMatrixAcceptanceTask =
            CatalogCraftingMatrixAcceptanceRunner.RunAsync(
                testPath,
                SlotId,
                ContentCatalog,
                BuildResourceBindings().Values
                    .OrderBy(
                        binding => binding.ResourceNodeId,
                        StringComparer.Ordinal)
                    .ToArray(),
                _lifetimeCancellation.Token);
    }

    private void BeginReset()
    {
        if (_database is null)
        {
            return;
        }

        _state = SalvageRepairSliceState.Saving;
        _status = "resetting vertical-slice slot";
        _resetTask = _database.ResetSlotAsync(
            SlotId,
            _lifetimeCancellation.Token);
    }

    private void QueueCurrentSnapshot(AutosaveTrigger trigger)
    {
        if (_autosave is null || _player is null)
        {
            return;
        }

        _revision++;
        SaveGameSnapshot snapshot = StarterRepairSnapshotFactory.Create(
            SlotId,
            _revision,
            Session,
            _player.GlobalPosition.X,
            _player.GlobalPosition.Y,
            _player.GlobalPosition.Z,
            TechnologyProgress.ToSaveData());
        _autosave.Request(trigger, snapshot);
        _autosaveElapsedSeconds = 0.0;
        _state = SalvageRepairSliceState.Saving;
        _status = $"autosave queued: {trigger}, rev={_revision}";
    }

    private async Task<GracefulExitResult> FlushGracefulExitAsync(
        SaveGameSnapshot snapshot)
    {
        if (_autosave is null)
        {
            return new GracefulExitResult(false, 0);
        }

        await _autosave.FlushAsync(
            AutosaveTrigger.GracefulExit,
            snapshot,
            _lifetimeCancellation.Token).ConfigureAwait(false);
        return new GracefulExitResult(true, snapshot.Revision);
    }

    private void TryBeginGracefulExit()
    {
        if (!_closeRequested || _gracefulExitTask is not null)
        {
            return;
        }

        CloseRecipeSelector();
        if (_initializeTask is not null ||
            _loadTask is not null ||
            _resetTask is not null ||
            _acceptanceTask is not null ||
            _contentAcceptanceTask is not null ||
            _craftingAcceptanceTask is not null ||
            _craftTimeAcceptanceTask is not null ||
            _thirdCraftingAcceptanceTask is not null ||
            _fourthCraftingAcceptanceTask is not null ||
            _catalogMatrixAcceptanceTask is not null ||
            _technologySelectorAcceptanceTask is not null ||
            (_autosave?.IsBusy ?? false) ||
            _player is null ||
            _autosave is null)
        {
            _state = SalvageRepairSliceState.Exiting;
            _status = "waiting for persistence before exit";
            return;
        }

        _revision++;
        SaveGameSnapshot snapshot = StarterRepairSnapshotFactory.Create(
            SlotId,
            _revision,
            Session,
            _player.GlobalPosition.X,
            _player.GlobalPosition.Y,
            _player.GlobalPosition.Z,
            TechnologyProgress.ToSaveData());
        _state = SalvageRepairSliceState.Exiting;
        _status = $"graceful-exit flush rev={snapshot.Revision}";
        GD.Print(
            "Vertical slice graceful-exit flush started: " +
            $"revision={snapshot.Revision}; " +
            $"salvage={Session.SalvageQuantity}; " +
            $"shipRepaired={(Session.ShipRepaired ? 1 : 0)}; " +
            $"crafted={CountCraftedStationRecipes()}/{StationRecipes.Count}; " +
            $"researchPoints={TechnologyProgress.ResearchPoints}; " +
            $"unlockedTech={TechnologyProgress.UnlockedCount}.");
        _gracefulExitTask = FlushGracefulExitAsync(snapshot);
    }

    private void PollInitializeTask()
    {
        if (_initializeTask is null || !_initializeTask.IsCompleted)
        {
            return;
        }

        Task<SaveDatabaseDiagnostics> task = _initializeTask;
        _initializeTask = null;
        try
        {
            _diagnostics = task.GetAwaiter().GetResult();
            _state = SalvageRepairSliceState.Loading;
            _status = "loading starter repair state";
            _loadTask = _database?.LoadAsync(
                SlotId,
                _lifetimeCancellation.Token);
        }
        catch (Exception exception)
        {
            Fail("initialization", exception);
        }
    }

    private void PollLoadTask()
    {
        if (_loadTask is null || !_loadTask.IsCompleted)
        {
            return;
        }

        Task<SaveGameSnapshot?> task = _loadTask;
        _loadTask = null;
        try
        {
            SaveGameSnapshot? snapshot = task.GetAwaiter().GetResult();
            _technologyProgression = TechnologyProgression.FromSaveData(
                ContentCatalog.Technologies,
                snapshot?.TechnologyProgress,
                DefaultResearchPoints);
            _session = StarterRepairSession.FromSnapshot(
                snapshot,
                BuildResourceBindings(),
                RepairRecipe,
                TechnologyProgress.IsUnlocked,
                StationRecipes.ToArray());
            _revision = snapshot?.Revision ?? 0;
            if (snapshot is not null && _player is not null)
            {
                _player.GlobalPosition = new Vector3(
                    (float)snapshot.Player.PositionX,
                    (float)snapshot.Player.PositionY,
                    (float)snapshot.Player.PositionZ);
            }

            CloseRecipeSelector();
            _craftTimer.Reset();
            _activeCraftingStation = null;
            _craftingInteractorName = "unknown";
            ApplySessionToScene();
            _state = SalvageRepairSliceState.Ready;
            _status = snapshot is null
                ? "new starter repair objective"
                : $"restored revision {_revision}";
            GD.Print(
                "TASK-062 vertical slice READY: " +
                $"revision={_revision}; " +
                $"salvage={Session.SalvageQuantity}; " +
                $"shipRepaired={(Session.ShipRepaired ? 1 : 0)}; " +
                $"crafted={CountCraftedStationRecipes()}/{StationRecipes.Count}; " +
                $"researchPoints={TechnologyProgress.ResearchPoints}; " +
                $"unlockedTech={TechnologyProgress.UnlockedCount}.");
        }
        catch (Exception exception)
        {
            Fail("load", exception);
        }
    }

    private void PollResetTask()
    {
        if (_resetTask is null || !_resetTask.IsCompleted)
        {
            return;
        }

        Task task = _resetTask;
        _resetTask = null;
        try
        {
            task.GetAwaiter().GetResult();
            _technologyProgression = new TechnologyProgression(
                ContentCatalog.Technologies,
                DefaultResearchPoints);
            _session = new StarterRepairSession(
                RepairRecipe,
                TechnologyProgress.IsUnlocked,
                StationRecipes.ToArray());
            _revision = 0;
            _autosaveElapsedSeconds = 0.0;
            CloseRecipeSelector();
            _craftTimer.Reset();
            _activeCraftingStation = null;
            _craftingInteractorName = "unknown";
            _lastDomainEvent = "GameplaySlotReset";
            if (_player is not null)
            {
                _player.GlobalPosition = new Vector3(0.0f, 1.05f, 5.5f);
                _player.Rotation = Vector3.Zero;
                _player.Velocity = Vector3.Zero;
            }

            ApplySessionToScene();
            _state = SalvageRepairSliceState.Ready;
            _status =
                $"slot reset; collect {Session.RequiredSalvage} x " +
                Session.SalvageDefinitionId;
            GD.Print("TASK-062 vertical slice slot reset PASS.");
        }
        catch (Exception exception)
        {
            Fail("reset", exception);
        }
    }

    private void PollAcceptanceTask()
    {
        if (_acceptanceTask is null || !_acceptanceTask.IsCompleted)
        {
            return;
        }

        Task<VerticalSliceAcceptanceReport> task = _acceptanceTask;
        _acceptanceTask = null;
        try
        {
            _acceptanceReport = task.GetAwaiter().GetResult();
            _acceptanceHud = _acceptanceReport.Passed
                ? $"PASS resources={_acceptanceReport.ResourcesCollected}, " +
                  $"blocked={(_acceptanceReport.RepairBlockedBeforeResources ? 1 : 0)}, " +
                  $"repaired={(_acceptanceReport.ShipRepaired ? 1 : 0)}, " +
                  $"autosave={(_acceptanceReport.QuestAutosaveObserved ? 1 : 0)}, " +
                  $"roundTrip={(_acceptanceReport.ExactRoundTrip ? 1 : 0)}"
                : $"FAIL {_acceptanceReport.Result}";
            _state = _acceptanceReport.Passed
                ? SalvageRepairSliceState.Passed
                : SalvageRepairSliceState.Failed;
            _status = _acceptanceReport.Result;
            string output = BuildAcceptanceOutput(_acceptanceReport);
            if (_acceptanceReport.Passed)
            {
                GD.Print(output);
            }
            else
            {
                GD.PushError(output);
            }
        }
        catch (Exception exception)
        {
            Fail("acceptance", exception);
        }
    }

    private void PollContentAcceptanceTask()
    {
        if (_contentAcceptanceTask is null ||
            !_contentAcceptanceTask.IsCompleted)
        {
            return;
        }

        Task<DataDrivenContentAcceptanceReport> task =
            _contentAcceptanceTask;
        _contentAcceptanceTask = null;
        try
        {
            _contentAcceptanceReport = task.GetAwaiter().GetResult();
            _contentAcceptanceHud = _contentAcceptanceReport.Passed
                ? $"PASS schema={_contentAcceptanceReport.SchemaVersion}, " +
                  $"items={_contentAcceptanceReport.ItemCount}, " +
                  $"resources={_contentAcceptanceReport.ResourceCount}, " +
                  $"recipes={_contentAcceptanceReport.RecipeCount}, " +
                  "dataDriven=1, invalidRejected=2"
                : $"FAIL {_contentAcceptanceReport.Result}";
            _state = _contentAcceptanceReport.Passed
                ? SalvageRepairSliceState.Passed
                : SalvageRepairSliceState.Failed;
            _status = _contentAcceptanceReport.Result;
            string output = BuildContentAcceptanceOutput(
                _contentAcceptanceReport);
            if (_contentAcceptanceReport.Passed)
            {
                GD.Print(output);
            }
            else
            {
                GD.PushError(output);
            }
        }
        catch (Exception exception)
        {
            Fail("content acceptance", exception);
        }
    }

    private void PollCraftingAcceptanceTask()
    {
        if (_craftingAcceptanceTask is null ||
            !_craftingAcceptanceTask.IsCompleted)
        {
            return;
        }

        Task<CraftingExpansionAcceptanceReport> task =
            _craftingAcceptanceTask;
        _craftingAcceptanceTask = null;
        try
        {
            _craftingAcceptanceReport = task.GetAwaiter().GetResult();
            _craftingAcceptanceHud = _craftingAcceptanceReport.Passed
                ? $"PASS resources={_craftingAcceptanceReport.ResourcesCollected}, " +
                  $"repairFirst={(_craftingAcceptanceReport.RepairPrerequisiteEnforced ? 1 : 0)}, " +
                  $"wrongStation={(_craftingAcceptanceReport.WrongStationRejected ? 1 : 0)}, " +
                  $"blocked={(_craftingAcceptanceReport.CraftBlockedBeforeResources ? 1 : 0)}, " +
                  $"crafted={(_craftingAcceptanceReport.Crafted ? 1 : 0)}, " +
                  $"roundTrip={(_craftingAcceptanceReport.ExactRoundTrip ? 1 : 0)}"
                : $"FAIL {_craftingAcceptanceReport.Result}";
            _state = _craftingAcceptanceReport.Passed
                ? SalvageRepairSliceState.Passed
                : SalvageRepairSliceState.Failed;
            _status = _craftingAcceptanceReport.Result;
            string output = BuildCraftingAcceptanceOutput(
                _craftingAcceptanceReport);
            if (_craftingAcceptanceReport.Passed)
            {
                GD.Print(output);
            }
            else
            {
                GD.PushError(output);
            }
        }
        catch (Exception exception)
        {
            Fail("crafting acceptance", exception);
        }
    }

    private void PollCraftTimeAcceptanceTask()
    {
        if (_craftTimeAcceptanceTask is null ||
            !_craftTimeAcceptanceTask.IsCompleted)
        {
            return;
        }

        Task<CraftTimeAcceptanceReport> task = _craftTimeAcceptanceTask;
        _craftTimeAcceptanceTask = null;
        try
        {
            _craftTimeAcceptanceReport = task.GetAwaiter().GetResult();
            _craftTimeAcceptanceHud = _craftTimeAcceptanceReport.Passed
                ? $"PASS duration={_craftTimeAcceptanceReport.DurationSeconds:0.0}, " +
                  $"started={(_craftTimeAcceptanceReport.Started ? 1 : 0)}, " +
                  $"duplicate={(_craftTimeAcceptanceReport.DuplicateStartRejected ? 1 : 0)}, " +
                  $"inputsHeld={(_craftTimeAcceptanceReport.InputsHeldUntilCompletion ? 1 : 0)}, " +
                  $"completed={(_craftTimeAcceptanceReport.CompletedAtConfiguredDuration ? 1 : 0)}, " +
                  $"single={(_craftTimeAcceptanceReport.SingleCompletion ? 1 : 0)}, " +
                  $"output={_craftTimeAcceptanceReport.ProducedOutputQuantity}"
                : $"FAIL {_craftTimeAcceptanceReport.Result}";
            _state = _craftTimeAcceptanceReport.Passed
                ? SalvageRepairSliceState.Passed
                : SalvageRepairSliceState.Failed;
            _status = _craftTimeAcceptanceReport.Result;
            string output = BuildCraftTimeAcceptanceOutput(
                _craftTimeAcceptanceReport);
            if (_craftTimeAcceptanceReport.Passed)
            {
                GD.Print(output);
            }
            else
            {
                GD.PushError(output);
            }
        }
        catch (Exception exception)
        {
            Fail("craft-time acceptance", exception);
        }
    }

    private void PollThirdCraftingAcceptanceTask()
    {
        if (_thirdCraftingAcceptanceTask is null ||
            !_thirdCraftingAcceptanceTask.IsCompleted)
        {
            return;
        }

        Task<ThirdCraftingPathAcceptanceReport> task =
            _thirdCraftingAcceptanceTask;
        _thirdCraftingAcceptanceTask = null;
        try
        {
            _thirdCraftingAcceptanceReport = task.GetAwaiter().GetResult();
            _thirdCraftingAcceptanceHud =
                _thirdCraftingAcceptanceReport.Passed
                    ? $"PASS resources={_thirdCraftingAcceptanceReport.ResourcesCollected}, " +
                      $"blocked={(_thirdCraftingAcceptanceReport.BlockedBeforeResources ? 1 : 0)}, " +
                      $"timed={(_thirdCraftingAcceptanceReport.TimedCompletion ? 1 : 0)}, " +
                      $"isolated={(_thirdCraftingAcceptanceReport.RecipeIsolation ? 1 : 0)}, " +
                      $"both={(_thirdCraftingAcceptanceReport.BothRecipesCrafted ? 1 : 0)}, " +
                      $"output={_thirdCraftingAcceptanceReport.NavigationOutputQuantity}, " +
                      $"roundTrip={(_thirdCraftingAcceptanceReport.ExactRoundTrip ? 1 : 0)}"
                    : $"FAIL {_thirdCraftingAcceptanceReport.Result}";
            _state = _thirdCraftingAcceptanceReport.Passed
                ? SalvageRepairSliceState.Passed
                : SalvageRepairSliceState.Failed;
            _status = _thirdCraftingAcceptanceReport.Result;
            string output = BuildThirdCraftingPathAcceptanceOutput(
                _thirdCraftingAcceptanceReport);
            if (_thirdCraftingAcceptanceReport.Passed)
            {
                GD.Print(output);
            }
            else
            {
                GD.PushError(output);
            }
        }
        catch (Exception exception)
        {
            Fail("third crafting path acceptance", exception);
        }
    }

    private void PollFourthCraftingAcceptanceTask()
    {
        if (_fourthCraftingAcceptanceTask is null ||
            !_fourthCraftingAcceptanceTask.IsCompleted)
        {
            return;
        }

        Task<FourthCraftingPathAcceptanceReport> task =
            _fourthCraftingAcceptanceTask;
        _fourthCraftingAcceptanceTask = null;
        try
        {
            _fourthCraftingAcceptanceReport = task.GetAwaiter().GetResult();
            _fourthCraftingAcceptanceHud =
                _fourthCraftingAcceptanceReport.Passed
                    ? $"PASS resources={_fourthCraftingAcceptanceReport.ResourcesCollected}, " +
                      $"blocked={(_fourthCraftingAcceptanceReport.BlockedBeforeResources ? 1 : 0)}, " +
                      $"timed={(_fourthCraftingAcceptanceReport.TimedCompletion ? 1 : 0)}, " +
                      $"isolated={(_fourthCraftingAcceptanceReport.RecipeIsolation ? 1 : 0)}, " +
                      $"all3={(_fourthCraftingAcceptanceReport.AllThreeRecipesCrafted ? 1 : 0)}, " +
                      $"output={_fourthCraftingAcceptanceReport.CoolantOutputQuantity}, " +
                      $"roundTrip={(_fourthCraftingAcceptanceReport.ExactRoundTrip ? 1 : 0)}"
                    : $"FAIL {_fourthCraftingAcceptanceReport.Result}";
            _state = _fourthCraftingAcceptanceReport.Passed
                ? SalvageRepairSliceState.Passed
                : SalvageRepairSliceState.Failed;
            _status = _fourthCraftingAcceptanceReport.Result;
            string output = BuildFourthCraftingPathAcceptanceOutput(
                _fourthCraftingAcceptanceReport);
            if (_fourthCraftingAcceptanceReport.Passed)
            {
                GD.Print(output);
            }
            else
            {
                GD.PushError(output);
            }
        }
        catch (Exception exception)
        {
            Fail("fourth crafting path acceptance", exception);
        }
    }

    private void PollCatalogMatrixAcceptanceTask()
    {
        if (_catalogMatrixAcceptanceTask is null ||
            !_catalogMatrixAcceptanceTask.IsCompleted)
        {
            return;
        }

        Task<CatalogCraftingMatrixAcceptanceReport> task =
            _catalogMatrixAcceptanceTask;
        _catalogMatrixAcceptanceTask = null;
        try
        {
            _catalogMatrixAcceptanceReport = task.GetAwaiter().GetResult();
            _catalogMatrixAcceptanceHud =
                _catalogMatrixAcceptanceReport.Passed
                    ? $"PASS resources={_catalogMatrixAcceptanceReport.ResourceDefinitions}, " +
                      $"recipes={_catalogMatrixAcceptanceReport.RecipeDefinitions}, " +
                      $"station={_catalogMatrixAcceptanceReport.StationRecipes}, " +
                      $"crafted={_catalogMatrixAcceptanceReport.CraftedRecipes}, " +
                      $"isolated={_catalogMatrixAcceptanceReport.IsolatedRecipes}, " +
                      $"roundTrip={(_catalogMatrixAcceptanceReport.ExactRoundTrip ? 1 : 0)}"
                    : $"FAIL {_catalogMatrixAcceptanceReport.Result}";
            _state = _catalogMatrixAcceptanceReport.Passed
                ? SalvageRepairSliceState.Passed
                : SalvageRepairSliceState.Failed;
            _status = _catalogMatrixAcceptanceReport.Result;
            string output = BuildCatalogMatrixAcceptanceOutput(
                _catalogMatrixAcceptanceReport);
            if (_catalogMatrixAcceptanceReport.Passed)
            {
                GD.Print(output);
            }
            else
            {
                GD.PushError(output);
            }
        }
        catch (Exception exception)
        {
            Fail("catalog crafting matrix acceptance", exception);
        }
    }

    private void PollProductionQueueAcceptanceTask()
    {
        if (_productionQueueAcceptanceTask is null ||
            !_productionQueueAcceptanceTask.IsCompleted)
        {
            return;
        }

        Task<ProductionQueueAcceptanceReport> task =
            _productionQueueAcceptanceTask;
        _productionQueueAcceptanceTask = null;
        try
        {
            _productionQueueAcceptanceReport =
                task.GetAwaiter().GetResult();
            ProductionQueueAcceptanceReport report =
                _productionQueueAcceptanceReport;
            _productionQueueAcceptanceHud = report.Passed
                ? $"PASS slots={report.ParallelSlots}, " +
                  $"queued={(report.ThirdJobQueued ? 1 : 0)}, " +
                  $"pause={(report.PauseResumePreservedProgress ? 1 : 0)}, " +
                  $"restore={(report.GracefulExitRestored ? 1 : 0)}, " +
                  $"cancel={(report.ActiveCancellation ? 1 : 0)}, " +
                  $"refund={(report.RefundExact ? 1 : 0)}, " +
                  $"completed={report.CompletedProcesses}, " +
                  $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}"
                : $"FAIL {report.Result}";
            _state = report.Passed
                ? SalvageRepairSliceState.Passed
                : SalvageRepairSliceState.Failed;
            _status = report.Result;
            string output =
                "TASK-090 production queue acceptance " +
                $"{(report.Passed ? "PASS" : "FAIL")}: " +
                $"station={report.StationId}; " +
                $"slots={report.ParallelSlots}; " +
                $"maxParallel={report.MaximumParallelRunning}; " +
                $"thirdQueued={(report.ThirdJobQueued ? 1 : 0)}; " +
                $"pauseResume={(report.PauseResumePreservedProgress ? 1 : 0)}; " +
                $"gracefulRestore={(report.GracefulExitRestored ? 1 : 0)}; " +
                $"activeCancel={(report.ActiveCancellation ? 1 : 0)}; " +
                $"refundExact={(report.RefundExact ? 1 : 0)}; " +
                $"completed={report.CompletedProcesses}; " +
                $"queueDrained={(report.QueueDrained ? 1 : 0)}; " +
                $"energyRemaining={report.EnergyRemaining.ToString("0.###", CultureInfo.InvariantCulture)}; " +
                $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}; " +
                $"logWritten={(report.LogWritten ? 1 : 0)}; " +
                $"maxWriters={report.Diagnostics.MaximumConcurrentWriters}; " +
                $"integrity={report.Diagnostics.IntegrityResult}; " +
                $"elapsedMs={report.ElapsedMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)}; " +
                $"result={report.Result}";
            if (report.Passed)
            {
                GD.Print(output);
            }
            else
            {
                GD.PushError(output);
            }
        }
        catch (Exception exception)
        {
            Fail("production queue acceptance", exception);
        }
    }

    private void PollChemicalProcessAcceptanceTask()
    {
        if (_chemicalProcessAcceptanceTask is null ||
            !_chemicalProcessAcceptanceTask.IsCompleted)
        {
            return;
        }

        Task<ChemicalProcessAcceptanceReport> task =
            _chemicalProcessAcceptanceTask;
        _chemicalProcessAcceptanceTask = null;
        try
        {
            _chemicalProcessAcceptanceReport =
                task.GetAwaiter().GetResult();
            ChemicalProcessAcceptanceReport report =
                _chemicalProcessAcceptanceReport;
            _chemicalProcessAcceptanceHud = report.Passed
                ? $"PASS batch={report.RequestedBatches}, " +
                  $"energy={(report.EnergyRejected ? 1 : 0)}, " +
                  $"environment={(report.TemperatureRejected && report.PressureRejected ? 1 : 0)}, " +
                  $"vacuum={(report.VacuumRejected ? 1 : 0)}, " +
                  $"catalyst={(report.CatalystRetained && report.CatalystConsumed ? 1 : 0)}, " +
                  $"byproduct={(report.ByproductsProduced ? 1 : 0)}, " +
                  $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}"
                : $"FAIL {report.Result}";
            _state = report.Passed
                ? SalvageRepairSliceState.Passed
                : SalvageRepairSliceState.Failed;
            _status = report.Result;
            string output =
                "TASK-083 chemical process runtime acceptance " +
                $"{(report.Passed ? "PASS" : "FAIL")}: " +
                $"batchRecipe={report.BatchRecipeId}; " +
                $"vacuumRecipe={report.VacuumRecipeId}; " +
                $"batches={report.RequestedBatches}; " +
                $"energyRejected={(report.EnergyRejected ? 1 : 0)}; " +
                $"temperatureRejected={(report.TemperatureRejected ? 1 : 0)}; " +
                $"pressureRejected={(report.PressureRejected ? 1 : 0)}; " +
                $"vacuumRejected={(report.VacuumRejected ? 1 : 0)}; " +
                $"missingCatalystRejected={(report.MissingCatalystRejected ? 1 : 0)}; " +
                $"catalystRetained={(report.CatalystRetained ? 1 : 0)}; " +
                $"catalystConsumed={(report.CatalystConsumed ? 1 : 0)}; " +
                $"byproducts={(report.ByproductsProduced ? 1 : 0)}; " +
                $"batchOutput={(report.BatchOutputCorrect ? 1 : 0)}; " +
                $"hazards={(report.HazardsExposed ? 1 : 0)}; " +
                $"energyConsumed={report.EnergyConsumed.ToString("0.###", CultureInfo.InvariantCulture)}; " +
                $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}; " +
                $"logWritten={(report.LogWritten ? 1 : 0)}; " +
                $"maxWriters={report.Diagnostics.MaximumConcurrentWriters}; " +
                $"integrity={report.Diagnostics.IntegrityResult}; " +
                $"elapsedMs={report.ElapsedMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)}; " +
                $"result={report.Result}";
            if (report.Passed)
            {
                GD.Print(output);
            }
            else
            {
                GD.PushError(output);
            }
        }
        catch (Exception exception)
        {
            Fail("chemical process runtime acceptance", exception);
        }
    }

    private void PollTechnologySelectorAcceptanceTask()
    {
        if (_technologySelectorAcceptanceTask is null ||
            !_technologySelectorAcceptanceTask.IsCompleted)
        {
            return;
        }

        Task<TechnologyRecipeSelectorAcceptanceReport> task =
            _technologySelectorAcceptanceTask;
        _technologySelectorAcceptanceTask = null;
        try
        {
            _technologySelectorAcceptanceReport =
                task.GetAwaiter().GetResult();
            TechnologyRecipeSelectorAcceptanceReport report =
                _technologySelectorAcceptanceReport;
            _technologySelectorAcceptanceHud = report.Passed
                ? $"PASS recipes={report.RecipesListed}, " +
                  $"oneStation={report.PhysicalStationsRequired}, " +
                  $"initial={report.InitiallyUnlockedRecipes}/" +
                  $"{report.InitiallyLockedRecipes}, " +
                  $"unlocked={report.TechnologiesUnlocked}, " +
                  $"crafted={(report.SelectedRecipeCrafted ? 1 : 0)}, " +
                  $"rp={report.ResearchPointsRemaining}, " +
                  $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}"
                : $"FAIL {report.Result}";
            _state = report.Passed
                ? SalvageRepairSliceState.Passed
                : SalvageRepairSliceState.Failed;
            _status = report.Result;
            string output =
                "TASK-082 station selector and research acceptance " +
                $"{(report.Passed ? "PASS" : "FAIL")}: " +
                $"recipes={report.RecipesListed}; " +
                $"physicalStations={report.PhysicalStationsRequired}; " +
                $"initiallyUnlocked={report.InitiallyUnlockedRecipes}; " +
                $"initiallyLocked={report.InitiallyLockedRecipes}; " +
                $"prerequisiteRejected={(report.MissingPrerequisiteRejected ? 1 : 0)}; " +
                $"technologiesUnlocked={report.TechnologiesUnlocked}; " +
                $"allRecipesUnlocked={(report.AllRecipesUnlocked ? 1 : 0)}; " +
                $"technologyBlocked={(report.TechnologyBlockedBeforeResearch ? 1 : 0)}; " +
                $"readyAfterResearch={(report.CraftReadyAfterResearch ? 1 : 0)}; " +
                $"crafted={(report.SelectedRecipeCrafted ? 1 : 0)}; " +
                $"researchPoints={report.ResearchPointsRemaining}; " +
                $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}; " +
                $"progressRestored={(report.ProgressRestored ? 1 : 0)}; " +
                $"maxWriters={report.Diagnostics.MaximumConcurrentWriters}; " +
                $"integrity={report.Diagnostics.IntegrityResult}; " +
                $"elapsedMs={report.ElapsedMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)}; " +
                $"result={report.Result}";
            if (report.Passed)
            {
                GD.Print(output);
            }
            else
            {
                GD.PushError(output);
            }
        }
        catch (Exception exception)
        {
            Fail("technology selector acceptance", exception);
        }
    }

    private void PollAutosave()
    {
        if (_autosave is null)
        {
            return;
        }

        if (_autosave.FailedBatches > _observedAutosaveFailures)
        {
            _observedAutosaveFailures = _autosave.FailedBatches;
            _state = SalvageRepairSliceState.Failed;
            _status = $"autosave FAIL: {_autosave.LastErrorMessage}";
            GD.PushError(
                $"TASK-062 vertical slice autosave failed: " +
                $"{_autosave.LastErrorMessage}");
            return;
        }

        if (_autosave.CompletedBatches <= _observedAutosaveBatches)
        {
            return;
        }

        _observedAutosaveBatches = _autosave.CompletedBatches;
        _state = SalvageRepairSliceState.Ready;
        _status =
            $"autosave PASS rev={_autosave.LastSavedRevision}, " +
            $"trigger={_autosave.LastCompletedTriggerSummary}";
        GD.Print(
            "Vertical slice autosave PASS: " +
            $"revision={_autosave.LastSavedRevision}; " +
            $"triggers={_autosave.LastCompletedTriggerSummary}; " +
            $"salvage={Session.SalvageQuantity}; " +
            $"shipRepaired={(Session.ShipRepaired ? 1 : 0)}; " +
            $"crafted={CountCraftedStationRecipes()}/{StationRecipes.Count}; pending=0");
    }

    private void PollGracefulExitTask()
    {
        if (_gracefulExitTask is null || !_gracefulExitTask.IsCompleted)
        {
            return;
        }

        Task<GracefulExitResult> task = _gracefulExitTask;
        _gracefulExitTask = null;
        try
        {
            GracefulExitResult result = task.GetAwaiter().GetResult();
            GD.Print(
                "Vertical slice graceful-exit autosave PASS: " +
                $"saved={(result.Saved ? 1 : 0)}; " +
                $"revision={result.Revision}; pending=0");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            _closeRequested = false;
            Fail("graceful exit", exception);
        }
    }

    private void UpdatePeriodicAutosave(double delta)
    {
        if ((_state != SalvageRepairSliceState.Ready &&
             _state != SalvageRepairSliceState.Passed) ||
            _closeRequested ||
            (_autosave?.IsBusy ?? true))
        {
            return;
        }

        _autosaveElapsedSeconds += delta;
        if (_autosaveElapsedSeconds >= AutosaveIntervalSeconds)
        {
            QueueCurrentSnapshot(AutosaveTrigger.Periodic);
        }
    }

    private void ApplySessionToScene()
    {
        foreach (SalvageResourceNode node in _resourceNodes)
        {
            node.SetCollected(
                Session.CollectedNodeIds.Contains(node.ResourceNodeId));
        }

        _shipTerminal?.SetRepaired(Session.ShipRepaired);
        foreach (PortableCraftingStation station in _craftingStations)
        {
            station.SetCrafting(false);
            CraftingRecipeDefinition[] stationRecipes = StationRecipes
                .Where(recipe => string.Equals(
                    recipe.RequiredStation,
                    station.StationId,
                    StringComparison.Ordinal))
                .ToArray();
            station.SetCrafted(
                stationRecipes.Length > 0 &&
                stationRecipes.All(recipe =>
                    Session.IsRecipeCrafted(recipe.RecipeId)));
        }

        _activeCraftingStation = null;
    }

    private void ApplyHudMode()
    {
        if (_hudMargin is null || _hudLabel is null ||
            _hudHiddenHint is null)
        {
            return;
        }

        bool hidden = _hudMode == SalvageRepairHudMode.Hidden;
        _hudMargin.Visible = !hidden;
        _hudHiddenHint.Visible = hidden;

        if (_hudMode == SalvageRepairHudMode.Compact)
        {
            _hudMargin.OffsetRight = 850.0f;
            _hudMargin.OffsetBottom = 380.0f;
            _hudLabel.CustomMinimumSize = new Vector2(800.0f, 325.0f);
        }
        else if (_hudMode == SalvageRepairHudMode.Detailed)
        {
            _hudMargin.OffsetRight = 1140.0f;
            _hudMargin.OffsetBottom = 650.0f;
            _hudLabel.CustomMinimumSize = new Vector2(1090.0f, 595.0f);
        }
    }

    private void UpdateHud()
    {
        if (_hudLabel is null)
        {
            return;
        }

        if (_selectorStation is not null)
        {
            UpdateRecipeSelector();
        }

        string databaseLine = _diagnostics is null
            ? "DB: initializing"
            : $"DB: {_state} • schema={_diagnostics.SchemaVersion} • " +
              $"integrity={_diagnostics.IntegrityResult} • " +
              $"writes={_database?.CompletedWrites ?? 0}";
        CraftingStackDefinition primaryInput = RepairRecipe.Inputs[0];
        CraftingStackDefinition primaryOutput = RepairRecipe.Outputs[0];
        int craftedCount = CountCraftedStationRecipes();
        int totalStationRecipes = StationRecipes.Count;
        CraftingRecipeDefinition? nextRecipe = StationRecipes.FirstOrDefault(
            recipe => !Session.IsRecipeCrafted(recipe.RecipeId));
        string craftProcess = _craftTimer.IsRunning
            ? $"RUNNING {_craftTimer.RecipeId} " +
              $"{_craftTimer.ElapsedSeconds:0.0}/" +
              $"{_craftTimer.DurationSeconds:0.0}s " +
              $"({_craftTimer.Progress01 * 100.0:0}%)"
            : craftedCount == totalStationRecipes
                ? "COMPLETE"
                : "idle";

        string objective;
        if (!Session.ShipRepaired)
        {
            objective = $"Objective: collect salvage " +
                $"{Session.SalvageQuantity}/{Session.RequiredSalvage}, " +
                "then interact with ship";
        }
        else if (_craftTimer.IsRunning)
        {
            objective = $"Objective: fabricating {_craftTimer.RecipeId}";
        }
        else if (nextRecipe is not null)
        {
            objective = $"Objective: components {craftedCount}/{totalStationRecipes}; " +
                $"next {BuildRecipeProgress(nextRecipe)}";
        }
        else
        {
            objective =
                $"Objective: COMPLETE - ship repaired and all " +
                $"{totalStationRecipes} station components crafted";
        }

        string ship = !Session.ShipRepaired
            ? $"Ship: DAMAGED • repair requires {Session.RequiredSalvage} " +
              Session.SalvageDefinitionId
            : $"Ship: REPAIRED • components={craftedCount}/" +
              $"{totalStationRecipes} READY";
        string contentLine =
            $"Content: schema={ContentCatalog.SchemaVersion} • " +
            $"items={ContentCatalog.Items.Count} • " +
            $"resources={ContentCatalog.Resources.Count} • " +
            $"recipes={ContentCatalog.Recipes.Count} • " +
            $"stations={ContentCatalog.Stations.Count} • " +
            $"tech={ContentCatalog.Technologies.Count}";
        string technologyLine =
            $"Research: RP={TechnologyProgress.ResearchPoints} • " +
            $"unlocked={TechnologyProgress.UnlockedCount}/" +
            $"{ContentCatalog.Technologies.Count} • " +
            "interact with the fabricator to open Recipes/Research";
        string repairLine =
            $"Repair: {RepairRecipe.RecipeId} • " +
            $"{primaryInput.Quantity}x{primaryInput.DefinitionId} -> " +
            $"{primaryOutput.Quantity}x{primaryOutput.DefinitionId}";
        string matrixLine =
            $"Craft catalog: stationRecipes={totalStationRecipes} • " +
            $"crafted={craftedCount}/{totalStationRecipes} • " +
            $"pending={totalStationRecipes - craftedCount} • " +
            $"physicalStations={_craftingStations.Count}";
        string pendingPreview = BuildPendingRecipePreview();
        double nextAutosave = Math.Max(
            0.0,
            AutosaveIntervalSeconds - _autosaveElapsedSeconds);
        string autosave = _autosave is null
            ? "Autosave: unavailable"
            : $"Autosave: {(_autosave.IsBusy ? "RUNNING" : "idle")} • " +
              $"lastRev={_autosave.LastSavedRevision} • " +
              $"last={_autosave.LastCompletedTriggerSummary} • " +
              $"next={nextAutosave.ToString("0.0", CultureInfo.InvariantCulture)}s";
        string interaction = _player?.GetInteractionPrompt() ??
            "interaction unavailable";

        if (_hudMode == SalvageRepairHudMode.Compact)
        {
            _hudLabel.Text =
                "VERTICAL SLICE 1 • STATION SELECTOR + RESEARCH • H - HUD\n" +
                $"{databaseLine}\n" +
                $"Progress: salvage={Session.SalvageQuantity}/{Session.RequiredSalvage} • " +
                $"components={craftedCount}/{totalStationRecipes} • rev={_revision}\n" +
                $"Craft: {craftProcess}\n" +
                $"{technologyLine}\n" +
                $"Interaction: {interaction}\n" +
                $"TASK-090 production queue (F1): {_productionQueueAcceptanceHud}\n" +
                $"TASK-083 chemical runtime (F2): {_chemicalProcessAcceptanceHud}\n" +
                $"TASK-082 selector/research (F3): {_technologySelectorAcceptanceHud}\n" +
                $"TASK-080 industry catalog (F4): {_industryCatalogAcceptanceHud}\n" +
                $"TASK-076 runtime matrix (F5): {_catalogMatrixAcceptanceHud}\n" +
                $"Status: {_status}\n" +
                "E - interact/select • F1 - production queue • " +
                "F2 - chemical runtime • " +
                "F3 - selector acceptance • F4/F5 - catalogs • " +
                "F6/F7/F9/F10/F11/F12 - regressions";
            return;
        }

        _hudLabel.Text =
            "VERTICAL SLICE 1 - SALVAGE -> REPAIR -> RESEARCH -> CRAFT -> AUTOSAVE • H - HUD\n" +
            databaseLine + "\n" +
            contentLine + "\n" +
            technologyLine + "\n" +
            repairLine + "\n" +
            matrixLine + "\n" +
            pendingPreview + "\n" +
            $"Craft process: {craftProcess}\n" +
            $"Snapshot: rev={_revision} • collected={Session.CollectedNodeCount}/" +
            $"{_resourceNodes.Count}\n" +
            objective + "\n" +
            ship + "\n" +
            $"Interaction: {interaction}\n" +
            autosave + "\n" +
            $"Last domain event: {_lastDomainEvent}\n" +
            $"TASK-090 production queue (F1): {_productionQueueAcceptanceHud}\n" +
            $"TASK-083 chemical runtime (F2): {_chemicalProcessAcceptanceHud}\n" +
            $"TASK-082 selector/research (F3): {_technologySelectorAcceptanceHud}\n" +
            $"TASK-080 industry catalog (F4): {_industryCatalogAcceptanceHud}\n" +
            $"TASK-076 runtime matrix (F5): {_catalogMatrixAcceptanceHud}\n" +
            $"TASK-072 legacy fourth path (F6): {_fourthCraftingAcceptanceHud}\n" +
            $"TASK-062 gameplay (F7): {_acceptanceHud}\n" +
            $"TASK-064 content (F9): {_contentAcceptanceHud}\n" +
            $"TASK-066 crafting (F10): {_craftingAcceptanceHud}\n" +
            $"TASK-068 craft time (F11): {_craftTimeAcceptanceHud}\n" +
            $"TASK-070 legacy third path (F12): {_thirdCraftingAcceptanceHud}\n" +
            $"Status: {_status}\n" +
            "WASD/Space - move • E - interact/select • H - HUD • " +
            "F1 - production queue acceptance • " +
            "F2 - chemical runtime acceptance • " +
            "F3 - selector/research acceptance • F4 - all 128 recipes • " +
            "F5 - runtime matrix • F6/F7/F9/F10/F11/F12 - regressions • " +
            "F8 - reset • Esc - close selector/release mouse";
    }

    private int CountCraftedStationRecipes()
    {
        return StationRecipes.Count(recipe =>
            Session.IsRecipeCrafted(recipe.RecipeId));
    }

    private string BuildPendingRecipePreview()
    {
        CraftingRecipeDefinition[] pending = StationRecipes
            .Where(recipe => !Session.IsRecipeCrafted(recipe.RecipeId))
            .Take(3)
            .ToArray();
        if (pending.Length == 0)
        {
            return "Pending recipes: none";
        }

        int totalPending = StationRecipes.Count -
            CountCraftedStationRecipes();
        string preview = string.Join(
            " • ",
            pending.Select(BuildRecipeProgress));
        int hidden = totalPending - pending.Length;
        return hidden > 0
            ? $"Pending recipes: {preview} • +{hidden} more"
            : $"Pending recipes: {preview}";
    }

    private string BuildRecipeProgress(CraftingRecipeDefinition recipe)
    {
        string inputs = string.Join(
            "+",
            recipe.Inputs.Select(input =>
                $"{Session.GetAvailableQuantity(input.DefinitionId)}/" +
                $"{input.Quantity} {GetShortContentId(input.DefinitionId)}"));
        string outputState = Session.IsRecipeCrafted(recipe.RecipeId)
            ? "READY"
            : TechnologyProgress.IsUnlocked(recipe.RequiredTechnology)
                ? "MISSING"
                : $"LOCKED:{GetShortContentId(recipe.RequiredTechnology)}";
        string stationName = _craftingStations
            .FirstOrDefault(station => string.Equals(
                station.StationId,
                recipe.RequiredStation,
                StringComparison.Ordinal))
            ?.Name.ToString() ?? recipe.RequiredStation;
        return $"{GetShortContentId(recipe.RecipeId)} " +
            $"[{inputs} -> {outputState}, {recipe.CraftTimeSeconds:0.##}s, " +
            $"{stationName}]";
    }

    private static string GetShortContentId(string stableId)
    {
        int separator = stableId.LastIndexOf('.');
        return separator >= 0 && separator + 1 < stableId.Length
            ? stableId[(separator + 1)..]
            : stableId;
    }

    private static string BuildCraftEventName(
        string recipeId,
        string suffix)
    {
        string shortId = GetShortContentId(recipeId);
        string[] parts = shortId.Split(
            '_',
            StringSplitOptions.RemoveEmptyEntries);
        string eventRoot = string.Concat(parts.Select(part =>
            char.ToUpperInvariant(part[0]) + part[1..]));
        return string.IsNullOrEmpty(eventRoot)
            ? $"Station{suffix}"
            : eventRoot + suffix;
    }

    private static string BuildAcceptanceOutput(
        VerticalSliceAcceptanceReport report)
    {
        return "TASK-062 vertical slice integration acceptance " +
            $"{(report.Passed ? "PASS" : "FAIL")}: " +
            $"resources={report.ResourcesCollected}; " +
            $"repairBlocked=" +
            $"{(report.RepairBlockedBeforeResources ? 1 : 0)}; " +
            $"shipRepaired={(report.ShipRepaired ? 1 : 0)}; " +
            $"questAutosave={(report.QuestAutosaveObserved ? 1 : 0)}; " +
            $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}; " +
            $"logWritten={(report.LogWritten ? 1 : 0)}; " +
            $"revision={report.Revision}; " +
            $"maxWriters={report.Diagnostics.MaximumConcurrentWriters}; " +
            $"integrity={report.Diagnostics.IntegrityResult}; " +
            $"elapsedMs={report.ElapsedMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)}; " +
            $"result={report.Result}";
    }

    private static string BuildContentAcceptanceOutput(
        DataDrivenContentAcceptanceReport report)
    {
        return "TASK-064 data-driven content acceptance " +
            $"{(report.Passed ? "PASS" : "FAIL")}: " +
            $"schema={report.SchemaVersion}; " +
            $"items={report.ItemCount}; " +
            $"resources={report.ResourceCount}; " +
            $"recipes={report.RecipeCount}; " +
            $"recipe={report.RecipeId}; " +
            $"required={report.ActualRequiredQuantity}; " +
            $"variantRequired={report.VariantRequiredQuantity}; " +
            $"blockedBelowVariant=" +
            $"{(report.BlockedBelowVariantThreshold ? 1 : 0)}; " +
            $"repairedAtVariant=" +
            $"{(report.RepairedAtVariantThreshold ? 1 : 0)}; " +
            $"outputs={report.ProducedOutputQuantity}; " +
            $"duplicateRejected={(report.DuplicateIdRejected ? 1 : 0)}; " +
            $"missingReferenceRejected=" +
            $"{(report.MissingReferenceRejected ? 1 : 0)}; " +
            $"stableIds={(report.StableIdsValidated ? 1 : 0)}; " +
            $"elapsedMs={report.ElapsedMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)}; " +
            $"result={report.Result}";
    }

    private static string BuildCraftingAcceptanceOutput(
        CraftingExpansionAcceptanceReport report)
    {
        return "TASK-066 crafting expansion acceptance " +
            $"{(report.Passed ? "PASS" : "FAIL")}: " +
            $"resources={report.ResourcesCollected}; " +
            $"repairPrerequisite={(report.RepairPrerequisiteEnforced ? 1 : 0)}; " +
            $"wrongStationRejected={(report.WrongStationRejected ? 1 : 0)}; " +
            $"blockedBeforeResources={(report.CraftBlockedBeforeResources ? 1 : 0)}; " +
            $"crafted={(report.Crafted ? 1 : 0)}; " +
            $"output={report.ProducedOutputQuantity}; " +
            $"questAutosave={(report.QuestAutosaveObserved ? 1 : 0)}; " +
            $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}; " +
            $"logWritten={(report.LogWritten ? 1 : 0)}; " +
            $"revision={report.Revision}; " +
            $"maxWriters={report.Diagnostics.MaximumConcurrentWriters}; " +
            $"integrity={report.Diagnostics.IntegrityResult}; " +
            $"elapsedMs={report.ElapsedMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)}; " +
            $"result={report.Result}";
    }

    private static string BuildCraftTimeAcceptanceOutput(
        CraftTimeAcceptanceReport report)
    {
        return "TASK-068 data-driven craft-time acceptance " +
            $"{(report.Passed ? "PASS" : "FAIL")}: " +
            $"duration={report.DurationSeconds.ToString("0.0", CultureInfo.InvariantCulture)}; " +
            $"positiveDuration={(report.PositiveJsonDuration ? 1 : 0)}; " +
            $"started={(report.Started ? 1 : 0)}; " +
            $"duplicateRejected={(report.DuplicateStartRejected ? 1 : 0)}; " +
            $"inputsHeldUntilCompletion={(report.InputsHeldUntilCompletion ? 1 : 0)}; " +
            $"partialRunning={(report.PartialAdvanceStayedRunning ? 1 : 0)}; " +
            $"completedAtDuration={(report.CompletedAtConfiguredDuration ? 1 : 0)}; " +
            $"singleCompletion={(report.SingleCompletion ? 1 : 0)}; " +
            $"output={report.ProducedOutputQuantity}; " +
            $"elapsedMs={report.ElapsedMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)}; " +
            $"result={report.Result}";
    }

    private static string BuildThirdCraftingPathAcceptanceOutput(
        ThirdCraftingPathAcceptanceReport report)
    {
        return "TASK-070 third crafting path acceptance " +
            $"{(report.Passed ? "PASS" : "FAIL")}: " +
            $"resources={report.ResourcesCollected}; " +
            $"blockedBeforeResources={(report.BlockedBeforeResources ? 1 : 0)}; " +
            $"timedCompletion={(report.TimedCompletion ? 1 : 0)}; " +
            $"recipeIsolation={(report.RecipeIsolation ? 1 : 0)}; " +
            $"bothCrafted={(report.BothRecipesCrafted ? 1 : 0)}; " +
            $"output={report.NavigationOutputQuantity}; " +
            $"questAutosave={(report.QuestAutosaveObserved ? 1 : 0)}; " +
            $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}; " +
            $"logWritten={(report.LogWritten ? 1 : 0)}; " +
            $"revision={report.Revision}; " +
            $"maxWriters={report.Diagnostics.MaximumConcurrentWriters}; " +
            $"integrity={report.Diagnostics.IntegrityResult}; " +
            $"elapsedMs={report.ElapsedMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)}; " +
            $"result={report.Result}";
    }

    private static string BuildFourthCraftingPathAcceptanceOutput(
        FourthCraftingPathAcceptanceReport report)
    {
        return "TASK-072 fourth crafting path acceptance " +
            $"{(report.Passed ? "PASS" : "FAIL")}: " +
            $"resources={report.ResourcesCollected}; " +
            $"blockedBeforeResources={(report.BlockedBeforeResources ? 1 : 0)}; " +
            $"timedCompletion={(report.TimedCompletion ? 1 : 0)}; " +
            $"recipeIsolation={(report.RecipeIsolation ? 1 : 0)}; " +
            $"allThreeCrafted={(report.AllThreeRecipesCrafted ? 1 : 0)}; " +
            $"output={report.CoolantOutputQuantity}; " +
            $"questAutosave={(report.QuestAutosaveObserved ? 1 : 0)}; " +
            $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}; " +
            $"logWritten={(report.LogWritten ? 1 : 0)}; " +
            $"revision={report.Revision}; " +
            $"maxWriters={report.Diagnostics.MaximumConcurrentWriters}; " +
            $"integrity={report.Diagnostics.IntegrityResult}; " +
            $"elapsedMs={report.ElapsedMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)}; " +
            $"result={report.Result}";
    }

    private static string BuildCatalogMatrixAcceptanceOutput(
        CatalogCraftingMatrixAcceptanceReport report)
    {
        return "TASK-076 catalog crafting matrix acceptance " +
            $"{(report.Passed ? "PASS" : "FAIL")}: " +
            $"items={report.ItemDefinitions}; " +
            $"resources={report.ResourceDefinitions}; " +
            $"recipes={report.RecipeDefinitions}; " +
            $"stationRecipes={report.StationRecipes}; " +
            $"resourceNodes={report.ResourceNodes}; " +
            $"blocked={report.BlockedRecipes}; " +
            $"timed={report.TimedRecipes}; " +
            $"isolated={report.IsolatedRecipes}; " +
            $"crafted={report.CraftedRecipes}; " +
            $"output={report.ProducedOutputQuantity}; " +
            $"wrongStation={(report.WrongStationRejected ? 1 : 0)}; " +
            $"duplicateStart={(report.DuplicateStartRejected ? 1 : 0)}; " +
            $"questAutosave={(report.QuestAutosaveObserved ? 1 : 0)}; " +
            $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}; " +
            $"logWritten={(report.LogWritten ? 1 : 0)}; " +
            $"revision={report.Revision}; " +
            $"maxWriters={report.Diagnostics.MaximumConcurrentWriters}; " +
            $"integrity={report.Diagnostics.IntegrityResult}; " +
            $"elapsedMs={report.ElapsedMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)}; " +
            $"result={report.Result}";
    }

    private void Fail(string operation, Exception exception)
    {
        _state = SalvageRepairSliceState.Failed;
        _status = $"{operation} failed: {exception.Message}";
        GD.PushError(
            $"TASK-062 vertical slice {operation} failed: {exception}");
    }

    private static bool Matches(Key physical, Key logical, Key expected)
    {
        return physical == expected || logical == expected;
    }
}
