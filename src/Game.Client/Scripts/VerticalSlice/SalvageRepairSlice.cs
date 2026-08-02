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

public partial class SalvageRepairSlice : Node3D
{
    private sealed record GracefulExitResult(
        bool Saved,
        int Revision);

    private const string SlotId = StarterRepairSnapshotFactory.SlotId;

    [Export(PropertyHint.Range, "5.0,600.0,5.0")]
    public double AutosaveIntervalSeconds { get; set; } = 60.0;

    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly List<SalvageResourceNode> _resourceNodes = new();
    private readonly List<PortableCraftingStation> _craftingStations = new();
    private readonly DataDrivenCraftTimer _craftTimer = new();
    private SaveDatabase? _database;
    private SaveAutosaveCoordinator? _autosave;
    private GameContentCatalog? _contentCatalog;
    private CraftingRecipeDefinition? _repairRecipe;
    private CraftingRecipeDefinition? _launchCapacitorRecipe;
    private CraftingRecipeDefinition? _navigationArrayRecipe;
    private CraftingRecipeDefinition? _coolantRegulatorRecipe;
    private StarterRepairSession? _session;
    private StarterShipRepairTerminal? _shipTerminal;
    private PortableCraftingStation? _activeCraftingStation;
    private PlayerController? _player;
    private MarginContainer? _hudMargin;
    private Label? _hudLabel;
    private PanelContainer? _hudHiddenHint;
    private Task<SaveDatabaseDiagnostics>? _initializeTask;
    private Task<SaveGameSnapshot?>? _loadTask;
    private Task? _resetTask;
    private Task<VerticalSliceAcceptanceReport>? _acceptanceTask;
    private Task<DataDrivenContentAcceptanceReport>? _contentAcceptanceTask;
    private Task<CraftingExpansionAcceptanceReport>? _craftingAcceptanceTask;
    private Task<CraftTimeAcceptanceReport>? _craftTimeAcceptanceTask;
    private Task<ThirdCraftingPathAcceptanceReport>? _thirdCraftingAcceptanceTask;
    private Task<FourthCraftingPathAcceptanceReport>? _fourthCraftingAcceptanceTask;
    private Task<GracefulExitResult>? _gracefulExitTask;
    private SaveDatabaseDiagnostics? _diagnostics;
    private VerticalSliceAcceptanceReport? _acceptanceReport;
    private DataDrivenContentAcceptanceReport? _contentAcceptanceReport;
    private CraftingExpansionAcceptanceReport? _craftingAcceptanceReport;
    private CraftTimeAcceptanceReport? _craftTimeAcceptanceReport;
    private ThirdCraftingPathAcceptanceReport? _thirdCraftingAcceptanceReport;
    private FourthCraftingPathAcceptanceReport? _fourthCraftingAcceptanceReport;
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
    private string _craftingInteractorName = "unknown";
    private string _lastDomainEvent = "none";

    private StarterRepairSession Session => _session ??
        throw new InvalidOperationException("Starter repair session is unavailable.");

    private GameContentCatalog ContentCatalog => _contentCatalog ??
        throw new InvalidOperationException("Game content catalog is unavailable.");

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

    public override void _Ready()
    {
        _hudMargin = GetNodeOrNull<MarginContainer>(
            "Hud/MarginContainer");
        _hudLabel = GetNodeOrNull<Label>(
            "Hud/MarginContainer/PanelContainer/Label");
        _hudHiddenHint = GetNodeOrNull<PanelContainer>(
            "Hud/HiddenHint");
        _shipTerminal = GetNodeOrNull<StarterShipRepairTerminal>(
            "Gameplay/DamagedShip");
        _player = GetNodeOrNull<PlayerController>("Player");
        if (_hudMargin is null || _hudLabel is null ||
            _hudHiddenHint is null || _shipTerminal is null ||
            _player is null)
        {
            throw new InvalidOperationException(
                "Vertical slice scene is missing HUD, player or ship.");
        }

        GameContentCatalog catalog = LoadContentCatalog();
        SaveDatabase.RegisterKnownInventoryDefinitions(catalog.Items.Keys);
        CraftingRecipeDefinition repairRecipe = catalog.GetRecipe(
            StarterRepairContentIds.RecipeId);
        CraftingRecipeDefinition launchCapacitorRecipe = catalog.GetRecipe(
            VerticalSliceContentIds.LaunchCapacitorRecipeId);
        CraftingRecipeDefinition navigationArrayRecipe = catalog.GetRecipe(
            VerticalSliceContentIds.NavigationArrayRecipeId);
        CraftingRecipeDefinition coolantRegulatorRecipe = catalog.GetRecipe(
            VerticalSliceContentIds.CoolantRegulatorRecipeId);
        _contentCatalog = catalog;
        _repairRecipe = repairRecipe;
        _launchCapacitorRecipe = launchCapacitorRecipe;
        _navigationArrayRecipe = navigationArrayRecipe;
        _coolantRegulatorRecipe = coolantRegulatorRecipe;
        _session = new StarterRepairSession(
            repairRecipe,
            launchCapacitorRecipe,
            navigationArrayRecipe,
            coolantRegulatorRecipe);

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
            (left, right) => string.Compare(
                left.RecipeId,
                right.RecipeId,
                StringComparison.Ordinal));
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
        GD.Print(
            "TASK-064 data-driven vertical slice initializing. " +
            $"Recipe={RepairRecipe.RecipeId}; " +
            $"required={Session.RequiredSalvage} x {Session.SalvageDefinitionId}; " +
            $"secondRecipe={LaunchCapacitorRecipe.RecipeId}; " +
            $"thirdRecipe={NavigationArrayRecipe.RecipeId}; " +
            $"fourthRecipe={CoolantRegulatorRecipe.RecipeId}; " +
            $"craftTimes={LaunchCapacitorRecipe.CraftTimeSeconds:0.0}/" +
            $"{NavigationArrayRecipe.CraftTimeSeconds:0.0}/" +
            $"{CoolantRegulatorRecipe.CraftTimeSeconds:0.0}s. " +
            "Press F6/F7/F9/F10/F11/F12 for acceptance or F8 to reset.");
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
        if (Matches(physical, logical, Key.H))
        {
            _hudMode = (SalvageRepairHudMode)(((int)_hudMode + 1) % 3);
            ApplyHudMode();
            GD.Print($"Vertical slice HUD mode: {_hudMode}.");
            GetViewport().SetInputAsHandled();
            return;
        }

        if (Matches(physical, logical, Key.F6) && CanStartCommand())
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
        _lastDomainEvent = recipeId switch
        {
            VerticalSliceContentIds.LaunchCapacitorRecipeId =>
                "LaunchCapacitorCraftStarted",
            VerticalSliceContentIds.NavigationArrayRecipeId =>
                "NavigationArrayCraftStarted",
            VerticalSliceContentIds.CoolantRegulatorRecipeId =>
                "CoolantRegulatorCraftStarted",
            _ => "StationCraftStarted"
        };
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

        source.SetCrafted(true);
        _lastDomainEvent = recipeId switch
        {
            VerticalSliceContentIds.LaunchCapacitorRecipeId =>
                "LaunchCapacitorCrafted",
            VerticalSliceContentIds.NavigationArrayRecipeId =>
                "NavigationArrayCrafted",
            VerticalSliceContentIds.CoolantRegulatorRecipeId =>
                "CoolantRegulatorCrafted",
            _ => "StationRecipeCrafted"
        };
        QueueCurrentSnapshot(AutosaveTrigger.QuestCompleted);
        int outputQuantity = recipe.Outputs.Sum(output => output.Quantity);
        if (timed)
        {
            string prefix = recipeId switch
            {
                VerticalSliceContentIds.LaunchCapacitorRecipeId =>
                    "TASK-068 timed craft completion PASS: ",
                VerticalSliceContentIds.NavigationArrayRecipeId =>
                    "TASK-070 third crafting path completion PASS: ",
                VerticalSliceContentIds.CoolantRegulatorRecipeId =>
                    "TASK-072 fourth crafting path completion PASS: ",
                _ => "Station timed craft completion PASS: "
            };
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
        if (string.Equals(
            recipeId,
            LaunchCapacitorRecipe.RecipeId,
            StringComparison.Ordinal))
        {
            recipe = LaunchCapacitorRecipe;
            return true;
        }

        if (string.Equals(
            recipeId,
            NavigationArrayRecipe.RecipeId,
            StringComparison.Ordinal))
        {
            recipe = NavigationArrayRecipe;
            return true;
        }

        if (string.Equals(
            recipeId,
            CoolantRegulatorRecipe.RecipeId,
            StringComparison.Ordinal))
        {
            recipe = CoolantRegulatorRecipe;
            return true;
        }

        recipe = null!;
        return false;
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
            _ => "TASK-CRAFT"
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

        Dictionary<string, PortableCraftingStation> stationByRecipe = new(
            StringComparer.Ordinal);
        foreach (PortableCraftingStation station in _craftingStations)
        {
            if (!stationByRecipe.TryAdd(station.RecipeId, station))
            {
                throw new InvalidOperationException(
                    $"Duplicate crafting station recipe binding {station.RecipeId}.");
            }
        }

        CraftingRecipeDefinition[] stationRecipes =
        {
            LaunchCapacitorRecipe,
            NavigationArrayRecipe,
            CoolantRegulatorRecipe
        };
        foreach (CraftingRecipeDefinition recipe in stationRecipes)
        {
            if (!stationByRecipe.TryGetValue(
                recipe.RecipeId,
                out PortableCraftingStation? station) ||
                station is null ||
                !string.Equals(
                    station.StationId,
                    recipe.RequiredStation,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Recipe {recipe.RecipeId} requires station " +
                    $"{recipe.RequiredStation}, but the matching scene " +
                    "station is missing or misconfigured.");
            }

            if (!double.IsFinite(recipe.CraftTimeSeconds) ||
                recipe.CraftTimeSeconds <= 0.0)
            {
                throw new InvalidOperationException(
                    $"Recipe {recipe.RecipeId} must define a positive " +
                    "CraftTimeSeconds value.");
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
        bool unique = actualIds
            .Distinct(StringComparer.Ordinal)
            .Count() == actualIds.Length;
        if (!unique)
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
            int quantity = node.Quantity;
            availableByDefinition.TryGetValue(
                definition.ItemDefinitionId,
                out int current);
            availableByDefinition[definition.ItemDefinitionId] =
                current + quantity;
        }

        foreach (CraftingRecipeDefinition recipe in new[]
        {
            RepairRecipe,
            LaunchCapacitorRecipe,
            NavigationArrayRecipe,
            CoolantRegulatorRecipe
        })
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
                ContentCatalog.GetResource(node.ResourceDefinitionId).ItemDefinitionId,
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

        CraftingStackDefinition launchInput = LaunchCapacitorRecipe.Inputs[0];
        availableByDefinition.TryGetValue(
            launchInput.DefinitionId,
            out int launchAvailable);
        GD.Print(
            "TASK-066 crafting binding PASS: " +
            $"recipe={LaunchCapacitorRecipe.RecipeId}; " +
            $"resource={launchInput.DefinitionId}; " +
            $"required={launchInput.Quantity}; " +
            $"available={launchAvailable}; " +
            $"station={LaunchCapacitorRecipe.RequiredStation}; " +
            $"craftTime={LaunchCapacitorRecipe.CraftTimeSeconds.ToString("0.0", CultureInfo.InvariantCulture)}; " +
            $"items={ContentCatalog.Items.Count}; " +
            $"resources={ContentCatalog.Resources.Count}; " +
            $"recipes={ContentCatalog.Recipes.Count}.");
        GD.Print(
            "TASK-068 craft-time binding PASS: " +
            $"recipe={LaunchCapacitorRecipe.RecipeId}; " +
            $"duration={LaunchCapacitorRecipe.CraftTimeSeconds.ToString("0.0", CultureInfo.InvariantCulture)}; " +
            $"station={LaunchCapacitorRecipe.RequiredStation}; timer=DataDrivenCraftTimer.");

        CraftingStackDefinition navigationInput = NavigationArrayRecipe.Inputs[0];
        availableByDefinition.TryGetValue(
            navigationInput.DefinitionId,
            out int navigationAvailable);
        GD.Print(
            "TASK-070 third crafting path binding PASS: " +
            $"recipe={NavigationArrayRecipe.RecipeId}; " +
            $"resource={navigationInput.DefinitionId}; " +
            $"required={navigationInput.Quantity}; " +
            $"available={navigationAvailable}; " +
            $"station={NavigationArrayRecipe.RequiredStation}; " +
            $"craftTime={NavigationArrayRecipe.CraftTimeSeconds.ToString("0.0", CultureInfo.InvariantCulture)}; " +
            $"items={ContentCatalog.Items.Count}; " +
            $"resources={ContentCatalog.Resources.Count}; " +
            $"recipes={ContentCatalog.Recipes.Count}; stations={_craftingStations.Count}.");

        CraftingStackDefinition coolantInput = CoolantRegulatorRecipe.Inputs[0];
        availableByDefinition.TryGetValue(
            coolantInput.DefinitionId,
            out int coolantAvailable);
        GD.Print(
            "TASK-072 fourth crafting path binding PASS: " +
            $"recipe={CoolantRegulatorRecipe.RecipeId}; " +
            $"resource={coolantInput.DefinitionId}; " +
            $"required={coolantInput.Quantity}; " +
            $"available={coolantAvailable}; " +
            $"station={CoolantRegulatorRecipe.RequiredStation}; " +
            $"craftTime={CoolantRegulatorRecipe.CraftTimeSeconds.ToString("0.0", CultureInfo.InvariantCulture)}; " +
            $"items={ContentCatalog.Items.Count}; " +
            $"resources={ContentCatalog.Resources.Count}; " +
            $"recipes={ContentCatalog.Recipes.Count}; stations={_craftingStations.Count}.");
    }

    private static GameContentCatalog LoadContentCatalog()
    {
        const string itemsPath = "res://Content/items.json";
        const string resourcesPath = "res://Content/resources.json";
        const string recipesPath = "res://Content/recipes.json";
        string itemsJson = Godot.FileAccess.GetFileAsString(itemsPath);
        string resourcesJson = Godot.FileAccess.GetFileAsString(resourcesPath);
        string recipesJson = Godot.FileAccess.GetFileAsString(recipesPath);
        GameContentCatalog catalog = GameContentCatalog.LoadFromJson(
            itemsJson,
            resourcesJson,
            recipesJson);
        GD.Print(
            "TASK-064 content catalog READY: " +
            $"schema={catalog.SchemaVersion}; " +
            $"items={catalog.Items.Count}; " +
            $"resources={catalog.Resources.Count}; " +
            $"recipes={catalog.Recipes.Count}.");
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
            _gracefulExitTask is null &&
            !_craftTimer.IsRunning &&
            !_autosave.IsBusy &&
            !_closeRequested;
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
            _player.GlobalPosition.Z);
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

        if (_initializeTask is not null ||
            _loadTask is not null ||
            _resetTask is not null ||
            _acceptanceTask is not null ||
            _contentAcceptanceTask is not null ||
            _craftingAcceptanceTask is not null ||
            _craftTimeAcceptanceTask is not null ||
            _thirdCraftingAcceptanceTask is not null ||
            _fourthCraftingAcceptanceTask is not null ||
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
            _player.GlobalPosition.Z);
        _state = SalvageRepairSliceState.Exiting;
        _status = $"graceful-exit flush rev={snapshot.Revision}";
        GD.Print(
            "Vertical slice graceful-exit flush started: " +
            $"revision={snapshot.Revision}; " +
            $"salvage={Session.SalvageQuantity}; " +
            $"shipRepaired={(Session.ShipRepaired ? 1 : 0)}; " +
            $"launchCapacitor={(Session.IsRecipeCrafted(LaunchCapacitorRecipe.RecipeId) ? 1 : 0)}; " +
            $"navigationArray={(Session.IsRecipeCrafted(NavigationArrayRecipe.RecipeId) ? 1 : 0)}; " +
            $"coolantRegulator={(Session.IsRecipeCrafted(CoolantRegulatorRecipe.RecipeId) ? 1 : 0)}.");
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
            _session = StarterRepairSession.FromSnapshot(
                snapshot,
                BuildResourceBindings(),
                RepairRecipe,
                LaunchCapacitorRecipe,
                NavigationArrayRecipe,
                CoolantRegulatorRecipe);
            _revision = snapshot?.Revision ?? 0;
            if (snapshot is not null && _player is not null)
            {
                _player.GlobalPosition = new Vector3(
                    (float)snapshot.Player.PositionX,
                    (float)snapshot.Player.PositionY,
                    (float)snapshot.Player.PositionZ);
            }

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
                $"launchCapacitor={(Session.IsRecipeCrafted(LaunchCapacitorRecipe.RecipeId) ? 1 : 0)}; " +
                $"navigationArray={(Session.IsRecipeCrafted(NavigationArrayRecipe.RecipeId) ? 1 : 0)}; " +
                $"coolantRegulator={(Session.IsRecipeCrafted(CoolantRegulatorRecipe.RecipeId) ? 1 : 0)}.");
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
            _session = new StarterRepairSession(
                RepairRecipe,
                LaunchCapacitorRecipe,
                NavigationArrayRecipe,
                CoolantRegulatorRecipe);
            _revision = 0;
            _autosaveElapsedSeconds = 0.0;
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
            $"launchCapacitor={(Session.IsRecipeCrafted(LaunchCapacitorRecipe.RecipeId) ? 1 : 0)}; " +
            $"navigationArray={(Session.IsRecipeCrafted(NavigationArrayRecipe.RecipeId) ? 1 : 0)}; " +
            $"coolantRegulator={(Session.IsRecipeCrafted(CoolantRegulatorRecipe.RecipeId) ? 1 : 0)}; pending=0");
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
            station.SetCrafted(Session.IsRecipeCrafted(station.RecipeId));
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

        string databaseLine = _diagnostics is null
            ? "DB: initializing"
            : $"DB: {_state} • schema={_diagnostics.SchemaVersion} • " +
              $"integrity={_diagnostics.IntegrityResult} • " +
              $"writes={_database?.CompletedWrites ?? 0}";
        CraftingStackDefinition primaryInput = RepairRecipe.Inputs[0];
        CraftingStackDefinition primaryOutput = RepairRecipe.Outputs[0];
        CraftingStackDefinition launchInput = LaunchCapacitorRecipe.Inputs[0];
        CraftingStackDefinition launchOutput = LaunchCapacitorRecipe.Outputs[0];
        CraftingStackDefinition navigationInput = NavigationArrayRecipe.Inputs[0];
        CraftingStackDefinition navigationOutput = NavigationArrayRecipe.Outputs[0];
        CraftingStackDefinition coolantInput = CoolantRegulatorRecipe.Inputs[0];
        CraftingStackDefinition coolantOutput = CoolantRegulatorRecipe.Outputs[0];
        int launchResourceQuantity = Session.GetAvailableQuantity(
            launchInput.DefinitionId);
        int navigationResourceQuantity = Session.GetAvailableQuantity(
            navigationInput.DefinitionId);
        int coolantResourceQuantity = Session.GetAvailableQuantity(
            coolantInput.DefinitionId);
        bool launchCrafted = Session.IsRecipeCrafted(
            LaunchCapacitorRecipe.RecipeId);
        bool navigationCrafted = Session.IsRecipeCrafted(
            NavigationArrayRecipe.RecipeId);
        bool coolantCrafted = Session.IsRecipeCrafted(
            CoolantRegulatorRecipe.RecipeId);
        string activeRecipeId = _craftTimer.IsRunning
            ? _craftTimer.RecipeId
            : "none";
        string craftProcess = _craftTimer.IsRunning
            ? $"RUNNING {activeRecipeId} " +
              $"{_craftTimer.ElapsedSeconds:0.0}/" +
              $"{_craftTimer.DurationSeconds:0.0}s " +
              $"({_craftTimer.Progress01 * 100.0:0}%)"
            : launchCrafted && navigationCrafted && coolantCrafted
                ? "COMPLETE"
                : "idle";
        string objective;
        if (!Session.ShipRepaired)
        {
            objective = $"Objective 1/4: collect salvage " +
                $"{Session.SalvageQuantity}/{Session.RequiredSalvage}, " +
                "then interact with ship";
        }
        else if (_craftTimer.IsRunning)
        {
            objective = $"Objective: fabricating {_craftTimer.RecipeId} " +
                $"{_craftTimer.ElapsedSeconds:0.0}/" +
                $"{_craftTimer.DurationSeconds:0.0}s";
        }
        else if (!launchCrafted || !navigationCrafted || !coolantCrafted)
        {
            string launchGoal = launchCrafted
                ? "READY"
                : $"{launchResourceQuantity}/{launchInput.Quantity}";
            string navigationGoal = navigationCrafted
                ? "READY"
                : $"{navigationResourceQuantity}/{navigationInput.Quantity}";
            string coolantGoal = coolantCrafted
                ? "READY"
                : $"{coolantResourceQuantity}/{coolantInput.Quantity}";
            objective = $"Objectives: capacitor={launchGoal} at PortableFabricator • " +
                $"navigation={navigationGoal} at NavigationFabricator • " +
                $"coolant={coolantGoal} at CoolantFabricator";
        }
        else
        {
            objective = "Objective: COMPLETE — ship repaired and all three components crafted";
        }

        string ship = !Session.ShipRepaired
            ? $"Ship: DAMAGED • repair requires {Session.RequiredSalvage} " +
              Session.SalvageDefinitionId
            : $"Ship: REPAIRED • capacitor={(launchCrafted ? "READY" : "MISSING")} • " +
              $"navigation={(navigationCrafted ? "READY" : "MISSING")} • " +
              $"coolant={(coolantCrafted ? "READY" : "MISSING")}";
        string contentLine =
            $"Content: schema={ContentCatalog.SchemaVersion} • " +
            $"items={ContentCatalog.Items.Count} • " +
            $"resources={ContentCatalog.Resources.Count} • " +
            $"recipes={ContentCatalog.Recipes.Count}";
        string recipeLine =
            $"Repair: {RepairRecipe.RecipeId} • " +
            $"{primaryInput.Quantity}×{primaryInput.DefinitionId} → " +
            $"{primaryOutput.Quantity}×{primaryOutput.DefinitionId}";
        string launchRecipeLine =
            $"Craft A: {LaunchCapacitorRecipe.RecipeId} • " +
            $"{launchInput.Quantity}×{launchInput.DefinitionId} → " +
            $"{launchOutput.Quantity}×{launchOutput.DefinitionId} • " +
            $"time={LaunchCapacitorRecipe.CraftTimeSeconds:0.0}s";
        string navigationRecipeLine =
            $"Craft B: {NavigationArrayRecipe.RecipeId} • " +
            $"{navigationInput.Quantity}×{navigationInput.DefinitionId} → " +
            $"{navigationOutput.Quantity}×{navigationOutput.DefinitionId} • " +
            $"time={NavigationArrayRecipe.CraftTimeSeconds:0.0}s";
        string coolantRecipeLine =
            $"Craft C: {CoolantRegulatorRecipe.RecipeId} • " +
            $"{coolantInput.Quantity}×{coolantInput.DefinitionId} → " +
            $"{coolantOutput.Quantity}×{coolantOutput.DefinitionId} • " +
            $"time={CoolantRegulatorRecipe.CraftTimeSeconds:0.0}s";
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
                "VERTICAL SLICE 1 • MULTI-RECIPE • H — HUD\n" +
                $"{databaseLine}\n" +
                $"Progress: salvage={Session.SalvageQuantity}/{Session.RequiredSalvage} • " +
                $"crystal={launchResourceQuantity}/{launchInput.Quantity} • " +
                $"fiber={navigationResourceQuantity}/{navigationInput.Quantity} • " +
                $"gel={coolantResourceQuantity}/{coolantInput.Quantity} • " +
                $"capacitor={(launchCrafted ? "READY" : "MISSING")} • " +
                $"navigation={(navigationCrafted ? "READY" : "MISSING")} • " +
                $"coolant={(coolantCrafted ? "READY" : "MISSING")} • rev={_revision}\n" +
                $"Craft: {craftProcess}\n" +
                $"Content: schema={ContentCatalog.SchemaVersion} • " +
                $"items={ContentCatalog.Items.Count} • resources={ContentCatalog.Resources.Count} • " +
                $"recipes={ContentCatalog.Recipes.Count}\n" +
                $"Interaction: {interaction}\n" +
                $"Status: {_status}\n" +
                "E — interact • F6 — fourth path • F7 — gameplay • F8 — reset • " +
                "F9 — content • F10 — crafting • F11 — craft time • F12 — third path";
            return;
        }

        _hudLabel.Text =
            "VERTICAL SLICE 1 — SALVAGE → REPAIR → MULTI-CRAFT → AUTOSAVE • H — HUD\n" +
            databaseLine + "\n" +
            contentLine + "\n" +
            recipeLine + "\n" +
            launchRecipeLine + "\n" +
            navigationRecipeLine + "\n" +
            coolantRecipeLine + "\n" +
            $"Craft process: {craftProcess}\n" +
            $"Snapshot: rev={_revision} • collected={Session.CollectedNodeCount}/" +
            $"{_resourceNodes.Count}\n" +
            objective + "\n" +
            ship + "\n" +
            $"Interaction: {interaction}\n" +
            autosave + "\n" +
            $"Last domain event: {_lastDomainEvent}\n" +
            $"TASK-062 gameplay (F7): {_acceptanceHud}\n" +
            $"TASK-064 content (F9): {_contentAcceptanceHud}\n" +
            $"TASK-066 crafting (F10): {_craftingAcceptanceHud}\n" +
            $"TASK-068 craft time (F11): {_craftTimeAcceptanceHud}\n" +
            $"TASK-070 third path (F12): {_thirdCraftingAcceptanceHud}\n" +
            $"TASK-072 fourth path (F6): {_fourthCraftingAcceptanceHud}\n" +
            $"Status: {_status}\n" +
            "WASD/Space — move • E — collect/repair/craft • H — HUD • " +
            "F6 — fourth path • F7 — gameplay • F8 — reset • F9 — content • " +
            "F10 — crafting • F11 — craft time • F12 — third path • Esc — release mouse";
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
