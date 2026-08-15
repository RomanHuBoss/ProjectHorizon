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
    Research = 1,
    Queue = 2,
    Dismantle = 3
}

public enum StationServicesTab
{
    Dialogue = 0,
    Buy = 1,
    Sell = 2,
    Quests = 3
}

public enum ShipManagementTab
{
    Overview = 0,
    Modules = 1,
    Systems = 2
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
    private IReadOnlyList<CatalogResourcePlacement>
        _generatedResourcePlacements = Array.Empty<CatalogResourcePlacement>();
    private readonly List<PortableCraftingStation> _craftingStations = new();
    private readonly Dictionary<string, CraftingRecipeDefinition>
        _stationRecipes = new(StringComparer.Ordinal);
    private readonly DataDrivenCraftTimer _craftTimer = new();
    private SaveDatabase? _database;
    private SaveAutosaveCoordinator? _autosave;
    private GameContentCatalog? _contentCatalog;
    private StationServicesCatalog? _stationServicesCatalog;
    private StationServicesRuntime? _stationServicesRuntime;
    private BaseConstructionCatalog? _baseConstructionCatalog;
    private BaseConstructionRuntime? _baseConstructionRuntime;
    private PlanetaryPoiCatalog? _planetaryPoiCatalog;
    private PlanetaryExplorationRuntime? _planetaryExplorationRuntime;
    private ShipSystemsCatalog? _shipSystemsCatalog;
    private ShipSystemsRuntime? _shipSystemsRuntime;
    private IReadOnlyList<PlanetaryPoiPlacement> _planetaryPoiPlacements =
        Array.Empty<PlanetaryPoiPlacement>();
    private readonly List<PlanetaryPoiNode> _planetaryPoiNodes = new();
    private TechnologyProgression? _technologyProgression;
    private CraftingRecipeDefinition? _repairRecipe;
    private CraftingRecipeDefinition? _launchCapacitorRecipe;
    private CraftingRecipeDefinition? _navigationArrayRecipe;
    private CraftingRecipeDefinition? _coolantRegulatorRecipe;
    private CraftingRecipeDefinition? _powerCouplerRecipe;
    private StarterRepairSession? _session;
    private StarterShipRepairTerminal? _shipTerminal;
    private StationServicesNpc? _stationServicesNpc;
    private PortableCraftingStation? _activeCraftingStation;
    private PlayerController? _player;
    private MarginContainer? _hudMargin;
    private Label? _hudLabel;
    private PanelContainer? _hudHiddenHint;
    private PanelContainer? _recipeSelectorPanel;
    private Label? _recipeSelectorLabel;
    private PanelContainer? _stationServicesPanel;
    private Label? _stationServicesLabel;
    private PanelContainer? _baseConstructionPanel;
    private Label? _baseConstructionLabel;
    private PanelContainer? _discoveryCatalogPanel;
    private Label? _discoveryCatalogLabel;
    private PanelContainer? _shipManagementPanel;
    private Label? _shipManagementLabel;
    private Label? _playerCoordinatesLabel;
    private Node3D? _baseConstructionModulesRoot;
    private Node3D? _planetaryPoisRoot;
    private MeshInstance3D? _baseBuildPreview;
    private Task<SaveDatabaseDiagnostics>? _initializeTask;
    private Task<SaveGameSnapshot?>? _loadTask;
    private Task? _resetTask;
    private Task<VerticalSliceAcceptanceReport>? _acceptanceTask;
    private Task<CatalogResourceLifecycleAcceptanceReport>?
        _catalogResourceLifecycleAcceptanceTask;
    private Task<DataDrivenContentAcceptanceReport>? _contentAcceptanceTask;
    private Task<CraftingExpansionAcceptanceReport>? _craftingAcceptanceTask;
    private Task<CraftTimeAcceptanceReport>? _craftTimeAcceptanceTask;
    private Task<ThirdCraftingPathAcceptanceReport>? _thirdCraftingAcceptanceTask;
    private Task<FourthCraftingPathAcceptanceReport>? _fourthCraftingAcceptanceTask;
    private Task<CatalogCraftingMatrixAcceptanceReport>? _catalogMatrixAcceptanceTask;
    private Task<TechnologyRecipeSelectorAcceptanceReport>?
        _technologySelectorAcceptanceTask;
    private Task<StationServicesAcceptanceReport>?
        _stationServicesAcceptanceTask;
    private Task<BaseConstructionAcceptanceReport>?
        _baseConstructionAcceptanceTask;
    private Task<PlanetaryExplorationAcceptanceReport>?
        _planetaryExplorationAcceptanceTask;
    private Task<ShipSystemsAcceptanceReport>?
        _shipSystemsAcceptanceTask;
    private Task<ChemicalProcessAcceptanceReport>?
        _chemicalProcessAcceptanceTask;
    private Task<ProductionQueueAcceptanceReport>?
        _productionQueueAcceptanceTask;
    private Task<ItemQualityDismantleAcceptanceReport>?
        _itemQualityDismantleAcceptanceTask;
    private Task<MultiStationIndustryAcceptanceReport>?
        _multiStationIndustryAcceptanceTask;
    private Task<ProductionNetworkHudAcceptanceReport>?
        _productionNetworkHudAcceptanceTask;
    private Task<GracefulExitResult>? _gracefulExitTask;
    private SaveDatabaseDiagnostics? _diagnostics;
    private VerticalSliceAcceptanceReport? _acceptanceReport;
    private CatalogResourceLifecycleAcceptanceReport?
        _catalogResourceLifecycleAcceptanceReport;
    private DataDrivenContentAcceptanceReport? _contentAcceptanceReport;
    private CraftingExpansionAcceptanceReport? _craftingAcceptanceReport;
    private CraftTimeAcceptanceReport? _craftTimeAcceptanceReport;
    private ThirdCraftingPathAcceptanceReport? _thirdCraftingAcceptanceReport;
    private FourthCraftingPathAcceptanceReport? _fourthCraftingAcceptanceReport;
    private CatalogCraftingMatrixAcceptanceReport? _catalogMatrixAcceptanceReport;
    private TechnologyRecipeSelectorAcceptanceReport?
        _technologySelectorAcceptanceReport;
    private StationServicesAcceptanceReport?
        _stationServicesAcceptanceReport;
    private BaseConstructionAcceptanceReport?
        _baseConstructionAcceptanceReport;
    private PlanetaryExplorationAcceptanceReport?
        _planetaryExplorationAcceptanceReport;
    private ShipSystemsAcceptanceReport?
        _shipSystemsAcceptanceReport;
    private ChemicalProcessAcceptanceReport?
        _chemicalProcessAcceptanceReport;
    private ProductionQueueAcceptanceReport?
        _productionQueueAcceptanceReport;
    private ItemQualityDismantleAcceptanceReport?
        _itemQualityDismantleAcceptanceReport;
    private MultiStationIndustryAcceptanceReport?
        _multiStationIndustryAcceptanceReport;
    private ProductionNetworkHudAcceptanceReport?
        _productionNetworkHudAcceptanceReport;
    private ProductionNetworkRuntime? _gameplayProductionNetwork;
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
    private string _catalogResourceLifecycleAcceptanceHud = "READY";
    private string _contentAcceptanceHud = "READY";
    private string _craftingAcceptanceHud = "READY";
    private string _craftTimeAcceptanceHud = "READY";
    private string _thirdCraftingAcceptanceHud = "READY";
    private string _fourthCraftingAcceptanceHud = "READY";
    private string _catalogMatrixAcceptanceHud = "READY";
    private string _industryCatalogAcceptanceHud = "READY";
    private string _technologySelectorAcceptanceHud = "READY";
    private string _stationServicesAcceptanceHud = "READY";
    private string _baseConstructionAcceptanceHud = "READY";
    private string _planetaryExplorationAcceptanceHud = "READY";
    private string _shipSystemsAcceptanceHud = "READY";
    private string _chemicalProcessAcceptanceHud = "READY";
    private string _productionQueueAcceptanceHud = "READY";
    private string _queueTerminalAcceptanceHud = "READY";
    private string _itemQualityDismantleAcceptanceHud = "READY";
    private string _multiStationIndustryAcceptanceHud = "READY";
    private string _productionNetworkHudAcceptanceHud = "READY";
    private PortableCraftingStation? _selectorStation;
    private Node3D? _selectorInteractor;
    private StationSelectorMode _selectorMode = StationSelectorMode.Recipes;
    private int _selectorIndex;
    private string _selectorFeedback = "";
    private ulong _selectorOpenedTicks;
    private bool _stationServicesOpen;
    private StationServicesTab _stationServicesTab = StationServicesTab.Dialogue;
    private int _stationServicesIndex;
    private string _stationServicesFeedback = "";
    private ulong _stationServicesOpenedTicks;
    private bool _baseBuildMode;
    private int _baseBuildIndex;
    private int _baseBuildRotation;
    private string _baseBuildFeedback = "";
    private bool _discoveryCatalogOpen;
    private const ulong F4ReleaseQuietMilliseconds = 750;
    private bool _f4AcceptanceKeyLatched;
    private bool _f4ReleaseSeen;
    private ulong _f4LastSignalTicks;
    private int _discoveryCatalogIndex;
    private string _discoveryCatalogFeedback = "";
    private bool _shipManagementOpen;
    private ShipManagementTab _shipManagementTab = ShipManagementTab.Overview;
    private int _shipManagementIndex;
    private string _shipManagementFeedback = "";
    private ulong _shipManagementOpenedTicks;
    private string _craftingInteractorName = "unknown";
    private string _lastDomainEvent = "none";

    private StarterRepairSession Session => _session ??
        throw new InvalidOperationException("Starter repair session is unavailable.");

    private GameContentCatalog ContentCatalog => _contentCatalog ??
        throw new InvalidOperationException("Game content catalog is unavailable.");

    private StationServicesCatalog StationServiceCatalog =>
        _stationServicesCatalog ??
        throw new InvalidOperationException(
            "Station services catalog is unavailable.");

    private StationServicesRuntime StationServices =>
        _stationServicesRuntime ??
        throw new InvalidOperationException(
            "Station services runtime is unavailable.");

    private BaseConstructionCatalog BaseConstructionCatalog =>
        _baseConstructionCatalog ??
        throw new InvalidOperationException(
            "Base construction catalog is unavailable.");

    private BaseConstructionRuntime BaseConstruction =>
        _baseConstructionRuntime ??
        throw new InvalidOperationException(
            "Base construction runtime is unavailable.");

    private PlanetaryPoiCatalog PlanetaryPoiCatalog =>
        _planetaryPoiCatalog ??
        throw new InvalidOperationException(
            "Planetary POI catalog is unavailable.");

    private PlanetaryExplorationRuntime PlanetaryExploration =>
        _planetaryExplorationRuntime ??
        throw new InvalidOperationException(
            "Planetary exploration runtime is unavailable.");

    private ShipSystemsCatalog ShipSystemsCatalog =>
        _shipSystemsCatalog ??
        throw new InvalidOperationException(
            "Ship systems catalog is unavailable.");

    private ShipSystemsRuntime ShipSystems =>
        _shipSystemsRuntime ??
        throw new InvalidOperationException(
            "Ship systems runtime is unavailable.");

    private TechnologyProgression TechnologyProgress =>
        _technologyProgression ??
        throw new InvalidOperationException(
            "Technology progression is unavailable.");

    private ProductionNetworkRuntime GameplayNetwork =>
        _gameplayProductionNetwork ??
        throw new InvalidOperationException(
            "Gameplay production network is unavailable.");

    private ProductionQueueRuntime GetGameplayQueue(string stationId)
    {
        return GameplayNetwork.GetQueue(stationId);
    }

    private ProductionQueueRuntime SelectorQueue
    {
        get
        {
            PortableCraftingStation station = _selectorStation ??
                throw new InvalidOperationException(
                    "No station selector is currently open.");
            return GetGameplayQueue(station.StationId);
        }
    }

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

    private IReadOnlyList<CraftingRecipeDefinition> ObjectiveRecipes =>
        StationRecipes
            .Where(recipe => !IndustryRecipePolicy.IsRepeatable(recipe))
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
        _stationServicesPanel = GetNodeOrNull<PanelContainer>(
            "Hud/StationServices");
        _stationServicesLabel = GetNodeOrNull<Label>(
            "Hud/StationServices/Label");
        _baseConstructionPanel = GetNodeOrNull<PanelContainer>(
            "Hud/BaseConstruction");
        _baseConstructionLabel = GetNodeOrNull<Label>(
            "Hud/BaseConstruction/Label");
        _discoveryCatalogPanel = GetNodeOrNull<PanelContainer>(
            "Hud/DiscoveryCatalog");
        _discoveryCatalogLabel = GetNodeOrNull<Label>(
            "Hud/DiscoveryCatalog/Label");
        _shipManagementPanel = GetNodeOrNull<PanelContainer>(
            "Hud/ShipManagement");
        _shipManagementLabel = GetNodeOrNull<Label>(
            "Hud/ShipManagement/Label");
        _playerCoordinatesLabel = GetNodeOrNull<Label>(
            "Hud/PlayerCoordinates/Label");
        _baseConstructionModulesRoot = GetNodeOrNull<Node3D>(
            "Gameplay/BaseConstructionModules");
        _planetaryPoisRoot = GetNodeOrNull<Node3D>(
            "Gameplay/PlanetaryPois");
        _baseBuildPreview = GetNodeOrNull<MeshInstance3D>(
            "Gameplay/BaseBuildPreview");
        _shipTerminal = GetNodeOrNull<StarterShipRepairTerminal>(
            "Gameplay/DamagedShip");
        _stationServicesNpc = GetNodeOrNull<StationServicesNpc>(
            "Gameplay/StationTrader");
        _player = GetNodeOrNull<PlayerController>("Player");
        if (_hudMargin is null || _hudLabel is null ||
            _hudHiddenHint is null || _recipeSelectorPanel is null ||
            _recipeSelectorLabel is null || _stationServicesPanel is null ||
            _stationServicesLabel is null || _baseConstructionPanel is null ||
            _baseConstructionLabel is null || _discoveryCatalogPanel is null ||
            _discoveryCatalogLabel is null || _shipManagementPanel is null ||
            _shipManagementLabel is null || _playerCoordinatesLabel is null ||
            _baseConstructionModulesRoot is null || _planetaryPoisRoot is null ||
            _baseBuildPreview is null ||
            _shipTerminal is null || _stationServicesNpc is null ||
            _player is null)
        {
            throw new InvalidOperationException(
                "Vertical slice scene is missing HUD, player or ship.");
        }

        BindStageOneVoyageSceneNodes();
        BindGalaxyNavigationSceneNodes();
        BindEcologySceneNodes();
        BindNpcFactionSceneNodes();
        BindNpcNavigationSceneNodes();
        BindProceduralQuestSceneNodes();
        BindPlayerSurvivalSceneNodes();

        GameContentCatalog catalog = LoadContentCatalog();
        StationServicesCatalog stationServicesCatalog =
            LoadStationServicesCatalog(catalog);
        BaseConstructionCatalog baseConstructionCatalog =
            LoadBaseConstructionCatalog(catalog);
        PlanetaryPoiCatalog planetaryPoiCatalog =
            LoadPlanetaryPoiCatalog();
        ShipSystemsCatalog shipSystemsCatalog =
            LoadShipSystemsCatalog(catalog);
        _ecologyCatalog = LoadEcologyCatalog(catalog);
        _npcFactionCatalog = LoadNpcFactionCatalog(stationServicesCatalog);
        _proceduralQuestCatalog = LoadProceduralQuestCatalog(
            stationServicesCatalog);
        _playerSurvivalCatalog = LoadPlayerSurvivalCatalog(catalog);
        IReadOnlyList<PlanetaryPoiPlacement> planetaryPoiPlacements =
            PlanetaryPoiPlanner.Plan(planetaryPoiCatalog);
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
        _stationServicesCatalog = stationServicesCatalog;
        _stationServicesRuntime = new StationServicesRuntime(
            catalog,
            stationServicesCatalog,
            StationServicesAcceptanceRunner.NpcId);
        _baseConstructionCatalog = baseConstructionCatalog;
        _baseConstructionRuntime = new BaseConstructionRuntime(
            baseConstructionCatalog);
        _planetaryPoiCatalog = planetaryPoiCatalog;
        _planetaryPoiPlacements = planetaryPoiPlacements;
        _planetaryExplorationRuntime = new PlanetaryExplorationRuntime(
            planetaryPoiCatalog,
            planetaryPoiPlacements);
        _shipSystemsCatalog = shipSystemsCatalog;
        _shipSystemsRuntime = new ShipSystemsRuntime(shipSystemsCatalog);
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
        InitializeStageOneVoyageRuntime(saveData: null);
        InitializeGalaxyNavigationRuntime(saveData: null);
        InitializeEcologyRuntime(saveData: null);
        InitializeNpcFactionRuntime(saveData: null);
        InitializeProceduralQuestRuntime(saveData: null);
        InitializePlayerSurvivalRuntime(saveData: null);
        _generatedResourcePlacements =
            GenerateMissingCatalogResourceNodes(catalog);

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
        InitializeGameplayProductionNetwork(
            saveData: null,
            legacySaveData: null);
        RebuildBaseConstructionScene();
        RebuildPlanetaryPoiScene();
        RebuildNpcFactionScene();
        InitializeNpcNavigationSurface();

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
            "the playable runtime matrix, F6 for base construction plus legacy " +
            "regression, F9/F10/F11/F12 for regressions, F7 for complete " +
            "resource acceptance or F8 to reset. Press G for base build mode, " +
            "P for POI scanner pulse, J for the discovery catalog, V for ecology " +
            "scan, O for the ecology catalogue, Q for the procedural mission " +
            "journal on foot, I for exosuit/multitool and U for ship management. " +
            "Shift sprints, Ctrl crouches and holding Space airborne uses the jetpack. Station Services keeps " +
            "Q for its legacy quest tab. After starter repair press E on the ship to board; " +
            "T launches or undocks, K toggles navigation assist, Enter docks " +
            "or lands, and E opens services or disembarks. Press M for the " +
            "system/galaxy map.");
        GD.Print(
            "TASK-090 production queue READY: " +
            $"stations={ContentCatalog.Stations.Count}; " +
            $"parallelStations={ContentCatalog.Stations.Values.Count(
                station => station.ParallelSlots > 1)}; " +
            $"maxSlots={ContentCatalog.Stations.Values.Max(
                station => station.ParallelSlots)}; " +
            "reservation=enqueue; cancellationRefund=full; " +
            "gracefulExit=freeze-and-resume; offlineProgress=0.");
        GD.Print(
            "TASK-092 production queue terminal READY: " +
            "tabs=Recipes/Research/Queue/Dismantle; progress=bar+elapsed; " +
            "actions=pause/resume/cancel; energy=visible; " +
            "reservations=visible; gameplayPersistence=enabled.");
        GD.Print(
            "TASK-096 multi-station industry READY: " +
            $"physicalStations={_craftingStations.Count}; " +
            $"runtimeRecipes={StationRecipes.Count}; " +
            $"repeatableRecipes={StationRecipes.Count(IndustryRecipePolicy.IsRepeatable)}; " +
            $"networkStations={GameplayNetwork.StationIds.Count}; " +
            "starterLine=refined_ferrite,purified_water,paraffinium_fraction," +
            "paraffinium_lubricant,raw_compotium_solution," +
            "compotium_concentrate; recharge=60s-to-full.");
        GD.Print(
            "TASK-098 production network HUD READY: " +
            $"stations={GameplayNetwork.StationIds.Count}; " +
            "source=ProductionNetworkRuntime; aggregate=jobs+states+energy; " +
            "stationDetails=enabled; legacyFallback=enabled; " +
            "falseUnavailable=guarded.");
        GD.Print(
            "TASK-100 catalog resource lifecycle READY: " +
            $"catalog={ContentCatalog.Resources.Count}; " +
            $"physicalTypes={_resourceNodes.Select(node => node.ResourceDefinitionId).Distinct(StringComparer.Ordinal).Count()}; " +
            $"nodes={_resourceNodes.Count}; " +
            $"generated={_generatedResourcePlacements.Count}; " +
            "genericCollection=enabled; mirrors=enabled; " +
            "depletionPersistence=enabled; reset=enabled.");
        GD.Print(
            "TASK-106 base construction READY: " +
            $"modules={BaseConstructionCatalog.Modules.Count}; " +
            $"grid={BaseConstructionCatalog.GridSizeMeters.ToString("0.#", CultureInfo.InvariantCulture)}m; " +
            $"limits={BaseConstructionCatalog.Limits.MaximumModules}/" +
            $"{BaseConstructionCatalog.Limits.MaximumInteractiveDevices}/" +
            $"{BaseConstructionCatalog.Limits.MaximumActivePhysicsObjects}/" +
            $"{BaseConstructionCatalog.Limits.MaximumDynamicLights}; " +
            "snap=cardinal; power=graph; persistence=enabled; F6=acceptance.");
        GD.Print(
            "TASK-108 planetary exploration READY: " +
            $"types={PlanetaryPoiCatalog.Definitions.Count}; " +
            $"placements={_planetaryPoiPlacements.Count}; " +
            $"seed={PlanetaryPoiCatalog.WorldSeed}; " +
            $"region={PlanetaryPoiCatalog.RegionKey}; " +
            "scanner=P; catalog=J; placement=constraint-aware; " +
            "discoveries=persistent; F4=acceptance; " +
            "f4Gate=release-confirmed+750ms-event-silence.");
        GD.Print(
            "TASK-110 ship systems READY: " +
            $"classes={ShipSystemsCatalog.Classes.Count}; " +
            $"systems={ShipSystemsCatalog.Systems.Count}; " +
            $"modules={ShipSystemsCatalog.Modules.Count}; " +
            $"class={ShipSystems.ShipClassId}; " +
            $"commissioned={(ShipSystems.Commissioned ? 1 : 0)}; " +
            "loadout=U; damage=per-system; repair=inventory-backed; " +
            "persistence=enabled; F5=acceptance.");
        GD.Print(
            "TASK-112 Stage 1 voyage READY: " +
            "loop=repair>board>takeoff>orbital_station>return>land; " +
            "shipStats=live-derived; fuel=takeoff+dock+undock+landing; " +
            "readiness=ship-systems; persistence=enabled; F5=acceptance; " +
            "controls=E board/services/disembark,Enter dock/land,T launch/undock,K assist,F2 camera.");
        GD.Print(
            "TASK-114 galaxy navigation READY: " +
            $"galaxy={GalaxyNavigation.CurrentSystem.GalaxyId}; " +
            $"system={GalaxyNavigation.CurrentSystem.SystemId}; " +
            $"sector={GalaxyNavigation.CurrentSystem.SectorX}," +
            $"{GalaxyNavigation.CurrentSystem.SectorY}," +
            $"{GalaxyNavigation.CurrentSystem.SectorZ}; " +
            $"seed={GalaxyNavigation.UniverseSeed}; " +
            $"starTypes={Enum.GetValues<GalaxyStarType>().Length}; " +
            "generation=on-demand; coordinates=galaxy+sector+double3; " +
            "maps=M; route=A*+range; hyperspace=station-only; " +
            "persistence=enabled; F5=acceptance.");
        GD.Print(
            "TASK-116 ecology READY: " +
            $"biomes={EcologyCatalog.Biomes.Count}; " +
            $"flora={EcologyCatalog.Flora.Count}; " +
            $"fauna={EcologyCatalog.Fauna.Count}; " +
            $"instanced={EcologyPlan.Flora.Count}; " +
            $"active/simplified={EcologyPlan.ActiveFauna.Count}/{EcologyPlan.SimplifiedFauna.Count}; " +
            "scanner=V; catalog=O; ai=utility+steering; persistence=seed+deltas; F5=acceptance; " +
            $"atmosphere={(_voyageShip?.HasAtmosphereReference == true ? "bound" : "missing")}.");
        GD.Print(
            "TASK-118 procedural quests READY: " +
            $"objectiveTypes={ProceduralQuestCatalog.Profiles.Count}; " +
            $"board={ProceduralQuests.Board.Count}; " +
            $"maxActive={ProceduralQuestCatalog.MaximumActive}; " +
            $"gameplayTypes={ProceduralQuests.Board.Select(quest => quest.ObjectiveType).Distinct().Count()}; " +
            "journal=Q; graph=objective>return>claim; rewards=credits+faction-reputation; " +
            "feasibility=runtime-capabilities; persistence=delta-state; F5=acceptance.");
        GD.Print(
            "TASK-120 player survival READY: " +
            $"suit={PlayerSurvivalCatalog.SuitModules.Count}; " +
            $"multitool={PlayerSurvivalCatalog.MultitoolModules.Count}; " +
            $"consumables={PlayerSurvivalCatalog.Consumables.Count}; " +
            $"environments={PlayerSurvivalCatalog.Environments.Count}; " +
            "stats=health+shield+stamina+life-support+hazard+oxygen+jetpack+multitool; " +
            "movement=sprint+crouch+jump+jetpack+swim; equipment=I; mode=Z; persistence=enabled; F5=acceptance.");
        GD.Print(
            "TASK-122 NPC/factions READY: " +
            $"factions={NpcFactionCatalog.Factions.Count}; archetypes={NpcFactionCatalog.Archetypes.Count}; " +
            $"agents={NpcFactionCatalog.Agents.Count}; dialogues={NpcFactionCatalog.Dialogues.Count}; " +
            $"defeatTargets={NpcFactionCatalog.DefeatTargetIds.Count}; protectTargets={NpcFactionCatalog.ProtectTargetIds.Count}; " +
            "reputation=per-faction; dialogue=localized+conditional+consequences; " +
            "combat=physical-hostile; persistence=delta-state; proceduralCombatObjectives=enabled; F5=acceptance.");
        GD.Print(
            "TASK-104 coordinate HUD READY: source=Player.GlobalPosition/Ship.GlobalPosition; " +
            "axes=XYZ; precision=0.1; corner=top-right; " +
            "visibleInModes=Detailed/Compact/Hidden.");
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
        UpdateF4AcceptanceKeyLatch();
        PollInitializeTask();
        PollLoadTask();
        PollResetTask();
        PollAcceptanceTask();
        PollCatalogResourceLifecycleAcceptanceTask();
        PollContentAcceptanceTask();
        PollCraftingAcceptanceTask();
        PollCraftTimeAcceptanceTask();
        PollThirdCraftingAcceptanceTask();
        PollFourthCraftingAcceptanceTask();
        PollBaseConstructionAcceptanceTask();
        PollPlanetaryExplorationAcceptanceTask();
        PollShipSystemsAcceptanceTask();
        PollStageOneVoyageAcceptanceTask();
        PollGalaxyNavigationAcceptanceTask();
        PollEcologyAcceptanceTask();
        PollProceduralQuestAcceptanceTask();
        PollPlayerSurvivalAcceptanceTask();
        PollNpcFactionAcceptanceTask();
        UpdateNpcNavigationAcceptance(delta);
        PollCatalogMatrixAcceptanceTask();
        PollTechnologySelectorAcceptanceTask();
        PollStationServicesAcceptanceTask();
        PollChemicalProcessAcceptanceTask();
        PollProductionQueueAcceptanceTask();
        PollItemQualityDismantleAcceptanceTask();
        PollMultiStationIndustryAcceptanceTask();
        PollProductionNetworkHudAcceptanceTask();
        UpdateGameplayProductionQueue(delta);
        UpdateTimedCraft(delta);
        UpdateStageOneVoyage(delta);
        UpdateEcology(delta);
        UpdatePlayerSurvival(delta);
        _baseConstructionRuntime?.Tick(delta);
        UpdateBaseBuildPreview();
        PollAutosave();
        PollGracefulExitTask();
        UpdatePeriodicAutosave(delta);
        TryBeginGracefulExit();
        UpdateHud();
    }

    private void UpdateF4AcceptanceKeyLatch()
    {
        if (!_f4AcceptanceKeyLatched ||
            !_f4ReleaseSeen ||
            _planetaryExplorationAcceptanceTask is not null)
        {
            return;
        }

        ulong now = Time.GetTicksMsec();
        if (_f4LastSignalTicks == 0 ||
            now - _f4LastSignalTicks < F4ReleaseQuietMilliseconds)
        {
            return;
        }

        _f4AcceptanceKeyLatched = false;
        _f4ReleaseSeen = false;
        _f4LastSignalTicks = 0;
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey keyEvent)
        {
            return;
        }

        Key physical = keyEvent.PhysicalKeycode;
        Key logical = keyEvent.Keycode;
        bool isF4 = Matches(physical, logical, Key.F4);
        if (isF4)
        {
            _f4LastSignalTicks = Time.GetTicksMsec();
            _f4ReleaseSeen = !keyEvent.Pressed;
            if (!keyEvent.Pressed || keyEvent.Echo)
            {
                GetViewport().SetInputAsHandled();
                return;
            }

            if (_f4AcceptanceKeyLatched)
            {
                GetViewport().SetInputAsHandled();
                return;
            }

            _f4AcceptanceKeyLatched = true;
            _f4ReleaseSeen = false;
        }

        if (!keyEvent.Pressed)
        {
            return;
        }

        if (keyEvent.Echo)
        {
            return;
        }
        if (HandleNpcFactionInput(physical, logical))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (HandlePlayerSurvivalInput(physical, logical))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (HandleProceduralQuestInput(physical, logical))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (HandleEcologyInput(physical, logical))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_shipManagementOpen)
        {
            if (Matches(physical, logical, Key.Escape) ||
                Matches(physical, logical, Key.U))
            {
                CloseShipManagement("ship management closed");
            }
            else if (Matches(physical, logical, Key.Up))
            {
                MoveShipManagementSelection(-1);
            }
            else if (Matches(physical, logical, Key.Down))
            {
                MoveShipManagementSelection(1);
            }
            else if (Matches(physical, logical, Key.Tab))
            {
                CycleShipManagementTab();
            }
            else if (Matches(physical, logical, Key.X) &&
                     _shipManagementTab == ShipManagementTab.Modules)
            {
                UninstallSelectedShipModule();
            }
            else if (Matches(physical, logical, Key.D) &&
                     _shipManagementTab == ShipManagementTab.Systems)
            {
                DamageSelectedShipSystem();
            }
            else if (Matches(physical, logical, Key.R) &&
                     _shipManagementTab == ShipManagementTab.Systems)
            {
                RepairSelectedShipSystem();
            }
            else if (Matches(physical, logical, Key.Enter) ||
                     (Matches(physical, logical, Key.E) &&
                      Time.GetTicksMsec() > _shipManagementOpenedTicks + 120))
            {
                ConfirmShipManagementSelection();
            }

            GetViewport().SetInputAsHandled();
            return;
        }
        if (_baseBuildMode)
        {
            if (Matches(physical, logical, Key.Escape) ||
                Matches(physical, logical, Key.G))
            {
                CloseBaseBuildMode("base construction closed");
            }
            else if (Matches(physical, logical, Key.Up))
            {
                MoveBaseBuildSelection(-1);
            }
            else if (Matches(physical, logical, Key.Down))
            {
                MoveBaseBuildSelection(1);
            }
            else if (Matches(physical, logical, Key.R))
            {
                _baseBuildRotation = (_baseBuildRotation + 1) % 4;
                _baseBuildFeedback =
                    $"rotation={_baseBuildRotation * 90} degrees";
                UpdateBaseConstructionPanel();
            }
            else if (Matches(physical, logical, Key.Enter))
            {
                PlaceSelectedBaseModule();
            }
            else if (Matches(physical, logical, Key.X) ||
                     Matches(physical, logical, Key.Delete))
            {
                RemoveTargetBaseModule();
            }
            else if (Matches(physical, logical, Key.T))
            {
                ToggleTargetBaseModule();
            }

            GetViewport().SetInputAsHandled();
            return;
        }

        if (_discoveryCatalogOpen)
        {
            if (Matches(physical, logical, Key.Escape) ||
                Matches(physical, logical, Key.J))
            {
                CloseDiscoveryCatalog("discovery catalog closed");
            }
            else if (Matches(physical, logical, Key.Up))
            {
                MoveDiscoveryCatalogSelection(-1);
            }
            else if (Matches(physical, logical, Key.Down))
            {
                MoveDiscoveryCatalogSelection(1);
            }
            else if (Matches(physical, logical, Key.P))
            {
                PulsePlanetaryScanner();
                UpdateDiscoveryCatalogPanel();
            }
            else if (Matches(physical, logical, Key.N))
            {
                NameSelectedDiscovery();
            }

            GetViewport().SetInputAsHandled();
            return;
        }

        if (_stationServicesOpen)
        {
            if (_stationServicesOpenedFromVoyage &&
                Matches(physical, logical, Key.M))
            {
                CloseStationServices();
                OpenGalaxyMap();
            }
            else if (_stationServicesOpenedFromVoyage &&
                Matches(physical, logical, Key.T))
            {
                BeginStageOneUndock();
            }
            else if (Matches(physical, logical, Key.Escape))
            {
                CloseStationServices("station services closed");
            }
            else if (Matches(physical, logical, Key.Up))
            {
                MoveStationServices(-1);
            }
            else if (Matches(physical, logical, Key.Down))
            {
                MoveStationServices(1);
            }
            else if (Matches(physical, logical, Key.Tab))
            {
                CycleStationServicesTab();
            }
            else if (Matches(physical, logical, Key.B))
            {
                SetStationServicesTab(StationServicesTab.Buy);
            }
            else if (Matches(physical, logical, Key.S))
            {
                SetStationServicesTab(StationServicesTab.Sell);
            }
            else if (Matches(physical, logical, Key.Q))
            {
                SetStationServicesTab(StationServicesTab.Quests);
            }
            else if (Matches(physical, logical, Key.Enter) ||
                     (Matches(physical, logical, Key.E) &&
                      Time.GetTicksMsec() > _stationServicesOpenedTicks + 120))
            {
                ConfirmStationServicesSelection();
            }

            GetViewport().SetInputAsHandled();
            return;
        }

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
            else if (Matches(physical, logical, Key.Tab))
            {
                CycleSelectorMode();
            }
            else if (Matches(physical, logical, Key.R))
            {
                ToggleRecipesResearchMode();
            }
            else if (Matches(physical, logical, Key.Q))
            {
                if (_selectorMode == StationSelectorMode.Recipes)
                {
                    EnqueueSelectedRecipe();
                }
                else
                {
                    SetSelectorMode(StationSelectorMode.Queue);
                }
            }
            else if (Matches(physical, logical, Key.D))
            {
                SetSelectorMode(StationSelectorMode.Dismantle);
            }
            else if (_selectorMode == StationSelectorMode.Queue &&
                     (Matches(physical, logical, Key.C) ||
                      Matches(physical, logical, Key.Delete)))
            {
                CancelSelectedQueueJob();
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

        if (HandleGalaxyNavigationInput(physical, logical))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (HandleStageOneVoyageInput(physical, logical))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (Matches(physical, logical, Key.U) &&
            (_state == SalvageRepairSliceState.Ready ||
             _state == SalvageRepairSliceState.Passed))
        {
            OpenShipManagement();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (Matches(physical, logical, Key.J) &&
            (_state == SalvageRepairSliceState.Ready ||
             _state == SalvageRepairSliceState.Passed))
        {
            OpenDiscoveryCatalog();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (Matches(physical, logical, Key.P) &&
            (_state == SalvageRepairSliceState.Ready ||
             _state == SalvageRepairSliceState.Passed))
        {
            PulsePlanetaryScanner();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (Matches(physical, logical, Key.G) &&
            (_state == SalvageRepairSliceState.Ready ||
             _state == SalvageRepairSliceState.Passed))
        {
            OpenBaseBuildMode();
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
        else if (isF4 && CanStartCommand())
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

    public void OpenStationServices(
        StationServicesNpc npc,
        Node3D interactor)
    {
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(interactor);
        if (_state != SalvageRepairSliceState.Ready &&
            _state != SalvageRepairSliceState.Passed)
        {
            _status = "wait until the current persistence operation completes";
            return;
        }

        if (!string.Equals(
            npc.NpcId,
            StationServices.NpcId,
            StringComparison.Ordinal))
        {
            _status = $"unknown station services NPC {npc.NpcId}";
            return;
        }

        CloseRecipeSelector();
        CloseBaseBuildMode();
        CloseDiscoveryCatalog();
        CloseShipManagement();
        CloseMissionJournal();
        _stationServicesOpen = true;
        _stationServicesTab = StationServicesTab.Dialogue;
        _stationServicesIndex = 0;
        _stationServicesFeedback = "";
        _stationServicesOpenedTicks = Time.GetTicksMsec();
        if (_stationServicesPanel is not null)
        {
            _stationServicesPanel.Visible = true;
        }

        UpdateStationServicesPanel();
        _status = $"station services opened: {npc.Name}";
        _lastDomainEvent = $"NpcInteraction({npc.NpcId})";
        RecordProceduralQuestReturnAtCurrentNpc();
        GD.Print(
            "TASK-102 player NPC interaction PASS: " +
            $"npc={npc.NpcId}; market={StationServices.MarketId}; " +
            $"credits={StationServices.PlayerCredits}; " +
            $"quests={StationServices.CompletedQuestCount}/" +
            $"{StationServiceCatalog.Quests.Count}; interactor={interactor.Name}.");
    }

    private void CloseStationServices(string status = "")
    {
        _stationServicesOpen = false;
        _stationServicesIndex = 0;
        _stationServicesFeedback = "";
        if (_stationServicesPanel is not null)
        {
            _stationServicesPanel.Visible = false;
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            _status = status;
        }
    }

    private void MoveStationServices(int delta)
    {
        int count = GetStationServicesItemCount();
        if (count <= 0)
        {
            _stationServicesIndex = 0;
            return;
        }

        _stationServicesIndex = (_stationServicesIndex + delta) % count;
        if (_stationServicesIndex < 0)
        {
            _stationServicesIndex += count;
        }

        _stationServicesFeedback = "";
        UpdateStationServicesPanel();
    }

    private void CycleStationServicesTab()
    {
        StationServicesTab next = (StationServicesTab)(
            ((int)_stationServicesTab + 1) %
            Enum.GetValues<StationServicesTab>().Length);
        SetStationServicesTab(next);
    }

    private void SetStationServicesTab(StationServicesTab tab)
    {
        _stationServicesTab = tab;
        _stationServicesIndex = 0;
        _stationServicesFeedback = "";
        UpdateStationServicesPanel();
    }

    private int GetStationServicesItemCount()
    {
        return _stationServicesTab switch
        {
            StationServicesTab.Dialogue => StationServiceCatalog.GetDialogue(
                StationServiceCatalog.GetNpc(
                    StationServices.NpcId).DialogueId).Options.Count,
            StationServicesTab.Buy => StationServices.GetBuyOffers().Count,
            StationServicesTab.Sell => StationServices.GetSellOffers(Session).Count,
            StationServicesTab.Quests => StationServices.Quests.Count,
            _ => 0
        };
    }

    private void ConfirmStationServicesSelection()
    {
        switch (_stationServicesTab)
        {
            case StationServicesTab.Dialogue:
                ExecuteSelectedDialogueAction();
                break;
            case StationServicesTab.Buy:
                ExecuteSelectedTrade(isBuy: true);
                break;
            case StationServicesTab.Sell:
                ExecuteSelectedTrade(isBuy: false);
                break;
            case StationServicesTab.Quests:
                ExecuteSelectedQuestAction();
                break;
        }

        UpdateStationServicesPanel();
    }

    private void ExecuteSelectedDialogueAction()
    {
        NpcServiceDefinition npc = StationServiceCatalog.GetNpc(
            StationServices.NpcId);
        DialogueServiceDefinition dialogue = StationServiceCatalog.GetDialogue(
            npc.DialogueId);
        if (dialogue.Options.Count == 0)
        {
            _stationServicesFeedback = dialogue.Greeting;
            return;
        }

        _stationServicesIndex = Math.Clamp(
            _stationServicesIndex,
            0,
            dialogue.Options.Count - 1);
        DialogueOptionServiceDefinition option =
            dialogue.Options[_stationServicesIndex];
        if (StationServices.Reputation < option.MinimumReputation)
        {
            _stationServicesFeedback =
                $"requires reputation {option.MinimumReputation}";
            return;
        }

        if (option.ReputationDelta != 0)
        {
            StationServices.ApplyReputationDelta(option.ReputationDelta);
            QueueCurrentSnapshot(AutosaveTrigger.BaseChanged);
        }

        _lastDomainEvent = $"DialogueOption({option.OptionId})";
        switch (option.Action)
        {
            case "OpenTrade":
                SetStationServicesTab(StationServicesTab.Buy);
                _stationServicesFeedback = option.Text;
                break;
            case "OpenQuests":
                SetStationServicesTab(StationServicesTab.Quests);
                _stationServicesFeedback = option.Text;
                break;
            case "Close":
                CloseStationServices(dialogue.Farewell);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported dialogue action {option.Action}.");
        }
    }

    private void ExecuteSelectedTrade(bool isBuy)
    {
        EnsureGameplayProductionNetwork();
        StationServices.RefreshEconomy(
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        MarketPriceQuote[] offers = (isBuy
                ? StationServices.GetBuyOffers()
                : StationServices.GetSellOffers(Session))
            .ToArray();
        if (offers.Length == 0)
        {
            _stationServicesFeedback = isBuy
                ? "market has no available stock"
                : "player inventory has no tradable items";
            return;
        }

        _stationServicesIndex = Math.Clamp(
            _stationServicesIndex,
            0,
            offers.Length - 1);
        MarketPriceQuote offer = offers[_stationServicesIndex];
        if (isBuy)
        {
            try
            {
                foreach (ProductionQueueRuntime queue in GameplayNetwork.Queues)
                {
                    _ = checked(queue.GetQuantity(offer.DefinitionId) + 1);
                }
            }
            catch (OverflowException)
            {
                _stationServicesFeedback =
                    $"inventory mirror capacity exceeded for {offer.DefinitionId}";
                _lastDomainEvent = "TradeBuyBlocked";
                return;
            }
        }
        else
        {
            ProductionQueueRuntime? missingMirror = GameplayNetwork.Queues
                .FirstOrDefault(queue =>
                    queue.GetQuantity(offer.DefinitionId) < 1);
            if (missingMirror is not null)
            {
                _stationServicesFeedback =
                    $"inventory mirror {missingMirror.StationId} is missing " +
                    offer.DefinitionId;
                _lastDomainEvent = "TradeSellBlocked";
                return;
            }
        }

        StationServiceTradeResult trade = isBuy
            ? StationServices.TryBuy(offer.DefinitionId, 1, Session)
            : StationServices.TrySell(offer.DefinitionId, 1, Session);
        _stationServicesFeedback = trade.Result;
        if (!trade.Succeeded)
        {
            _lastDomainEvent = isBuy ? "TradeBuyBlocked" : "TradeSellBlocked";
            return;
        }

        if (isBuy)
        {
            GameplayNetwork.AddInventoryAll(offer.DefinitionId, 1);
        }
        else if (!GameplayNetwork.TryConsumeInventoryAll(
            offer.DefinitionId,
            1,
            out string mirrorResult))
        {
            throw new InvalidOperationException(
                $"Trade inventory mirror desynchronized: {mirrorResult}.");
        }

        _lastDomainEvent = isBuy
            ? $"TradeBought({offer.DefinitionId})"
            : $"TradeSold({offer.DefinitionId})";
        RecordProceduralQuestObjective(
            ProceduralQuestObjectiveType.TradeItem,
            offer.DefinitionId,
            1,
            queueAutosave: false);
        QueueCurrentSnapshot(AutosaveTrigger.BaseChanged);
        GD.Print(
            $"TASK-102 player trade {(isBuy ? "buy" : "sell")} PASS: " +
            $"definition={offer.DefinitionId}; quantity=1; " +
            $"total={trade.TotalCredits}; credits={trade.PlayerCredits}; " +
            $"merchantCredits={trade.MerchantCredits}; " +
            $"stock={StationServices.Quote(offer.DefinitionId).Stock}; " +
            "inventoryMirrors=1.");
        int count = GetStationServicesItemCount();
        _stationServicesIndex = count == 0
            ? 0
            : Math.Min(_stationServicesIndex, count - 1);
    }

    private void ExecuteSelectedQuestAction()
    {
        StationServiceQuestView[] quests = StationServices.Quests.ToArray();
        if (quests.Length == 0)
        {
            _stationServicesFeedback = "no quests available";
            return;
        }

        _stationServicesIndex = Math.Clamp(
            _stationServicesIndex,
            0,
            quests.Length - 1);
        StationServiceQuestView quest = quests[_stationServicesIndex];
        bool changed;
        AutosaveTrigger trigger;
        if (quest.Status == StationServiceQuestStatus.Offered)
        {
            changed = StationServices.TryAcceptQuest(
                quest.Definition.QuestId,
                out _stationServicesFeedback);
            trigger = AutosaveTrigger.BaseChanged;
        }
        else if (quest.Status == StationServiceQuestStatus.ReadyToClaim)
        {
            changed = StationServices.TryClaimQuest(
                quest.Definition.QuestId,
                out _stationServicesFeedback);
            trigger = AutosaveTrigger.QuestCompleted;
        }
        else
        {
            changed = false;
            trigger = AutosaveTrigger.BaseChanged;
            _stationServicesFeedback = quest.Status ==
                StationServiceQuestStatus.Completed
                ? "quest already completed"
                : $"quest progress {quest.ClampedProgress}/" +
                  quest.CurrentNode.RequiredQuantity;
        }

        if (!changed)
        {
            return;
        }

        _lastDomainEvent = quest.Status == StationServiceQuestStatus.Offered
            ? $"QuestAccepted({quest.Definition.QuestId})"
            : $"QuestCompleted({quest.Definition.QuestId})";
        QueueCurrentSnapshot(trigger);
        GD.Print(
            "TASK-102 player quest action PASS: " +
            $"quest={quest.Definition.QuestId}; action=" +
            $"{(trigger == AutosaveTrigger.QuestCompleted ? "claim" : "accept")}; " +
            $"credits={StationServices.PlayerCredits}; " +
            $"reputation={StationServices.Reputation}; " +
            $"completed={StationServices.CompletedQuestCount}/" +
            $"{StationServiceCatalog.Quests.Count}.");
    }

    private void UpdateStationServicesPanel()
    {
        if (!_stationServicesOpen || _stationServicesLabel is null)
        {
            return;
        }

        long economyDays = StationServices.RefreshEconomy(
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if (economyDays > 0)
        {
            QueueCurrentSnapshot(AutosaveTrigger.BaseChanged);
        }

        NpcServiceDefinition npc = StationServiceCatalog.GetNpc(
            StationServices.NpcId);
        DialogueServiceDefinition dialogue = StationServiceCatalog.GetDialogue(
            npc.DialogueId);
        List<string> lines = new()
        {
            "STATION SERVICES — FRONTIER EXCHANGE",
            $"NPC: {npc.NpcId} • type={npc.NpcType} • faction={npc.FactionId}",
            $"Wallet: {StationServices.PlayerCredits} credits • " +
            $"Reputation: {StationServices.Reputation} • " +
            $"Completed quests: {StationServices.CompletedQuestCount}/" +
            $"{StationServiceCatalog.Quests.Count}",
            "Tabs: " + string.Join(" | ", Enum.GetValues<StationServicesTab>()
                .Select(tab => tab == _stationServicesTab
                    ? $"[{tab}]"
                    : tab.ToString())),
            ""
        };

        if (_stationServicesTab == StationServicesTab.Dialogue)
        {
            lines.Add(dialogue.Greeting);
            lines.Add("");
            _stationServicesIndex = dialogue.Options.Count == 0
                ? 0
                : Math.Clamp(
                    _stationServicesIndex,
                    0,
                    dialogue.Options.Count - 1);
            for (int index = 0; index < dialogue.Options.Count; index++)
            {
                DialogueOptionServiceDefinition option = dialogue.Options[index];
                string cursor = index == _stationServicesIndex ? ">" : " ";
                lines.Add(
                    $"{cursor} {option.Text} [{option.Action}] " +
                    $"minRep={option.MinimumReputation}");
            }

            lines.Add("");
            lines.Add("Price = BasePrice × system × supply/demand × faction × " +
                "reputation × deterministic daily modifier.");
            lines.Add($"Market coverage: {StationServices.TradableItemCount}/" +
                $"{ContentCatalog.Items.Count} catalog items • " +
                $"economy day={StationServices.DayIndex}.");
        }
        else if (_stationServicesTab is StationServicesTab.Buy or
                 StationServicesTab.Sell)
        {
            MarketPriceQuote[] offers = (_stationServicesTab ==
                    StationServicesTab.Buy
                    ? StationServices.GetBuyOffers()
                    : StationServices.GetSellOffers(Session))
                .ToArray();
            if (offers.Length == 0)
            {
                lines.Add(_stationServicesTab == StationServicesTab.Buy
                    ? "No market stock."
                    : "No tradable player inventory.");
            }
            else
            {
                _stationServicesIndex = Math.Clamp(
                    _stationServicesIndex,
                    0,
                    offers.Length - 1);
                int first = Math.Max(0, _stationServicesIndex - 5);
                int last = Math.Min(offers.Length, first + 11);
                first = Math.Max(0, last - 11);
                for (int index = first; index < last; index++)
                {
                    MarketPriceQuote quote = offers[index];
                    string cursor = index == _stationServicesIndex ? ">" : " ";
                    int playerQuantity = Session.GetAvailableQuantity(
                        quote.DefinitionId);
                    lines.Add(
                        $"{cursor} {GetShortContentId(quote.DefinitionId),-34} " +
                        $"buy={quote.BuyPrice,5} sell={quote.SellPrice,5} " +
                        $"stock={quote.Stock,3} inv={playerQuantity,3}");
                }

                MarketPriceQuote selected = offers[_stationServicesIndex];
                lines.Add("");
                lines.Add(
                    $"Selected factors: base={selected.BasePrice:0.##} • " +
                    $"system={selected.SystemEconomyModifier:0.###} • " +
                    $"supply={selected.SupplyDemandModifier:0.###} • " +
                    $"faction={selected.FactionModifier:0.###} • " +
                    $"reputation={selected.ReputationModifier:0.###} • " +
                    $"daily={selected.RandomDailyModifier:0.###}");
            }
        }
        else
        {
            StationServiceQuestView[] quests = StationServices.Quests.ToArray();
            _stationServicesIndex = quests.Length == 0
                ? 0
                : Math.Clamp(_stationServicesIndex, 0, quests.Length - 1);
            for (int index = 0; index < quests.Length; index++)
            {
                StationServiceQuestView quest = quests[index];
                string cursor = index == _stationServicesIndex ? ">" : " ";
                lines.Add(
                    $"{cursor} {GetShortContentId(quest.Definition.QuestId)} " +
                    $"[{quest.Status}] {quest.ClampedProgress}/" +
                    $"{quest.CurrentNode.RequiredQuantity}");
                lines.Add(
                    $"    {quest.CurrentNode.ObjectiveType}: " +
                    $"{quest.CurrentNode.TargetDefinitionId} • reward=" +
                    $"{quest.Definition.RewardCredits}cr +" +
                    $"{quest.Definition.ReputationReward} rep");
            }
        }

        if (!string.IsNullOrWhiteSpace(_stationServicesFeedback))
        {
            lines.Add("");
            lines.Add($"Result: {_stationServicesFeedback}");
        }

        lines.Add("");
        lines.Add("Up/Down - select • Tab - next tab • B - Buy • S - Sell • " +
            "Q - Quests • Enter/E - action • Esc - close");
        _stationServicesLabel.Text = string.Join("\n", lines);
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

        EnsureGameplayProductionNetwork();
        IReadOnlyList<CraftingRecipeDefinition> recipes =
            GetSelectorRecipes(station.StationId);
        if (recipes.Count == 0)
        {
            _status = $"station {station.StationId} has no runtime recipes";
            return;
        }

        CloseStationServices();
        CloseBaseBuildMode();
        CloseDiscoveryCatalog();
        CloseShipManagement();
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

        int count = GetSelectorItemCount(station.StationId);
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

    private int GetSelectorItemCount(string stationId)
    {
        return _selectorMode switch
        {
            StationSelectorMode.Recipes =>
                GetSelectorRecipes(stationId).Count,
            StationSelectorMode.Research =>
                GetSelectorTechnologies(stationId).Count,
            StationSelectorMode.Queue =>
                _gameplayProductionNetwork is null
                    ? 0
                    : GetGameplayQueue(stationId).Jobs.Count,
            StationSelectorMode.Dismantle =>
                GetDismantleRecipes(stationId).Count,
            _ => 0
        };
    }

    private void CycleSelectorMode()
    {
        StationSelectorMode next = _selectorMode switch
        {
            StationSelectorMode.Recipes => StationSelectorMode.Research,
            StationSelectorMode.Research => StationSelectorMode.Queue,
            StationSelectorMode.Queue => StationSelectorMode.Dismantle,
            _ => StationSelectorMode.Recipes
        };
        SetSelectorMode(next);
    }

    private void ToggleRecipesResearchMode()
    {
        SetSelectorMode(
            _selectorMode == StationSelectorMode.Research
                ? StationSelectorMode.Recipes
                : StationSelectorMode.Research);
    }

    private void SetSelectorMode(StationSelectorMode mode)
    {
        if (_selectorStation is null)
        {
            return;
        }

        _selectorMode = mode;
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

        if (_selectorMode == StationSelectorMode.Queue)
        {
            ToggleSelectedQueueJobPause();
            return;
        }

        if (_selectorMode == StationSelectorMode.Dismantle)
        {
            DismantleSelectedItem();
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

        if (IndustryRecipePolicy.IsRepeatable(entry.Recipe))
        {
            EnqueueSelectedRecipe();
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

    private IReadOnlyList<CraftingRecipeDefinition> GetDismantleRecipes(
        string stationId)
    {
        return GetSelectorRecipes(stationId)
            .Where(recipe =>
                recipe.Outputs.Count == 1 &&
                recipe.DismantleReturns.Count > 0 &&
                Session.GetCraftedQuantity(recipe.Outputs[0].DefinitionId) > 0)
            .OrderBy(recipe => recipe.RecipeId, StringComparer.Ordinal)
            .ToArray();
    }

    private void DismantleSelectedItem()
    {
        PortableCraftingStation? station = _selectorStation;
        if (station is null)
        {
            return;
        }

        IReadOnlyList<CraftingRecipeDefinition> recipes =
            GetDismantleRecipes(station.StationId);
        if (recipes.Count == 0)
        {
            _selectorFeedback = "No dismantlable crafted items.";
            UpdateRecipeSelector();
            return;
        }

        CraftingRecipeDefinition recipe =
            recipes[Math.Clamp(_selectorIndex, 0, recipes.Count - 1)];
        CraftingStackDefinition output = recipe.Outputs.Single();
        IndustryItemProperties properties =
            Session.GetItemProperties(output.DefinitionId);
        DismantleExecutionReport report =
            ItemPropertyRuntime.Dismantle(recipe, properties);
        if (!report.Succeeded)
        {
            _selectorFeedback = report.Result;
            UpdateRecipeSelector();
            return;
        }

        if (!Session.TryConsumeInventory(
            output.DefinitionId,
            1,
            out string consumeResult))
        {
            _selectorFeedback = consumeResult;
            UpdateRecipeSelector();
            return;
        }

        if (!GameplayNetwork.TryConsumeInventoryAll(
            output.DefinitionId,
            1,
            out string queueConsumeResult))
        {
            Session.GrantInventory(output.DefinitionId, 1, properties);
            _selectorFeedback = queueConsumeResult;
            UpdateRecipeSelector();
            return;
        }

        IndustryItemProperties recoveredProperties =
            ItemPropertyRuntime.CreateRecoveredProperties(properties);
        foreach (CraftingStackDefinition returned in report.Returns)
        {
            Session.GrantInventory(
                returned.DefinitionId,
                returned.Quantity,
                recoveredProperties);
            GameplayNetwork.AddInventoryAll(
                returned.DefinitionId,
                returned.Quantity);
        }

        _lastDomainEvent = $"ItemDismantled({output.DefinitionId})";
        _selectorFeedback = report.Result;
        _status = report.Result;
        ApplySessionToScene();
        ApplyGameplayProductionNetworkStationState();
        QueueCurrentSnapshot(AutosaveTrigger.BaseChanged);
        _selectorIndex = Math.Clamp(
            _selectorIndex,
            0,
            Math.Max(0, GetDismantleRecipes(station.StationId).Count - 1));
        GD.Print(
            "TASK-093 player dismantle PASS: " +
            $"recipe={recipe.RecipeId}; source={output.DefinitionId}; " +
            $"quality={properties.Quality}; purity={properties.Purity}; " +
            $"stability={properties.Stability}; " +
            $"efficiency={report.RecoveryEfficiency.ToString("0.###", CultureInfo.InvariantCulture)}; " +
            $"returns={report.Returns.Sum(item => item.Quantity)}; " +
            $"autosaveTrigger={AutosaveTrigger.BaseChanged}.");
        UpdateRecipeSelector();
    }

    private void EnqueueSelectedRecipe()
    {
        PortableCraftingStation? station = _selectorStation;
        if (station is null)
        {
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
        CraftingRecipeDefinition recipe = entry.Recipe;
        if (entry.Crafted)
        {
            _selectorFeedback =
                $"Recipe {recipe.RecipeId} is already completed.";
            UpdateRecipeSelector();
            return;
        }

        if (!entry.TechnologyUnlocked)
        {
            _selectorFeedback =
                $"LOCKED: research {recipe.RequiredTechnology}.";
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

        ProductionQueueRuntime queue = GetGameplayQueue(station.StationId);
        if (!IndustryRecipePolicy.IsRepeatable(recipe) &&
            queue.Jobs.Any(job => string.Equals(
                job.RecipeId,
                recipe.RecipeId,
                StringComparison.Ordinal)))
        {
            _selectorFeedback =
                $"Recipe {recipe.RecipeId} is already in the queue.";
            UpdateRecipeSelector();
            return;
        }

        ProductionQueueCommandReport report = queue.Enqueue(
            recipe.RecipeId,
            CreateNominalEnvironment(recipe),
            requestedBatches: 1);
        if (report.Result != ProductionQueueCommandResult.Enqueued)
        {
            _selectorFeedback = report.ResultText;
            _status = report.ResultText;
            UpdateRecipeSelector();
            return;
        }

        try
        {
            IReadOnlyList<CraftingStackDefinition> reservedStacks =
                recipe.Inputs
                    .Concat(recipe.Catalysts.Select(catalyst =>
                        new CraftingStackDefinition(
                            catalyst.DefinitionId,
                            catalyst.Quantity)))
                    .ToArray();
            EnsureNetworkMirrorCanConsume(
                reservedStacks,
                station.StationId);
            foreach (CraftingStackDefinition input in recipe.Inputs)
            {
                if (!Session.TryConsumeInventory(
                    input.DefinitionId,
                    input.Quantity,
                    out string consumeResult))
                {
                    throw new InvalidOperationException(consumeResult);
                }
            }

            foreach (CatalystStackDefinition catalyst in recipe.Catalysts)
            {
                if (!Session.TryConsumeInventory(
                    catalyst.DefinitionId,
                    catalyst.Quantity,
                    out string consumeResult))
                {
                    throw new InvalidOperationException(consumeResult);
                }
            }

            MirrorSessionConsumptionToGameplayNetwork(
                reservedStacks,
                station.StationId);
        }
        catch
        {
            queue.Cancel(report.JobId);
            throw;
        }

        _lastDomainEvent = $"ProductionJobEnqueued({report.JobId})";
        _selectorFeedback = report.ResultText;
        _status = report.ResultText;
        _selectorMode = StationSelectorMode.Queue;
        _selectorIndex = Math.Max(0, queue.Jobs.Count - 1);
        ApplyGameplayProductionNetworkStationState();
        QueueCurrentSnapshot(AutosaveTrigger.BaseChanged);
        GD.Print(
            "TASK-092 player queue enqueue PASS: " +
            $"station={station.StationId}; job={report.JobId}; " +
            $"recipe={recipe.RecipeId}; " +
            $"energyReserved={recipe.EnergyCost.ToString("0.###", CultureInfo.InvariantCulture)}; " +
            $"energyRemaining={queue.EnergyRemaining.ToString("0.###", CultureInfo.InvariantCulture)}; " +
            $"inputsReserved={recipe.Inputs.Sum(input => input.Quantity)}; " +
            $"status={queue.Jobs.Single(job => job.JobId == report.JobId).Status}." );
        UpdateRecipeSelector();
    }

    private void ToggleSelectedQueueJobPause()
    {
        ProductionQueueJobView? job = GetSelectedQueueJob();
        if (job is null)
        {
            _selectorFeedback = "Queue is empty.";
            UpdateRecipeSelector();
            return;
        }

        ProductionQueueRuntime queue = SelectorQueue;
        ProductionQueueCommandReport report = job.Status switch
        {
            ProductionQueueJobStatus.Running => queue.Pause(job.JobId),
            ProductionQueueJobStatus.Paused => queue.Resume(job.JobId),
            _ => new ProductionQueueCommandReport(
                ProductionQueueCommandResult.InvalidState,
                $"job {job.JobId} is waiting for a free slot",
                job.JobId,
                IndustryProcessResult.Ready,
                Array.Empty<CraftingStackDefinition>(),
                Array.Empty<CraftingStackDefinition>(),
                0.0)
        };
        _selectorFeedback = report.ResultText;
        _status = report.ResultText;
        if (report.Result is ProductionQueueCommandResult.Paused or
            ProductionQueueCommandResult.Resumed)
        {
            _lastDomainEvent =
                $"ProductionJob{report.Result}({report.JobId})";
            ApplyGameplayProductionNetworkStationState();
            QueueCurrentSnapshot(AutosaveTrigger.BaseChanged);
            GD.Print(
                "TASK-092 player queue control PASS: " +
                $"station={queue.StationId}; action={report.Result}; " +
                $"job={report.JobId}; running={queue.RunningCount}; " +
                $"queued={queue.QueuedCount}; paused={queue.PausedCount}.");
        }

        UpdateRecipeSelector();
    }

    private void CancelSelectedQueueJob()
    {
        ProductionQueueJobView? job = GetSelectedQueueJob();
        if (job is null)
        {
            _selectorFeedback = "Queue is empty.";
            UpdateRecipeSelector();
            return;
        }

        ProductionQueueRuntime queue = SelectorQueue;
        ProductionQueueCommandReport report = queue.Cancel(job.JobId);
        if (report.Result == ProductionQueueCommandResult.Cancelled)
        {
            foreach (CraftingStackDefinition input in report.RefundedInputs)
            {
                Session.GrantInventory(input.DefinitionId, input.Quantity);
                GameplayNetwork.AddInventoryAllExcept(
                    queue.StationId,
                    input.DefinitionId,
                    input.Quantity);
            }

            foreach (CraftingStackDefinition catalyst in report.RefundedCatalysts)
            {
                Session.GrantInventory(catalyst.DefinitionId, catalyst.Quantity);
                GameplayNetwork.AddInventoryAllExcept(
                    queue.StationId,
                    catalyst.DefinitionId,
                    catalyst.Quantity);
            }

            _lastDomainEvent = $"ProductionJobCancelled({report.JobId})";
            ApplyGameplayProductionNetworkStationState();
            QueueCurrentSnapshot(AutosaveTrigger.BaseChanged);
            _selectorIndex = Math.Clamp(
                _selectorIndex,
                0,
                Math.Max(0, queue.Jobs.Count - 1));
            GD.Print(
                "TASK-092 player queue cancellation PASS: " +
                $"station={queue.StationId}; job={report.JobId}; " +
                $"inputsRefunded={report.RefundedInputs.Sum(input => input.Quantity)}; " +
                $"catalystsRefunded={report.RefundedCatalysts.Sum(catalyst => catalyst.Quantity)}; " +
                $"energyRefunded={report.RefundedEnergy.ToString("0.###", CultureInfo.InvariantCulture)}; " +
                $"energyRemaining={queue.EnergyRemaining.ToString("0.###", CultureInfo.InvariantCulture)}.");
        }

        _selectorFeedback = report.ResultText;
        _status = report.ResultText;
        UpdateRecipeSelector();
    }

    private ProductionQueueJobView? GetSelectedQueueJob()
    {
        IReadOnlyList<ProductionQueueJobView> jobs = SelectorQueue.Jobs;
        return jobs.Count == 0
            ? null
            : jobs[Math.Clamp(_selectorIndex, 0, jobs.Count - 1)];
    }

    private static IndustryProcessEnvironment CreateNominalEnvironment(
        CraftingRecipeDefinition recipe)
    {
        RecipeEnvironmentDefinition environment = recipe.Environment;
        double temperature =
            (environment.MinimumTemperatureKelvin +
             environment.MaximumTemperatureKelvin) / 2.0;
        double pressure = environment.RequiresVacuum
            ? environment.MinimumPressureKPa
            : (environment.MinimumPressureKPa +
               environment.MaximumPressureKPa) / 2.0;
        return new IndustryProcessEnvironment(
            temperature,
            pressure,
            environment.RequiresVacuum);
    }

    private void UpdateRecipeSelector()
    {
        PortableCraftingStation? station = _selectorStation;
        if (_recipeSelectorLabel is null || station is null)
        {
            return;
        }

        string stationId = station.StationId;
        ProductionQueueRuntime queue = GetGameplayQueue(stationId);
        ProductionQueueTerminalSnapshot queueSnapshot =
            ProductionQueueTerminalModel.Build(queue);
        List<string> lines = new()
        {
            $"INDUSTRY TERMINAL - {station.Name}",
            $"Station: {stationId}",
            $"Mode: {_selectorMode} | RP: {TechnologyProgress.ResearchPoints} | " +
            $"Unlocked: {TechnologyProgress.UnlockedCount}/{ContentCatalog.Technologies.Count}",
            $"Energy: {queueSnapshot.EnergyRemaining.ToString("0.###", CultureInfo.InvariantCulture)}/" +
            $"{queueSnapshot.EnergyCapacity.ToString("0.###", CultureInfo.InvariantCulture)} | " +
            $"Slots: {queueSnapshot.RunningJobs}/{queueSnapshot.ParallelSlots} | " +
            $"Waiting: {queueSnapshot.QueuedJobs} | Paused: {queueSnapshot.PausedJobs}",
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
                ProductionQueueJobView? queuedJob = queue.Jobs
                    .FirstOrDefault(job => string.Equals(
                        job.RecipeId,
                        recipe.RecipeId,
                        StringComparison.Ordinal));
                string status = queuedJob is not null
                    ? queuedJob.Status.ToString().ToUpperInvariant()
                    : entry.Crafted
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
                    $"T{recipe.TechnologyTier} {recipe.CraftTimeSeconds:0.##}s " +
                    $"E{recipe.EnergyCost:0.###}");
                lines.Add($"    {inputs} -> {outputs}");
            }
        }
        else if (_selectorMode == StationSelectorMode.Research)
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
        else if (_selectorMode == StationSelectorMode.Queue)
        {
            lines.Add(
                $"Queue jobs: {queueSnapshot.Jobs.Count} | " +
                "freeze-and-resume persistence | offline progress: 0");
            lines.Add("");
            if (queueSnapshot.Jobs.Count == 0)
            {
                lines.Add("  Queue is empty. Select a recipe and press Q to enqueue.");
            }
            else
            {
                for (int index = 0; index < queueSnapshot.Jobs.Count; index++)
                {
                    ProductionQueueTerminalJobRow row =
                        queueSnapshot.Jobs[index];
                    string cursor = index == _selectorIndex ? ">" : " ";
                    lines.Add(
                        $"{cursor} [{row.Status.ToString().ToUpperInvariant()}] " +
                        $"{GetShortContentId(row.RecipeId)} {row.ProgressBar} " +
                        $"{row.TimingText} {row.SlotText}");
                    lines.Add(
                        $"    reserve E{row.ReservedEnergy.ToString("0.###", CultureInfo.InvariantCulture)}; " +
                        row.ReservationText);
                }
            }
        }
        else
        {
            IReadOnlyList<CraftingRecipeDefinition> recipes =
                GetDismantleRecipes(stationId);
            lines.Add(
                $"Dismantlable items: {recipes.Count} | returns are scaled by " +
                "quality/purity/stability");
            lines.Add("");
            if (recipes.Count == 0)
            {
                lines.Add("  No crafted items with dismantle returns.");
            }
            else
            {
                for (int index = 0; index < recipes.Count; index++)
                {
                    CraftingRecipeDefinition recipe = recipes[index];
                    CraftingStackDefinition output = recipe.Outputs.Single();
                    IndustryItemProperties properties =
                        Session.GetItemProperties(output.DefinitionId);
                    DismantleExecutionReport preview =
                        ItemPropertyRuntime.Dismantle(recipe, properties);
                    string cursor = index == _selectorIndex ? ">" : " ";
                    string returns = preview.Returns.Count == 0
                        ? "no recoverable material"
                        : string.Join(" + ", preview.Returns.Select(item =>
                            $"{item.Quantity} {GetShortContentId(item.DefinitionId)}"));
                    lines.Add(
                        $"{cursor} {GetShortContentId(output.DefinitionId)} x" +
                        $"{Session.GetCraftedQuantity(output.DefinitionId)} " +
                        $"Q{properties.Quality}/P{properties.Purity}/S{properties.Stability}");
                    lines.Add(
                        $"    recovery {preview.RecoveryEfficiency * 100.0:0}% -> {returns}");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(_selectorFeedback))
        {
            lines.Add("");
            lines.Add($"Result: {_selectorFeedback}");
        }

        lines.Add("");
        lines.Add(_selectorMode switch
        {
            StationSelectorMode.Recipes =>
                "Up/Down - select | Enter/E - craft now | Q - enqueue | " +
                "Tab - next tab | R - Research | Esc - close",
            StationSelectorMode.Research =>
                "Up/Down - select | Enter/E - unlock | Q - Queue | " +
                "Tab - next tab | R - Recipes | D - Dismantle | Esc - close",
            StationSelectorMode.Queue =>
                "Up/Down - select | Enter/E - pause/resume | C/Delete - cancel | " +
                "D - Dismantle | Tab - next tab | R - Research | Esc - close",
            _ =>
                "Up/Down - select | Enter/E - dismantle | Tab - next tab | " +
                "R - Research | Q - Queue | Esc - close"
        });
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

        EnsureGameplayProductionNetwork();
        GameplayNetwork.AddInventoryAll(definitionId, quantity);
        int questUpdates = StationServices.RecordObjective(
            StationServiceObjectiveType.CollectResource,
            definitionId,
            quantity);
        int proceduralQuestUpdates = RecordProceduralQuestObjective(
            ProceduralQuestObjectiveType.CollectResource,
            definitionId,
            quantity);
        RecordPlayerMultitoolUse(PlayerMultitoolFunction.Mining, definitionId);
        _lastDomainEvent =
            $"ResourceCollected({resourceNodeId}, definition={definitionId}, " +
            $"quantity={quantity})";
        _status = result;
        source.SetCollected(true);
        RefreshNpcNavigationObstacles();
        GD.Print(
            $"Vertical slice domain event: {_lastDomainEvent}; " +
            $"available={Session.GetAvailableQuantity(definitionId)}; " +
            $"questUpdates={questUpdates}; proceduralQuestUpdates={proceduralQuestUpdates}; " +
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
            if (!ShipSystems.Commissioned)
            {
                ShipSystems.Commission(out _);
                ConfigureVoyageShipFromDerivedStats();
                QueueCurrentSnapshot(AutosaveTrigger.ShipChanged);
            }

            TryBoardStageOneVoyage(interactor);
            return;
        }

        if (!ShipSystems.Commission(out string commissioningResult) &&
            !ShipSystems.Commissioned)
        {
            throw new InvalidOperationException(commissioningResult);
        }

        ConfigureVoyageShipFromDerivedStats();
        MirrorSessionConsumptionToGameplayNetwork(RepairRecipe.Inputs);
        MirrorSessionGrantToGameplayNetwork(RepairRecipe.Outputs);
        _shipTerminal?.SetRepaired(true);
        RecordPlayerMultitoolUse(PlayerMultitoolFunction.Repair, "starter-ship");
        _lastDomainEvent = "StarterRepairQuestCompleted";
        _status = "starter ship repaired and commissioned; press E on it again to board";
        RecordProceduralQuestObjective(
            ProceduralQuestObjectiveType.RepairObject,
            "object.ship.starter",
            1,
            queueAutosave: false);
        QueueCurrentSnapshot(AutosaveTrigger.QuestCompleted);
        GD.Print(
            "Vertical slice domain event: StarterRepairQuestCompleted; " +
            $"autosaveTrigger={AutosaveTrigger.QuestCompleted}; " +
            $"revision={_revision}; commissioned={(ShipSystems.Commissioned ? 1 : 0)}; " +
            $"flightReady={(ShipSystems.FlightReady ? 1 : 0)}; " +
            $"interactor={interactor.Name}");
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

        EnsureGameplayProductionNetwork();
        ProductionQueueRuntime stationQueue = GetGameplayQueue(stationId);
        if (stationQueue.Jobs.Count > 0)
        {
            _lastDomainEvent = "ProductionQueueBusy";
            _status = "this station queue is active; open the Queue tab to manage it";
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

        MirrorSessionConsumptionToGameplayNetwork(recipe.Inputs);
        MirrorSessionGrantToGameplayNetwork(recipe.Outputs);
        foreach (CraftingStackDefinition output in recipe.Outputs)
        {
            StationServices.RecordObjective(
                StationServiceObjectiveType.CraftItem,
                output.DefinitionId,
                output.Quantity);
            RecordProceduralQuestObjective(
                ProceduralQuestObjectiveType.CraftItem,
                output.DefinitionId,
                output.Quantity,
                queueAutosave: false);
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
            "recipe.refining.refined_ferrite" => "TASK-096",
            "recipe.refining.purified_water" => "TASK-096",
            "recipe.chemistry.paraffinium_fraction" => "TASK-096",
            "recipe.chemistry.paraffinium_lubricant" => "TASK-096",
            "recipe.chemistry.raw_compotium_solution" => "TASK-096",
            "recipe.chemistry.compotium_concentrate" => "TASK-096",
            _ => "TASK-076"
        };
    }

    private IReadOnlyList<CatalogResourcePlacement>
        GenerateMissingCatalogResourceNodes(GameContentCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Node3D gameplay = GetNode<Node3D>("Gameplay");
        string[] existingDefinitions = GetTree()
            .GetNodesInGroup("vertical_slice_resource")
            .OfType<SalvageResourceNode>()
            .Select(node => node.ResourceDefinitionId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        IReadOnlyList<CatalogResourcePlacement> placements =
            CatalogResourceFieldPlanner.BuildMissingPlacements(
                catalog.Resources,
                existingDefinitions);
        if (placements.Count == 0)
        {
            return placements;
        }

        Node3D field = gameplay.GetNodeOrNull<Node3D>(
            "CatalogResourceField") ?? new Node3D
            {
                Name = "CatalogResourceField"
            };
        if (field.GetParent() is null)
        {
            gameplay.AddChild(field);
        }

        foreach (CatalogResourcePlacement placement in placements)
        {
            SalvageResourceNode resourceNode = new()
            {
                Name = placement.NodeName,
                ResourceNodeId = placement.ResourceNodeId,
                ResourceDefinitionId = placement.ResourceDefinitionId,
                Position = new Vector3(
                    (float)placement.PositionX,
                    (float)placement.PositionY,
                    (float)placement.PositionZ)
            };
            resourceNode.AddChild(new MeshInstance3D
            {
                Name = "MeshInstance3D",
                Mesh = new CylinderMesh
                {
                    TopRadius = 0.42f,
                    BottomRadius = 0.62f,
                    Height = 1.2f,
                    RadialSegments = 8,
                    Rings = 2
                }
            });
            resourceNode.AddChild(new CollisionShape3D
            {
                Name = "CollisionShape3D",
                Shape = new CylinderShape3D
                {
                    Radius = 0.62f,
                    Height = 1.2f
                }
            });
            field.AddChild(resourceNode);
            resourceNode.AddToGroup("interactable");
            resourceNode.AddToGroup("vertical_slice_resource");
        }

        return placements;
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
            GameItemDefinition item = ContentCatalog.GetItem(
                definition.ItemDefinitionId);
            node.ConfigureDefinition(definition);
            if (node.Quantity > item.MaxStack)
            {
                throw new InvalidOperationException(
                    $"Resource node {node.ResourceNodeId} yields " +
                    $"{node.Quantity}, exceeding {item.DefinitionId}.MaxStack=" +
                    $"{item.MaxStack}.");
            }

            availableByDefinition.TryGetValue(
                definition.ItemDefinitionId,
                out int current);
            availableByDefinition[definition.ItemDefinitionId] =
                current + node.Quantity;
        }

        string[] physicalResourceDefinitionIds = _resourceNodes
            .Select(node => node.ResourceDefinitionId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        if (!CatalogResourceFieldPlanner.CoversCatalog(
                ContentCatalog.Resources,
                physicalResourceDefinitionIds))
        {
            string[] missing = ContentCatalog.Resources.Keys
                .Except(
                    physicalResourceDefinitionIds,
                    StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            throw new InvalidOperationException(
                "Vertical slice does not physically cover all catalog " +
                "resources: " + string.Join(", ", missing));
        }

        foreach (CraftingRecipeDefinition recipe in
            new[] { RepairRecipe }.Concat(stationRecipes))
        {
            CraftingStackDefinition[] requiredStacks = recipe.Inputs
                .Concat(recipe.Catalysts.Select(catalyst =>
                    new CraftingStackDefinition(
                        catalyst.DefinitionId,
                        catalyst.Quantity)))
                .ToArray();
            foreach (CraftingStackDefinition required in requiredStacks)
            {
                availableByDefinition.TryGetValue(
                    required.DefinitionId,
                    out int available);
                bool producedByRuntimeRecipe = stationRecipes.Any(
                    producer => producer.Outputs.Any(output => string.Equals(
                        output.DefinitionId,
                        required.DefinitionId,
                        StringComparison.Ordinal)));
                if (available < required.Quantity && !producedByRuntimeRecipe)
                {
                    throw new InvalidOperationException(
                        $"Recipe {recipe.RecipeId} requires " +
                        $"{required.Quantity} x {required.DefinitionId}, but scene " +
                        $"provides only {available} and no runtime recipe " +
                        "produces the missing input or catalyst.");
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
            "TASK-100 catalog resource binding PASS: " +
            $"catalog={ContentCatalog.Resources.Count}; " +
            $"physicalTypes={physicalResourceDefinitionIds.Length}; " +
            $"nodes={_resourceNodes.Count}; " +
            $"authored={_resourceNodes.Count - _generatedResourcePlacements.Count}; " +
            $"generated={_generatedResourcePlacements.Count}; " +
            "unique=1; deterministicYield=1; maxStack=1; coverage=1.");
        CraftingRecipeDefinition[] portableRecipes = stationRecipes
            .Where(recipe => string.Equals(
                recipe.RequiredStation,
                "station.portable_fabricator",
                StringComparison.Ordinal))
            .ToArray();
        GD.Print(
            "TASK-082 station selector binding PASS: " +
            "physicalStations=1; " +
            $"selectorRecipes={portableRecipes.Length}; " +
            $"researchPoints={TechnologyProgress.ResearchPoints}; " +
            $"initiallyUnlocked={portableRecipes.Count(recipe => TechnologyProgress.IsUnlocked(recipe.RequiredTechnology))}; " +
            $"initiallyLocked={portableRecipes.Count(recipe => !TechnologyProgress.IsUnlocked(recipe.RequiredTechnology))}.");
        GD.Print(
            "TASK-102 station services binding PASS: " +
            $"economies={StationServiceCatalog.EconomyTypes.Count}; " +
            $"factions={StationServiceCatalog.Factions.Count}; " +
            $"npcs={StationServiceCatalog.Npcs.Count}; " +
            $"dialogueOptions={StationServiceCatalog.Dialogues.Values.Sum(dialogue => dialogue.Options.Count)}; " +
            $"quests={StationServiceCatalog.Quests.Count}; " +
            $"questNodes={StationServiceCatalog.Quests.Values.Sum(quest => quest.Nodes.Count)}; " +
            $"tradable={StationServices.TradableItemCount}; " +
            "priceFormula=6-factors; trade=atomic; questGraph=validated; " +
            "persistence=enabled.");
        GD.Print(
            "TASK-102 station services READY: " +
            $"npc={StationServices.NpcId}; market={StationServices.MarketId}; " +
            $"credits={StationServices.PlayerCredits}; " +
            $"reputation={StationServices.Reputation}; " +
            "tabs=Dialogue/Buy/Sell/Quests; F3=acceptance.");
        GD.Print(
            "TASK-096 multi-station industry binding PASS: " +
            $"physicalStations={_craftingStations.Count}; " +
            $"stationTypes={requiredStationIds.Length}; " +
            $"runtimeRecipes={stationRecipes.Length}; " +
            $"repeatable={stationRecipes.Count(IndustryRecipePolicy.IsRepeatable)}; " +
            $"chemistry={stationRecipes.Count(recipe => string.Equals(recipe.Category, "Chemistry", StringComparison.Ordinal))}; " +
            $"refining={stationRecipes.Count(recipe => string.Equals(recipe.Category, "Refining", StringComparison.Ordinal))}; " +
            $"resourceNodes={_resourceNodes.Count}; networkPersistence=enabled; " +
            "energyRecharge=60s-to-full.");
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

    private static StationServicesCatalog LoadStationServicesCatalog(
        GameContentCatalog contentCatalog)
    {
        const string path = "res://Content/station_services.json";
        string json = Godot.FileAccess.GetFileAsString(path);
        StationServicesCatalog catalog = StationServicesCatalog.LoadFromJson(
            json,
            contentCatalog);
        GD.Print(
            "TASK-102 station services catalog READY: " +
            $"schema={catalog.SchemaVersion}; " +
            $"factions={catalog.Factions.Count}; " +
            $"markets={catalog.Markets.Count}; " +
            $"npcs={catalog.Npcs.Count}; " +
            $"dialogues={catalog.Dialogues.Count}; " +
            $"quests={catalog.Quests.Count}; " +
            $"tradable={contentCatalog.Items.Count}.");
        return catalog;
    }

    private static BaseConstructionCatalog LoadBaseConstructionCatalog(
        GameContentCatalog contentCatalog)
    {
        const string path = "res://Content/base_construction.json";
        string json = Godot.FileAccess.GetFileAsString(path);
        BaseConstructionCatalog catalog = BaseConstructionCatalog.LoadFromJson(
            json,
            contentCatalog);
        int categories = catalog.Modules.Values
            .Select(module => module.Category)
            .Distinct(StringComparer.Ordinal)
            .Count();
        GD.Print(
            "TASK-106 base construction catalog READY: " +
            $"schema={catalog.SchemaVersion}; " +
            $"modules={catalog.Modules.Count}; categories={categories}; " +
            $"grid={catalog.GridSizeMeters.ToString("0.#", CultureInfo.InvariantCulture)}; " +
            $"limits={catalog.Limits.MaximumModules}/" +
            $"{catalog.Limits.MaximumInteractiveDevices}/" +
            $"{catalog.Limits.MaximumActivePhysicsObjects}/" +
            $"{catalog.Limits.MaximumDynamicLights}.");
        GD.Print(
            "TASK-106 base construction binding PASS: " +
            $"catalogModules={catalog.Modules.Count}; " +
            $"baseRecipes={contentCatalog.Recipes.Values.Count(recipe => string.Equals(recipe.Category, "Base", StringComparison.Ordinal))}; " +
            $"anchors={catalog.Modules.Values.Count(module => module.IsAnchor)}; " +
            "snap=cardinal; collision=grid; power=graph; persistence=enabled.");
        return catalog;
    }

    private static PlanetaryPoiCatalog LoadPlanetaryPoiCatalog()
    {
        const string path = "res://Content/planetary_pois.json";
        string json = Godot.FileAccess.GetFileAsString(path);
        PlanetaryPoiCatalog catalog = PlanetaryPoiCatalog.LoadFromJson(json);
        int categories = catalog.Definitions.Values
            .Select(definition => definition.Category)
            .Distinct(StringComparer.Ordinal)
            .Count();
        GD.Print(
            "TASK-108 planetary POI catalog READY: " +
            $"schema={catalog.SchemaVersion}; " +
            $"types={catalog.Definitions.Count}; categories={categories}; " +
            $"seed={catalog.WorldSeed}; region={catalog.RegionKey}; " +
            $"spacing={catalog.MinimumPoiSpacing.ToString("0.#", CultureInfo.InvariantCulture)}; " +
            "constraints=biome+slope+height+water+danger+rarity+quests.");
        return catalog;
    }

    private static ShipSystemsCatalog LoadShipSystemsCatalog(
        GameContentCatalog contentCatalog)
    {
        const string path = "res://Content/ships.json";
        string json = Godot.FileAccess.GetFileAsString(path);
        ShipSystemsCatalog catalog = ShipSystemsCatalog.LoadFromJson(
            json,
            contentCatalog);
        int shipModuleOutputs = contentCatalog.Recipes.Values
            .Where(recipe => string.Equals(
                recipe.Category,
                "ShipModule",
                StringComparison.Ordinal))
            .SelectMany(recipe => recipe.Outputs)
            .Select(output => output.DefinitionId)
            .Distinct(StringComparer.Ordinal)
            .Count();
        int repairItems = catalog.Systems.Values
            .Select(system => system.RepairDefinitionId)
            .Distinct(StringComparer.Ordinal)
            .Count();
        GD.Print(
            "TASK-110 ship systems catalog READY: " +
            $"schema={catalog.SchemaVersion}; " +
            $"classes={catalog.Classes.Count}; " +
            $"systems={catalog.Systems.Count}; " +
            $"modules={catalog.Modules.Count}; " +
            $"starterClass={catalog.StarterClassId}; " +
            $"moduleCoverage={catalog.Modules.Count}/{shipModuleOutputs}.");
        GD.Print(
            "TASK-110 ship systems binding PASS: " +
            $"classes={catalog.Classes.Count}; " +
            $"systems={catalog.Systems.Count}; " +
            $"modules={catalog.Modules.Count}; " +
            $"repairItems={repairItems}; " +
            $"fuel={ShipSystemsAcceptanceRunner.FuelDefinitionId}; " +
            "slots=Technology/Weapon; persistence=enabled.");
        return catalog;
    }

    private void InitializeGameplayProductionNetwork(
        ProductionQueueNetworkSaveData? saveData,
        ProductionQueueSaveData? legacySaveData)
    {
        if (_session is null || _technologyProgression is null ||
            _craftingStations.Count == 0)
        {
            return;
        }

        string[] activeStationIds = _craftingStations
            .Select(station => station.StationId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        _gameplayProductionNetwork = ProductionNetworkRuntime.Create(
            ContentCatalog.Stations,
            _stationRecipes,
            activeStationIds,
            Session.AvailableInventory,
            TechnologyProgress.IsUnlocked,
            saveData,
            legacySaveData);
        ApplyGameplayProductionNetworkStationState();
    }

    private void EnsureGameplayProductionNetwork()
    {
        if (_gameplayProductionNetwork is null)
        {
            InitializeGameplayProductionNetwork(
                saveData: null,
                legacySaveData: null);
        }
    }

    private void UpdateGameplayProductionQueue(double delta)
    {
        ProductionNetworkRuntime? network = _gameplayProductionNetwork;
        if (network is null)
        {
            return;
        }

        network.RechargeAll(delta, fullRechargeSeconds: 60.0);
        IReadOnlyList<StationProductionAdvance> advances =
            network.Advance(delta);
        ApplyGameplayProductionNetworkStationState();
        if (advances.All(advance =>
            advance.Report.CompletedProcesses.Count == 0))
        {
            return;
        }

        int completedCount = 0;
        foreach (StationProductionAdvance stationAdvance in advances)
        {
            ProductionQueueRuntime queue =
                network.GetQueue(stationAdvance.StationId);
            foreach (IndustryProcessExecutionReport process in
                stationAdvance.Report.CompletedProcesses)
            {
                completedCount++;
                foreach (CraftingStackDefinition catalyst in
                    process.RetainedCatalysts)
                {
                    Session.GrantInventory(
                        catalyst.DefinitionId,
                        catalyst.Quantity);
                    network.AddInventoryAllExcept(
                        queue.StationId,
                        catalyst.DefinitionId,
                        catalyst.Quantity);
                }

                CraftingRecipeDefinition completedRecipe =
                    ContentCatalog.GetRecipe(process.RecipeId);
                IndustryItemProperties outputProperties =
                    ItemPropertyRuntime.CreateOutputProperties(
                        completedRecipe,
                        process.ProcessSequence,
                        ItemPropertyRuntime.CreateNominalEnvironment(
                            completedRecipe));
                foreach (CraftingStackDefinition output in process.Outputs)
                {
                    Session.GrantInventory(
                        output.DefinitionId,
                        output.Quantity,
                        outputProperties);
                    network.AddInventoryAllExcept(
                        queue.StationId,
                        output.DefinitionId,
                        output.Quantity);
                    StationServices.RecordObjective(
                        StationServiceObjectiveType.CraftItem,
                        output.DefinitionId,
                        output.Quantity);
                    RecordProceduralQuestObjective(
                        ProceduralQuestObjectiveType.CraftItem,
                        output.DefinitionId,
                        output.Quantity,
                        queueAutosave: false);
                }

                foreach (CraftingStackDefinition byproduct in process.Byproducts)
                {
                    Session.GrantInventory(
                        byproduct.DefinitionId,
                        byproduct.Quantity);
                    network.AddInventoryAllExcept(
                        queue.StationId,
                        byproduct.DefinitionId,
                        byproduct.Quantity);
                }

                _lastDomainEvent =
                    $"ProductionJobCompleted({process.RecipeId})";
                GD.Print(
                    "TASK-092 player queue completion PASS: " +
                    $"station={queue.StationId}; recipe={process.RecipeId}; " +
                    $"outputs={process.Outputs.Sum(output => output.Quantity)}; " +
                    $"byproducts={process.Byproducts.Sum(output => output.Quantity)}; " +
                    $"energyRemaining={queue.EnergyRemaining.ToString("0.###", CultureInfo.InvariantCulture)}; " +
                    $"running={queue.RunningCount}; queued={queue.QueuedCount}; " +
                    $"paused={queue.PausedCount}.");
            }
        }

        ApplySessionToScene();
        _status = completedCount == 1
            ? "production completed"
            : $"production completed: {completedCount} jobs";
        QueueCurrentSnapshot(AutosaveTrigger.QuestCompleted);
    }

    private void ApplyGameplayProductionNetworkStationState()
    {
        ProductionNetworkRuntime? network = _gameplayProductionNetwork;
        if (network is null)
        {
            return;
        }

        foreach (PortableCraftingStation station in _craftingStations)
        {
            ProductionQueueRuntime queue = network.GetQueue(station.StationId);
            station.SetCrafting(queue.RunningCount > 0);
            if (queue.RunningCount > 0)
            {
                continue;
            }

            CraftingRecipeDefinition[] uniqueRecipes = StationRecipes
                .Where(recipe =>
                    string.Equals(
                        recipe.RequiredStation,
                        station.StationId,
                        StringComparison.Ordinal) &&
                    !IndustryRecipePolicy.IsRepeatable(recipe))
                .ToArray();
            station.SetCrafted(
                uniqueRecipes.Length > 0 &&
                uniqueRecipes.All(recipe =>
                    Session.IsRecipeCrafted(recipe.RecipeId)));
        }
    }

    private void EnsureNetworkMirrorCanConsume(
        IReadOnlyList<CraftingStackDefinition> stacks,
        string excludedStationId)
    {
        foreach (ProductionQueueRuntime queue in GameplayNetwork.Queues)
        {
            if (string.Equals(
                    queue.StationId,
                    excludedStationId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            foreach (CraftingStackDefinition stack in stacks)
            {
                int available = queue.GetQuantity(stack.DefinitionId);
                if (available < stack.Quantity)
                {
                    throw new InvalidOperationException(
                        $"Gameplay production network mirror {queue.StationId} " +
                        $"is missing {stack.Quantity - available} x " +
                        $"{stack.DefinitionId}.");
                }
            }
        }
    }

    private void MirrorSessionConsumptionToGameplayNetwork(
        IReadOnlyList<CraftingStackDefinition> stacks,
        string? excludedStationId = null)
    {
        ProductionNetworkRuntime? network = _gameplayProductionNetwork;
        if (network is null)
        {
            return;
        }

        foreach (CraftingStackDefinition stack in stacks)
        {
            string result;
            bool consumed;
            if (excludedStationId is null)
            {
                consumed = network.TryConsumeInventoryAll(
                    stack.DefinitionId,
                    stack.Quantity,
                    out result);
            }
            else
            {
                consumed = network.TryConsumeInventoryAllExcept(
                    excludedStationId,
                    stack.DefinitionId,
                    stack.Quantity,
                    out result);
            }

            if (!consumed)
            {
                throw new InvalidOperationException(
                    $"Gameplay production network desynchronized: {result}.");
            }
        }
    }

    private void MirrorSessionGrantToGameplayNetwork(
        IReadOnlyList<CraftingStackDefinition> stacks,
        string? excludedStationId = null)
    {
        ProductionNetworkRuntime? network = _gameplayProductionNetwork;
        if (network is null)
        {
            return;
        }

        foreach (CraftingStackDefinition stack in stacks)
        {
            if (excludedStationId is null)
            {
                network.AddInventoryAll(stack.DefinitionId, stack.Quantity);
            }
            else
            {
                network.AddInventoryAllExcept(
                    excludedStationId,
                    stack.DefinitionId,
                    stack.Quantity);
            }
        }
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

    private IReadOnlyList<ShipModuleDefinition> ShipModuleDefinitions =>
        ShipSystemsCatalog.Modules.Values
            .OrderBy(module => module.ModuleId, StringComparer.Ordinal)
            .ToArray();

    private IReadOnlyList<ShipSystemDefinition> ShipSystemDefinitions =>
        ShipSystemsCatalog.Systems.Values
            .OrderBy(system => system.SystemId, StringComparer.Ordinal)
            .ToArray();

    private void OpenShipManagement()
    {
        if (_shipManagementPanel is null)
        {
            return;
        }

        CloseRecipeSelector();
        CloseStationServices();
        CloseBaseBuildMode();
        CloseDiscoveryCatalog();
        _shipManagementOpen = true;
        _shipManagementTab = ShipManagementTab.Overview;
        _shipManagementIndex = 0;
        _shipManagementFeedback = Session.ShipRepaired && ShipSystems.Commissioned
            ? "ship systems commissioned and online"
            : "starter ship must be repaired and commissioned before loadout changes";
        _shipManagementOpenedTicks = Time.GetTicksMsec();
        _shipManagementPanel.Visible = true;
        UpdateShipManagementPanel();
        _lastDomainEvent = "ShipManagementOpened";
    }

    private void CloseShipManagement(string status = "")
    {
        _shipManagementOpen = false;
        if (_shipManagementPanel is not null)
        {
            _shipManagementPanel.Visible = false;
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            _status = status;
        }
    }

    private void MoveShipManagementSelection(int delta)
    {
        int count = GetShipManagementItemCount();
        if (count <= 0)
        {
            _shipManagementIndex = 0;
            return;
        }

        _shipManagementIndex = (_shipManagementIndex + delta) % count;
        if (_shipManagementIndex < 0)
        {
            _shipManagementIndex += count;
        }

        UpdateShipManagementPanel();
    }

    private void CycleShipManagementTab()
    {
        _shipManagementTab = (ShipManagementTab)(
            ((int)_shipManagementTab + 1) % 3);
        _shipManagementIndex = 0;
        _shipManagementFeedback = $"tab={_shipManagementTab}";
        UpdateShipManagementPanel();
    }

    private int GetShipManagementItemCount()
    {
        return _shipManagementTab switch
        {
            ShipManagementTab.Overview => 1,
            ShipManagementTab.Modules => ShipModuleDefinitions.Count,
            ShipManagementTab.Systems => ShipSystemDefinitions.Count,
            _ => 0
        };
    }

    private void ConfirmShipManagementSelection()
    {
        switch (_shipManagementTab)
        {
            case ShipManagementTab.Overview:
                RefuelShip();
                break;
            case ShipManagementTab.Modules:
                InstallSelectedShipModule();
                break;
            case ShipManagementTab.Systems:
                RepairSelectedShipSystem();
                break;
        }
    }

    private void InstallSelectedShipModule()
    {
        if (!Session.ShipRepaired || !ShipSystems.Commissioned)
        {
            _shipManagementFeedback = "repair and commission the starter ship first";
            return;
        }

        IReadOnlyList<ShipModuleDefinition> definitions = ShipModuleDefinitions;
        if (definitions.Count == 0)
        {
            return;
        }

        _shipManagementIndex = Math.Clamp(
            _shipManagementIndex,
            0,
            definitions.Count - 1);
        ShipModuleDefinition definition = definitions[_shipManagementIndex];
        ShipModuleInstallResult preflight = ShipSystems.CanInstall(
            definition.ModuleId,
            out string result);
        if (preflight != ShipModuleInstallResult.Installed)
        {
            _shipManagementFeedback = result;
            return;
        }

        if (!TryConsumeSharedInventory(
            definition.ModuleId,
            1,
            out string inventoryResult))
        {
            _shipManagementFeedback = inventoryResult;
            return;
        }

        ShipModuleInstallResult installed = ShipSystems.TryInstall(
            definition.ModuleId,
            out result);
        if (installed != ShipModuleInstallResult.Installed)
        {
            GrantSharedInventory(definition.ModuleId, 1);
            _shipManagementFeedback = result;
            return;
        }

        _shipManagementFeedback = result;
        _lastDomainEvent = $"ShipModuleInstalled({definition.ModuleId})";
        QueueCurrentSnapshot(AutosaveTrigger.ShipChanged);
        GD.Print(
            "TASK-110 player ship module install PASS: " +
            $"module={definition.ModuleId}; slot={definition.SlotType}; " +
            $"installed={ShipSystems.InstalledModuleCount}; " +
            $"flightReady={(ShipSystems.FlightReady ? 1 : 0)}; " +
            $"hyperReady={(ShipSystems.HyperspaceReady ? 1 : 0)}.");
        UpdateShipManagementPanel();
    }

    private void UninstallSelectedShipModule()
    {
        if (!Session.ShipRepaired || !ShipSystems.Commissioned)
        {
            _shipManagementFeedback = "repair and commission the starter ship first";
            return;
        }

        IReadOnlyList<ShipModuleDefinition> definitions = ShipModuleDefinitions;
        if (definitions.Count == 0)
        {
            return;
        }

        _shipManagementIndex = Math.Clamp(
            _shipManagementIndex,
            0,
            definitions.Count - 1);
        ShipModuleDefinition definition = definitions[_shipManagementIndex];
        ShipModuleUninstallResult uninstalled = ShipSystems.TryUninstall(
            definition.ModuleId,
            out string result);
        if (uninstalled != ShipModuleUninstallResult.Uninstalled)
        {
            _shipManagementFeedback = result;
            return;
        }

        GrantSharedInventory(definition.ModuleId, 1);
        _shipManagementFeedback = result;
        _lastDomainEvent = $"ShipModuleUninstalled({definition.ModuleId})";
        QueueCurrentSnapshot(AutosaveTrigger.ShipChanged);
        GD.Print(
            "TASK-110 player ship module uninstall PASS: " +
            $"module={definition.ModuleId}; " +
            $"installed={ShipSystems.InstalledModuleCount}; refund=1.");
        UpdateShipManagementPanel();
    }

    private void DamageSelectedShipSystem()
    {
        if (!Session.ShipRepaired || !ShipSystems.Commissioned)
        {
            _shipManagementFeedback = "repair and commission the starter ship first";
            return;
        }

        IReadOnlyList<ShipSystemDefinition> definitions = ShipSystemDefinitions;
        if (definitions.Count == 0)
        {
            return;
        }

        _shipManagementIndex = Math.Clamp(
            _shipManagementIndex,
            0,
            definitions.Count - 1);
        ShipSystemDefinition definition = definitions[_shipManagementIndex];
        ShipSystemMutationResult mutation = ShipSystems.ApplyDamage(
            definition.SystemId,
            25.0,
            out string result);
        _shipManagementFeedback = result;
        if (mutation != ShipSystemMutationResult.Applied)
        {
            return;
        }

        _lastDomainEvent = $"ShipSystemDamaged({definition.SystemId})";
        QueueCurrentSnapshot(AutosaveTrigger.ShipChanged);
        GD.Print(
            "TASK-110 player ship damage PASS: " +
            $"system={definition.SystemId}; " +
            $"health={ShipSystems.GetSystemHealth(definition.SystemId):0.#}/" +
            $"{ShipSystems.GetSystemMaximumHealth(definition.SystemId):0.#}; " +
            $"flightReady={(ShipSystems.FlightReady ? 1 : 0)}; " +
            $"hyperReady={(ShipSystems.HyperspaceReady ? 1 : 0)}.");
        UpdateShipManagementPanel();
    }

    private void RepairSelectedShipSystem()
    {
        if (!Session.ShipRepaired || !ShipSystems.Commissioned)
        {
            _shipManagementFeedback = "repair and commission the starter ship first";
            return;
        }

        IReadOnlyList<ShipSystemDefinition> definitions = ShipSystemDefinitions;
        if (definitions.Count == 0)
        {
            return;
        }

        _shipManagementIndex = Math.Clamp(
            _shipManagementIndex,
            0,
            definitions.Count - 1);
        ShipSystemDefinition definition = definitions[_shipManagementIndex];
        double current = ShipSystems.GetSystemHealth(definition.SystemId);
        double maximum = ShipSystems.GetSystemMaximumHealth(definition.SystemId);
        if (current + 0.0001 >= maximum)
        {
            _shipManagementFeedback = $"{definition.SystemId} is already full";
            return;
        }

        if (!TryConsumeSharedInventory(
            definition.RepairDefinitionId,
            1,
            out string inventoryResult))
        {
            _shipManagementFeedback = inventoryResult;
            return;
        }

        ShipSystemMutationResult mutation = ShipSystems.Repair(
            definition.SystemId,
            definition.RepairPerUnit,
            out string result);
        if (mutation != ShipSystemMutationResult.Applied)
        {
            GrantSharedInventory(definition.RepairDefinitionId, 1);
            _shipManagementFeedback = result;
            return;
        }

        _shipManagementFeedback = result;
        RecordPlayerMultitoolUse(PlayerMultitoolFunction.Repair, definition.SystemId);
        _lastDomainEvent = $"ShipSystemRepaired({definition.SystemId})";
        RecordProceduralQuestObjective(
            ProceduralQuestObjectiveType.RepairObject,
            "object.ship.starter",
            1,
            queueAutosave: false);
        QueueCurrentSnapshot(AutosaveTrigger.ShipChanged);
        GD.Print(
            "TASK-110 player ship repair PASS: " +
            $"system={definition.SystemId}; " +
            $"repairItem={definition.RepairDefinitionId}; " +
            $"health={ShipSystems.GetSystemHealth(definition.SystemId):0.#}/" +
            $"{ShipSystems.GetSystemMaximumHealth(definition.SystemId):0.#}.");
        UpdateShipManagementPanel();
    }

    private void RefuelShip()
    {
        if (!Session.ShipRepaired || !ShipSystems.Commissioned)
        {
            _shipManagementFeedback = "repair and commission the starter ship first";
            return;
        }

        double capacity = ShipSystems.GetEffectiveStats().FuelCapacity;
        if (ShipSystems.Fuel + 0.0001 >= capacity)
        {
            _shipManagementFeedback = "fuel tank is already full";
            return;
        }

        if (!TryConsumeSharedInventory(
            ShipSystemsAcceptanceRunner.FuelDefinitionId,
            1,
            out string inventoryResult))
        {
            _shipManagementFeedback = inventoryResult;
            return;
        }

        double added = ShipSystems.Refuel(25.0);
        if (added <= 0.0)
        {
            GrantSharedInventory(ShipSystemsAcceptanceRunner.FuelDefinitionId, 1);
            _shipManagementFeedback = "refuel produced no fuel";
            return;
        }

        _shipManagementFeedback =
            $"refueled +{added:0.#}; fuel={ShipSystems.Fuel:0.#}/{capacity:0.#}";
        _lastDomainEvent = "ShipRefueled";
        QueueCurrentSnapshot(AutosaveTrigger.ShipChanged);
        GD.Print(
            "TASK-110 player ship refuel PASS: " +
            $"fuel={ShipSystems.Fuel:0.#}/{capacity:0.#}; " +
            $"item={ShipSystemsAcceptanceRunner.FuelDefinitionId}.");
        UpdateShipManagementPanel();
    }

    private bool TryConsumeSharedInventory(
        string definitionId,
        int quantity,
        out string result)
    {
        if (Session.GetAvailableQuantity(definitionId) < quantity)
        {
            result = $"missing {quantity} x {definitionId}";
            return false;
        }

        ProductionQueueRuntime? missingMirror = GameplayNetwork.Queues
            .FirstOrDefault(queue =>
                queue.GetQuantity(definitionId) < quantity);
        if (missingMirror is not null)
        {
            result = $"inventory mirror {missingMirror.StationId} is missing " +
                $"{definitionId}";
            return false;
        }

        if (!Session.TryConsumeInventory(definitionId, quantity, out result))
        {
            return false;
        }

        MirrorSessionConsumptionToGameplayNetwork(
            new[] { new CraftingStackDefinition(definitionId, quantity) });
        return true;
    }

    private void GrantSharedInventory(string definitionId, int quantity)
    {
        Session.GrantInventory(definitionId, quantity);
        MirrorSessionGrantToGameplayNetwork(
            new[] { new CraftingStackDefinition(definitionId, quantity) });
    }

    private void UpdateShipManagementPanel()
    {
        if (_shipManagementPanel is null || _shipManagementLabel is null)
        {
            return;
        }

        _shipManagementPanel.Visible = _shipManagementOpen;
        if (!_shipManagementOpen)
        {
            return;
        }

        ShipEffectiveStats stats = ShipSystems.GetEffectiveStats();
        string tabs = string.Join(
            "  ",
            Enum.GetValues<ShipManagementTab>().Select(tab =>
                tab == _shipManagementTab ? $"[{tab}]" : tab.ToString()));
        string content;
        if (_shipManagementTab == ShipManagementTab.Overview)
        {
            content =
                $"Class: {GetShortContentId(ShipSystems.ShipClassId)}\n" +
                $"Hull={stats.Hull:0.#}  Shield={stats.Shield:0.#}  Cargo={stats.CargoCapacity}\n" +
                $"Fuel={ShipSystems.Fuel:0.#}/{stats.FuelCapacity:0.#}  " +
                $"Accel={stats.Acceleration:0.#}  Speed={stats.MaxSpeed:0.#}\n" +
                $"Maneuver={stats.Maneuverability:0.#}  HyperRange={stats.HyperdriveRange:0.#}  " +
                $"Atmos={stats.AtmosphericEfficiency:0.#}%\n" +
                $"Slots: weapon={ShipSystems.InstalledWeaponModules}/{stats.WeaponSlots}  " +
                $"technology={ShipSystems.InstalledTechnologyModules}/{stats.TechnologySlots}\n" +
                $"Readiness: commissioned={(ShipSystems.Commissioned ? "YES" : "NO")}  " +
                $"flight={(ShipSystems.FlightReady ? "READY" : "BLOCKED")}  " +
                $"hyperspace={(ShipSystems.HyperspaceReady ? "READY" : "BLOCKED")}  " +
                $"offlineSystems={ShipSystems.DisabledSystemCount}\n\n" +
                $"Enter/E: refuel with 1 x {ShipSystemsAcceptanceRunner.FuelDefinitionId} " +
                $"(inventory={Session.GetAvailableQuantity(ShipSystemsAcceptanceRunner.FuelDefinitionId)})";
        }
        else if (_shipManagementTab == ShipManagementTab.Modules)
        {
            IReadOnlyList<ShipModuleDefinition> modules = ShipModuleDefinitions;
            _shipManagementIndex = Math.Clamp(
                _shipManagementIndex,
                0,
                Math.Max(0, modules.Count - 1));
            int start = Math.Max(0, _shipManagementIndex - 5);
            int end = Math.Min(modules.Count, start + 11);
            start = Math.Max(0, end - 11);
            List<string> lines = new();
            for (int index = start; index < end; index++)
            {
                ShipModuleDefinition module = modules[index];
                InstalledShipModuleState? installed = ShipSystems.InstalledModules
                    .FirstOrDefault(value => string.Equals(
                        value.Definition.ModuleId,
                        module.ModuleId,
                        StringComparison.Ordinal));
                string state = installed is null
                    ? "AVAILABLE"
                    : installed.Active ? "INSTALLED/ACTIVE" : "INSTALLED/OFFLINE";
                lines.Add(
                    $"{(index == _shipManagementIndex ? ">" : " ")} " +
                    $"{GetShortContentId(module.ModuleId),-24} " +
                    $"{module.SlotType,-10} {state,-18} " +
                    $"inv={Session.GetAvailableQuantity(module.ModuleId)}");
            }

            content = string.Join("\n", lines) +
                "\n\nEnter/E: install selected  X: uninstall selected";
        }
        else
        {
            IReadOnlyList<ShipSystemDefinition> systems = ShipSystemDefinitions;
            _shipManagementIndex = Math.Clamp(
                _shipManagementIndex,
                0,
                Math.Max(0, systems.Count - 1));
            List<string> lines = new();
            for (int index = 0; index < systems.Count; index++)
            {
                ShipSystemDefinition system = systems[index];
                double health = ShipSystems.GetSystemHealth(system.SystemId);
                double maximum = ShipSystems.GetSystemMaximumHealth(system.SystemId);
                lines.Add(
                    $"{(index == _shipManagementIndex ? ">" : " ")} " +
                    $"{GetShortContentId(system.SystemId),-14} " +
                    $"{health,6:0.#}/{maximum,-6:0.#} " +
                    $"repair={GetShortContentId(system.RepairDefinitionId)} " +
                    $"inv={Session.GetAvailableQuantity(system.RepairDefinitionId)}");
            }

            content = string.Join("\n", lines) +
                "\n\nEnter/E or R: repair selected  D: apply 25 test damage";
        }

        _shipManagementLabel.Text =
            "SHIP MANAGEMENT - TASK-110\n" +
            tabs + "\n" +
            $"Starter repair: {(Session.ShipRepaired ? "COMPLETE" : "REQUIRED")}  " +
            $"Commissioned: {(ShipSystems.Commissioned ? "YES" : "NO")}\n\n" +
            content + "\n\n" +
            $"Status: {_shipManagementFeedback}\n" +
            "Up/Down select  Tab pages  U/Esc close";
    }

    private IReadOnlyList<BaseModuleDefinition> BaseBuildDefinitions =>
        BaseConstructionCatalog.Modules.Values
            .OrderBy(module => module.ModuleId, StringComparer.Ordinal)
            .ToArray();

    private void OpenBaseBuildMode()
    {
        CloseRecipeSelector();
        CloseStationServices();
        CloseDiscoveryCatalog();
        CloseShipManagement();
        CloseBaseBuildMode();
        _baseBuildMode = true;
        IReadOnlyList<BaseModuleDefinition> definitions = BaseBuildDefinitions;
        if (BaseConstruction.ModuleCount == 0)
        {
            _baseBuildIndex = definitions
                .Select((definition, index) => (definition, index))
                .Single(pair => pair.definition.IsAnchor)
                .index;
        }
        else
        {
            _baseBuildIndex = Math.Clamp(
                _baseBuildIndex,
                0,
                Math.Max(0, definitions.Count - 1));
        }

        _baseBuildFeedback = BaseConstruction.ModuleCount == 0
            ? "anchor selected; place it to start the connected base graph"
            : "select a module and place it on an adjacent grid cell";
        if (_baseConstructionPanel is not null)
        {
            _baseConstructionPanel.Visible = true;
        }

        if (_baseBuildPreview is not null)
        {
            _baseBuildPreview.Visible = true;
        }

        UpdateBaseConstructionPanel();
        UpdateBaseBuildPreview();
        _status = "base construction mode";
        GD.Print(
            "TASK-106 player base construction mode PASS: " +
            $"modules={BaseConstruction.ModuleCount}; " +
            $"stock={BaseBuildDefinitions.Sum(module => BaseConstruction.GetStock(module.ModuleId))}; " +
            "controls=Up/Down,R,Enter,X,T,G.");
    }

    private void CloseBaseBuildMode(string status = "")
    {
        _baseBuildMode = false;
        if (_baseConstructionPanel is not null)
        {
            _baseConstructionPanel.Visible = false;
        }

        if (_baseBuildPreview is not null)
        {
            _baseBuildPreview.Visible = false;
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            _status = status;
        }
    }

    private void MoveBaseBuildSelection(int delta)
    {
        IReadOnlyList<BaseModuleDefinition> definitions = BaseBuildDefinitions;
        if (definitions.Count == 0)
        {
            return;
        }

        _baseBuildIndex = (_baseBuildIndex + delta) % definitions.Count;
        if (_baseBuildIndex < 0)
        {
            _baseBuildIndex += definitions.Count;
        }

        _baseBuildFeedback = "module selection changed";
        UpdateBaseConstructionPanel();
        UpdateBaseBuildPreview();
    }

    private void PlaceSelectedBaseModule()
    {
        IReadOnlyList<BaseModuleDefinition> definitions = BaseBuildDefinitions;
        if (definitions.Count == 0)
        {
            return;
        }

        BaseModuleDefinition definition = definitions[_baseBuildIndex];
        var (gridX, gridZ, _) = GetBaseBuildTarget();
        BasePlacementResult placementResult = BaseConstruction.TryPlace(
            definition.ModuleId,
            gridX,
            gridZ,
            _baseBuildRotation,
            out BaseModulePlacement? placement,
            out string result);
        _baseBuildFeedback = result;
        if (placementResult == BasePlacementResult.Placed && placement is not null)
        {
            RebuildBaseConstructionScene();
            _lastDomainEvent =
                $"BaseModulePlaced({placement.InstanceId},{placement.ModuleId})";
            RecordProceduralQuestObjective(
                ProceduralQuestObjectiveType.BuildModule,
                placement.ModuleId,
                1,
                queueAutosave: false);
            QueueCurrentSnapshot(AutosaveTrigger.BaseChanged);
            GD.Print(
                "TASK-106 player base placement PASS: " +
                $"instance={placement.InstanceId}; module={placement.ModuleId}; " +
                $"grid={placement.GridX},{placement.GridZ}; " +
                $"rotation={placement.RotationQuarterTurns * 90}; " +
                $"modules={BaseConstruction.ModuleCount}; " +
                $"power={BaseConstruction.Power.Generation.ToString("0.#", CultureInfo.InvariantCulture)}/" +
                $"{BaseConstruction.Power.Consumption.ToString("0.#", CultureInfo.InvariantCulture)}.");
        }

        UpdateBaseConstructionPanel();
        UpdateBaseBuildPreview();
    }

    private void RemoveTargetBaseModule()
    {
        var (gridX, gridZ, _) = GetBaseBuildTarget();
        BaseModulePlacement? placement = BaseConstruction.FindAt(gridX, gridZ);
        if (placement is null)
        {
            _baseBuildFeedback = $"no base module at {gridX},{gridZ}";
            UpdateBaseConstructionPanel();
            return;
        }

        bool removed = BaseConstruction.TryRemove(
            placement.InstanceId,
            out string result);
        _baseBuildFeedback = result;
        if (removed)
        {
            RebuildBaseConstructionScene();
            _lastDomainEvent =
                $"BaseModuleRemoved({placement.InstanceId},{placement.ModuleId})";
            QueueCurrentSnapshot(AutosaveTrigger.BaseChanged);
            GD.Print(
                "TASK-106 player base removal PASS: " +
                $"instance={placement.InstanceId}; module={placement.ModuleId}; " +
                $"modules={BaseConstruction.ModuleCount}; refunded=1; " +
                $"connected={BaseConstruction.Power.ConnectedComponents}.");
        }

        UpdateBaseConstructionPanel();
        UpdateBaseBuildPreview();
    }

    private void ToggleTargetBaseModule()
    {
        var (gridX, gridZ, _) = GetBaseBuildTarget();
        BaseModulePlacement? placement = BaseConstruction.FindAt(gridX, gridZ);
        if (placement is null)
        {
            _baseBuildFeedback = $"no base module at {gridX},{gridZ}";
            UpdateBaseConstructionPanel();
            return;
        }

        bool toggled = BaseConstruction.TryToggle(
            placement.InstanceId,
            out string result);
        _baseBuildFeedback = result;
        if (toggled)
        {
            RebuildBaseConstructionScene();
            _lastDomainEvent =
                $"BaseDeviceToggled({placement.InstanceId})";
            QueueCurrentSnapshot(AutosaveTrigger.BaseChanged);
            GD.Print(
                "TASK-106 player base device toggle PASS: " +
                $"instance={placement.InstanceId}; result={result}; " +
                $"powered={BaseConstruction.Power.PoweredConsumers}/" +
                $"{BaseConstruction.Power.EnabledConsumers}.");
        }

        UpdateBaseConstructionPanel();
        UpdateBaseBuildPreview();
    }

    private (int GridX, int GridZ, Vector3 WorldPosition)
        GetBaseBuildTarget()
    {
        PlayerController player = _player ??
            throw new InvalidOperationException("Player is unavailable.");
        Vector3 forward = -player.GlobalTransform.Basis.Z;
        forward.Y = 0.0f;
        if (forward.LengthSquared() < 0.0001f)
        {
            forward = Vector3.Forward;
        }
        else
        {
            forward = forward.Normalized();
        }

        Vector3 target = player.GlobalPosition + forward * 4.5f;
        double gridSize = BaseConstructionCatalog.GridSizeMeters;
        int gridX = (int)Math.Round(
            target.X / gridSize,
            MidpointRounding.AwayFromZero);
        int gridZ = (int)Math.Round(
            target.Z / gridSize,
            MidpointRounding.AwayFromZero);
        return (
            gridX,
            gridZ,
            new Vector3(
                (float)(gridX * gridSize),
                0.11f,
                (float)(gridZ * gridSize)));
    }

    private void UpdateBaseConstructionPanel()
    {
        if (_baseConstructionLabel is null || !_baseBuildMode)
        {
            return;
        }

        IReadOnlyList<BaseModuleDefinition> definitions = BaseBuildDefinitions;
        if (definitions.Count == 0)
        {
            _baseConstructionLabel.Text = "BASE CONSTRUCTION\nNo modules.";
            return;
        }

        _baseBuildIndex = Math.Clamp(
            _baseBuildIndex,
            0,
            definitions.Count - 1);
        BaseModuleDefinition selected = definitions[_baseBuildIndex];
        (int gridX, int gridZ, Vector3 worldPosition) = GetBaseBuildTarget();
        BaseModulePlacement? targetModule = BaseConstruction.FindAt(
            gridX,
            gridZ);
        List<string> lines = new()
        {
            "BASE CONSTRUCTION — Stage 2 foundation subsystem",
            $"Base: {BaseConstruction.BuildSummary()}",
            $"Target: grid={gridX},{gridZ} • world=" +
                $"X={worldPosition.X:0.0} Z={worldPosition.Z:0.0} • " +
                (targetModule is null
                    ? "empty"
                    : $"{targetModule.ModuleId} ({targetModule.InstanceId})"),
            $"Selected: {selected.ModuleId} • category={selected.Category} • " +
                $"stock={BaseConstruction.GetStock(selected.ModuleId)} • " +
                $"rotation={_baseBuildRotation * 90}°",
            $"Power: generation={BaseConstruction.Power.Generation:0.#} • " +
                $"consumption={BaseConstruction.Power.Consumption:0.#} • " +
                $"battery={BaseConstruction.Power.BatteryStored:0.#}/" +
                $"{BaseConstruction.Power.BatteryCapacity:0.#} • " +
                $"powered={BaseConstruction.Power.PoweredConsumers}/" +
                $"{BaseConstruction.Power.EnabledConsumers}",
            "",
            "MODULE PALETTE"
        };
        const int visiblePaletteRows = 11;
        int paletteStart = Math.Clamp(
            _baseBuildIndex - visiblePaletteRows / 2,
            0,
            Math.Max(0, definitions.Count - visiblePaletteRows));
        int paletteEnd = Math.Min(
            definitions.Count,
            paletteStart + visiblePaletteRows);
        if (paletteStart > 0)
        {
            lines.Add($"  ... {paletteStart} earlier modules ...");
        }

        for (int index = paletteStart; index < paletteEnd; index++)
        {
            BaseModuleDefinition definition = definitions[index];
            lines.Add(
                $"{(index == _baseBuildIndex ? ">" : " ")} " +
                $"{definition.ModuleId} [{definition.Category}] " +
                $"stock={BaseConstruction.GetStock(definition.ModuleId)} " +
                $"P={definition.PowerGeneration:0.#}/" +
                $"{definition.PowerConsumption:0.#} " +
                $"B={definition.BatteryCapacity:0.#}");
        }

        if (paletteEnd < definitions.Count)
        {
            lines.Add(
                $"  ... {definitions.Count - paletteEnd} later modules ...");
        }

        lines.Add("");
        lines.Add("Up/Down select • R rotate • Enter place • X/Delete remove");
        lines.Add("T enable/disable targeted device • G/Esc close");
        lines.Add($"Result: {_baseBuildFeedback}");
        _baseConstructionLabel.Text = string.Join("\n", lines);
    }

    private void UpdateBaseBuildPreview()
    {
        if (_baseBuildPreview is null)
        {
            return;
        }

        if (!_baseBuildMode || BaseBuildDefinitions.Count == 0)
        {
            _baseBuildPreview.Visible = false;
            return;
        }

        BaseModuleDefinition definition = BaseBuildDefinitions[_baseBuildIndex];
        (int gridX, int gridZ, Vector3 worldPosition) = GetBaseBuildTarget();
        bool hasAdjacent = BaseConstruction.ModuleCount == 0
            ? definition.IsAnchor
            : BaseConstruction.Placements.Any(placement =>
                Math.Abs(placement.GridX - gridX) +
                Math.Abs(placement.GridZ - gridZ) == 1);
        bool valid = BaseConstruction.GetStock(definition.ModuleId) > 0 &&
            BaseConstruction.FindAt(gridX, gridZ) is null &&
            hasAdjacent;
        EnsureBaseBuildPreviewMesh(definition);
        _baseBuildPreview.GlobalPosition = new Vector3(
            worldPosition.X,
            (float)(definition.Size.Y * 0.5 + 0.11),
            worldPosition.Z);
        _baseBuildPreview.Rotation = new Vector3(
            0.0f,
            _baseBuildRotation * Mathf.Pi * 0.5f,
            0.0f);
        _baseBuildPreview.Visible = true;
        if (_baseBuildPreview.Mesh is PrimitiveMesh primitiveMesh &&
            primitiveMesh.Material is StandardMaterial3D material)
        {
            material.AlbedoColor = valid
                ? new Color(0.18f, 0.92f, 0.42f, 0.42f)
                : new Color(0.92f, 0.18f, 0.18f, 0.42f);
        }
    }

    private void EnsureBaseBuildPreviewMesh(BaseModuleDefinition definition)
    {
        if (_baseBuildPreview is null)
        {
            return;
        }

        if (_baseBuildPreview.Mesh is not null &&
            string.Equals(
                _baseBuildPreview.Mesh.ResourceName,
                definition.ModuleId,
                StringComparison.Ordinal))
        {
            return;
        }

        StandardMaterial3D material = new()
        {
            AlbedoColor = new Color(0.18f, 0.92f, 0.42f, 0.42f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Metallic = 0.1f,
            Roughness = 0.45f
        };
        if (string.Equals(definition.Shape, "Cylinder", StringComparison.Ordinal))
        {
            float radius = (float)(Math.Max(
                definition.Size.X,
                definition.Size.Z) * 0.5);
            _baseBuildPreview.Mesh = new CylinderMesh
            {
                ResourceName = definition.ModuleId,
                Material = material,
                TopRadius = radius * 0.92f,
                BottomRadius = radius,
                Height = (float)definition.Size.Y,
                RadialSegments = 16
            };
        }
        else
        {
            _baseBuildPreview.Mesh = new BoxMesh
            {
                ResourceName = definition.ModuleId,
                Material = material,
                Size = new Vector3(
                    (float)definition.Size.X,
                    (float)definition.Size.Y,
                    (float)definition.Size.Z)
            };
        }
    }

    private void RebuildBaseConstructionScene()
    {
        if (_baseConstructionModulesRoot is null ||
            _baseConstructionRuntime is null ||
            _baseConstructionCatalog is null)
        {
            return;
        }

        foreach (Node child in _baseConstructionModulesRoot.GetChildren())
        {
            _baseConstructionModulesRoot.RemoveChild(child);
            child.QueueFree();
        }

        foreach (BaseModulePlacement placement in BaseConstruction.Placements)
        {
            BaseConstructionModuleNode node = new();
            node.Configure(
                BaseConstructionCatalog.GetModule(placement.ModuleId),
                placement,
                BaseConstructionCatalog.GridSizeMeters);
            _baseConstructionModulesRoot.AddChild(node);
        }
        RefreshNpcNavigationObstacles();
    }

    private void RebuildPlanetaryPoiScene()
    {
        if (_planetaryPoisRoot is null ||
            _planetaryExplorationRuntime is null ||
            _planetaryPoiCatalog is null)
        {
            return;
        }

        _planetaryPoiNodes.Clear();
        foreach (Node child in _planetaryPoisRoot.GetChildren())
        {
            _planetaryPoisRoot.RemoveChild(child);
            child.QueueFree();
        }

        foreach (PlanetaryPoiRuntimeState state in PlanetaryExploration.States)
        {
            PlanetaryPoiNode node = new();
            node.Configure(state.Definition, state.Placement);
            _planetaryPoisRoot.AddChild(node);
            node.ApplyState(state.Discovered, state.Resolved);
            _planetaryPoiNodes.Add(node);
        }

        _planetaryPoiNodes.Sort((left, right) => string.Compare(
            left.InstanceId,
            right.InstanceId,
            StringComparison.Ordinal));
        RefreshNpcNavigationObstacles();
    }

    private void ApplyPlanetaryPoiStateToScene()
    {
        if (_planetaryExplorationRuntime is null)
        {
            return;
        }

        foreach (PlanetaryPoiNode node in _planetaryPoiNodes)
        {
            PlanetaryPoiRuntimeState state = PlanetaryExploration.GetState(
                node.InstanceId);
            node.ApplyState(state.Discovered, state.Resolved);
        }

        if (_discoveryCatalogOpen)
        {
            UpdateDiscoveryCatalogPanel();
        }
    }

    private void PulsePlanetaryScanner()
    {
        if (_player is null || _planetaryPoiNodes.Count == 0)
        {
            _status = "planetary scanner unavailable";
            return;
        }

        (PlanetaryPoiNode Node, float Distance)[] ordered = _planetaryPoiNodes
            .Select(node => (
                Node: node,
                Distance: _player.GlobalPosition.DistanceTo(
                    node.GlobalPosition)))
            .OrderBy(entry => entry.Distance)
            .ThenBy(entry => entry.Node.InstanceId, StringComparer.Ordinal)
            .ToArray();
        (PlanetaryPoiNode Node, float Distance)[] undiscoveredInRange = ordered
            .Where(entry =>
                !PlanetaryExploration.GetState(entry.Node.InstanceId).Discovered &&
                entry.Distance <= entry.Node.ScanRange)
            .ToArray();
        (PlanetaryPoiNode Node, float Distance) nearest =
            undiscoveredInRange.Length > 0
                ? undiscoveredInRange[0]
                : ordered[0];

        if (nearest.Distance > nearest.Node.ScanRange)
        {
            _status =
                $"scanner: nearest POI {nearest.Node.PoiTypeId} is " +
                $"{nearest.Distance:0.0}m away; range={nearest.Node.ScanRange:0.0}m";
            _lastDomainEvent =
                $"PoiScanOutOfRange({nearest.Node.InstanceId})";
            return;
        }

        RecordPlayerMultitoolUse(PlayerMultitoolFunction.Scanner, "planetary-poi");
        PlanetaryPoiScanResult result = PlanetaryExploration.Scan(
            nearest.Node.InstanceId,
            out string message);
        _status = $"scanner: {message}";
        _lastDomainEvent =
            $"PoiScanned({nearest.Node.InstanceId}, result={result})";
        ApplyPlanetaryPoiStateToScene();
        if (result == PlanetaryPoiScanResult.Discovered)
        {
            RecordProceduralQuestObjective(
                ProceduralQuestObjectiveType.ScanObject,
                nearest.Node.PoiTypeId,
                1,
                queueAutosave: false);
            RecordProceduralQuestObjective(
                ProceduralQuestObjectiveType.FindSignal,
                nearest.Node.PoiTypeId,
                1,
                queueAutosave: false);
            QueueCurrentSnapshot(AutosaveTrigger.DiscoveryChanged);
        }

        GD.Print(
            "TASK-108 player scanner PASS: " +
            $"instance={nearest.Node.InstanceId}; " +
            $"type={nearest.Node.PoiTypeId}; " +
            $"distance={nearest.Distance.ToString("0.0", CultureInfo.InvariantCulture)}; " +
            $"result={result}; " +
            $"discovered={PlanetaryExploration.DiscoveredCount}/" +
            $"{PlanetaryPoiCatalog.Definitions.Count}; " +
            $"resolved={PlanetaryExploration.ResolvedCount}; " +
            $"points={PlanetaryExploration.DiscoveryPoints}.");
    }

    public bool TryInteractPlanetaryPoi(
        PlanetaryPoiNode node,
        Node3D interactor)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(interactor);
        PlanetaryPoiInteractionResult result = PlanetaryExploration.Interact(
            node.InstanceId,
            out string message);
        _status = message;
        _lastDomainEvent =
            $"PoiInteraction({node.InstanceId}, result={result})";
        ApplyPlanetaryPoiStateToScene();
        if (result == PlanetaryPoiInteractionResult.Resolved)
        {
            RecordProceduralQuestObjective(
                ProceduralQuestObjectiveType.VisitLocation,
                node.PoiTypeId,
                1,
                queueAutosave: false);
            QueueCurrentSnapshot(AutosaveTrigger.DiscoveryChanged);
        }

        string line =
            "TASK-108 player POI interaction " +
            $"{(result == PlanetaryPoiInteractionResult.Resolved ? "PASS" : "BLOCKED")}: " +
            $"instance={node.InstanceId}; type={node.PoiTypeId}; " +
            $"result={result}; discovered={PlanetaryExploration.DiscoveredCount}; " +
            $"resolved={PlanetaryExploration.ResolvedCount}; " +
            $"points={PlanetaryExploration.DiscoveryPoints}; " +
            $"interactor={interactor.Name}.";
        if (result == PlanetaryPoiInteractionResult.Resolved ||
            result == PlanetaryPoiInteractionResult.AlreadyResolved)
        {
            GD.Print(line);
        }
        else
        {
            GD.PushWarning(line);
        }

        return result == PlanetaryPoiInteractionResult.Resolved;
    }

    private void OpenDiscoveryCatalog()
    {
        CloseRecipeSelector();
        CloseStationServices();
        CloseBaseBuildMode();
        CloseShipManagement();
        _discoveryCatalogOpen = true;
        _discoveryCatalogIndex = Math.Clamp(
            _discoveryCatalogIndex,
            0,
            Math.Max(0, PlanetaryExploration.States.Count - 1));
        _discoveryCatalogFeedback = string.Empty;
        if (_discoveryCatalogPanel is not null)
        {
            _discoveryCatalogPanel.Visible = true;
        }

        UpdateDiscoveryCatalogPanel();
        _status = "discovery catalog opened";
    }

    private void CloseDiscoveryCatalog(string status = "")
    {
        _discoveryCatalogOpen = false;
        _discoveryCatalogFeedback = string.Empty;
        if (_discoveryCatalogPanel is not null)
        {
            _discoveryCatalogPanel.Visible = false;
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            _status = status;
        }
    }

    private void MoveDiscoveryCatalogSelection(int delta)
    {
        int count = PlanetaryExploration.States.Count;
        if (count <= 0)
        {
            _discoveryCatalogIndex = 0;
            return;
        }

        _discoveryCatalogIndex = (_discoveryCatalogIndex + delta) % count;
        if (_discoveryCatalogIndex < 0)
        {
            _discoveryCatalogIndex += count;
        }

        _discoveryCatalogFeedback = string.Empty;
        UpdateDiscoveryCatalogPanel();
    }

    private void NameSelectedDiscovery()
    {
        IReadOnlyList<PlanetaryPoiRuntimeState> states =
            GetDiscoveryCatalogStates();
        if (states.Count == 0)
        {
            return;
        }

        PlanetaryPoiRuntimeState selected = states[Math.Clamp(
            _discoveryCatalogIndex,
            0,
            states.Count - 1)];
        string generatedName = $"Waypoint {_discoveryCatalogIndex + 1:00}";
        bool renamed = PlanetaryExploration.TryRename(
            selected.Placement.InstanceId,
            generatedName,
            out string message);
        _discoveryCatalogFeedback = message;
        _status = message;
        if (renamed)
        {
            QueueCurrentSnapshot(AutosaveTrigger.DiscoveryChanged);
            GD.Print(
                "TASK-108 player POI naming PASS: " +
                $"instance={selected.Placement.InstanceId}; " +
                $"name={generatedName}; named={PlanetaryExploration.NamedCount}.");
        }

        UpdateDiscoveryCatalogPanel();
    }

    private IReadOnlyList<PlanetaryPoiRuntimeState> GetDiscoveryCatalogStates()
    {
        return PlanetaryExploration.States
            .OrderBy(state => state.Definition.Category, StringComparer.Ordinal)
            .ThenBy(state => state.Definition.PoiTypeId, StringComparer.Ordinal)
            .ToArray();
    }

    private void UpdateDiscoveryCatalogPanel()
    {
        if (_discoveryCatalogLabel is null || !_discoveryCatalogOpen)
        {
            return;
        }

        IReadOnlyList<PlanetaryPoiRuntimeState> states =
            GetDiscoveryCatalogStates();
        if (states.Count == 0)
        {
            _discoveryCatalogLabel.Text = "DISCOVERY CATALOG\nNo POIs available.";
            return;
        }

        _discoveryCatalogIndex = Math.Clamp(
            _discoveryCatalogIndex,
            0,
            states.Count - 1);
        PlanetaryPoiRuntimeState selected = states[_discoveryCatalogIndex];
        List<string> lines = new()
        {
            "DISCOVERY CATALOG — planetary POIs",
            $"Discovered {PlanetaryExploration.DiscoveredCount}/{states.Count} | " +
            $"Resolved {PlanetaryExploration.ResolvedCount}/{states.Count} | " +
            $"Named {PlanetaryExploration.NamedCount} | " +
            $"Points {PlanetaryExploration.DiscoveryPoints}",
            "P scanner pulse | Up/Down select | N assign waypoint name | J/Esc close",
            string.Empty
        };
        int start = Math.Max(0, _discoveryCatalogIndex - 7);
        int end = Math.Min(states.Count, start + 15);
        start = Math.Max(0, end - 15);
        for (int index = start; index < end; index++)
        {
            PlanetaryPoiRuntimeState state = states[index];
            string marker = index == _discoveryCatalogIndex ? ">" : " ";
            string status = state.Resolved
                ? "RESOLVED"
                : state.Discovered ? "DISCOVERED" : "UNKNOWN";
            string name = state.Discovered
                ? PlanetaryExploration.DisplayName(state)
                : "unidentified signal";
            lines.Add(
                $"{marker} [{status,-10}] {name} | {state.Definition.Category}");
        }

        lines.Add(string.Empty);
        lines.Add(
            $"Selected: {(selected.Discovered ? PlanetaryExploration.DisplayName(selected) : "unknown")} | " +
            $"type={selected.Definition.PoiTypeId} | interaction={selected.Definition.InteractionKind}");
        lines.Add(
            $"Position: X={selected.Placement.PositionX:0.0} " +
            $"Z={selected.Placement.PositionZ:0.0} | " +
            $"scan={selected.Definition.ScanRange:0.0}m | rarity={selected.Definition.Rarity}");
        lines.Add(
            $"Environment: biome={selected.Placement.Environment.BiomeId} | " +
            $"slope={selected.Placement.Environment.SlopeDegrees:0.0}° | " +
            $"height={selected.Placement.Environment.Height:0.0} | " +
            $"water={selected.Placement.Environment.DistanceToWater:0.0}m | " +
            $"danger={selected.Placement.Environment.Danger}");
        if (!string.IsNullOrWhiteSpace(_discoveryCatalogFeedback))
        {
            lines.Add($"Result: {_discoveryCatalogFeedback}");
        }

        _discoveryCatalogLabel.Text = string.Join("\n", lines);
    }

    private bool CanStartCommand()
    {
        return _database is not null &&
            _autosave is not null &&
            _initializeTask is null &&
            _loadTask is null &&
            _resetTask is null &&
            _acceptanceTask is null &&
            _catalogResourceLifecycleAcceptanceTask is null &&
            _contentAcceptanceTask is null &&
            _craftingAcceptanceTask is null &&
            _craftTimeAcceptanceTask is null &&
            _thirdCraftingAcceptanceTask is null &&
            _fourthCraftingAcceptanceTask is null &&
            _catalogMatrixAcceptanceTask is null &&
            _technologySelectorAcceptanceTask is null &&
            _stationServicesAcceptanceTask is null &&
            _baseConstructionAcceptanceTask is null &&
            _planetaryExplorationAcceptanceTask is null &&
            _shipSystemsAcceptanceTask is null &&
            _stageOneVoyageAcceptanceTask is null &&
            _galaxyNavigationAcceptanceTask is null &&
            _ecologyAcceptanceTask is null &&
            _proceduralQuestAcceptanceTask is null &&
            _playerSurvivalAcceptanceTask is null &&
            _npcFactionAcceptanceTask is null &&
            _chemicalProcessAcceptanceTask is null &&
            _productionQueueAcceptanceTask is null &&
            _itemQualityDismantleAcceptanceTask is null &&
            _multiStationIndustryAcceptanceTask is null &&
            _productionNetworkHudAcceptanceTask is null &&
            _gracefulExitTask is null &&
            _selectorStation is null &&
            !_stationServicesOpen &&
            !_baseBuildMode &&
            !_discoveryCatalogOpen &&
            !_shipManagementOpen &&
            !_galaxyMapOpen &&
            !_ecologyCatalogOpen &&
            !_missionJournalOpen &&
            !_playerEquipmentOpen &&
            !_npcInteractionOpen &&
            !(_stageOneVoyageRuntime?.Piloted ?? false) &&
            (_gameplayProductionNetwork?.TotalJobs ?? 0) == 0 &&
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

        BeginPlanetaryExplorationAcceptance();
    }

    private void BeginPlanetaryExplorationAcceptance()
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
            "save_1.planetary-exploration-test.db");
        _state = SalvageRepairSliceState.Testing;
        _status = "TASK-080/TASK-108 catalog and exploration acceptance running";
        _planetaryExplorationAcceptanceHud = "RUNNING";
        _planetaryExplorationAcceptanceReport = null;
        _planetaryExplorationAcceptanceTask =
            PlanetaryExplorationAcceptanceRunner.RunAsync(
                testPath,
                SlotId,
                ContentCatalog,
                PlanetaryPoiCatalog,
                RepairRecipe,
                _lifetimeCancellation.Token);
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
        ResourceNodeBinding[] bindings = BuildResourceBindings().Values
            .OrderBy(binding => binding.ResourceNodeId, StringComparer.Ordinal)
            .ToArray();
        string verticalSliceTestPath = Path.Combine(
            directory,
            "save_1.vertical-slice-test.db");
        string resourceLifecycleTestPath = Path.Combine(
            directory,
            "save_1.resource-lifecycle-test.db");
        _state = SalvageRepairSliceState.Testing;
        _status = "TASK-062/TASK-100 resource acceptance running";
        _acceptanceHud = "RUNNING";
        _catalogResourceLifecycleAcceptanceHud = "RUNNING";
        _acceptanceReport = null;
        _catalogResourceLifecycleAcceptanceReport = null;
        _acceptanceTask = VerticalSliceAcceptanceRunner.RunAsync(
            verticalSliceTestPath,
            SlotId,
            RepairRecipe,
            bindings,
            _lifetimeCancellation.Token);
        _catalogResourceLifecycleAcceptanceTask =
            CatalogResourceLifecycleAcceptanceRunner.RunAsync(
                resourceLifecycleTestPath,
                SlotId,
                ContentCatalog,
                RepairRecipe,
                StationRecipes,
                bindings,
                _generatedResourcePlacements,
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
        string baseConstructionTestPath = Path.Combine(
            directory,
            "save_1.base-construction-test.db");
        _state = SalvageRepairSliceState.Testing;
        _status = "TASK-072/TASK-106 acceptance running";
        _fourthCraftingAcceptanceHud = "RUNNING";
        _baseConstructionAcceptanceHud = "RUNNING";
        _fourthCraftingAcceptanceReport = null;
        _baseConstructionAcceptanceReport = null;
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
        _baseConstructionAcceptanceTask =
            BaseConstructionAcceptanceRunner.RunAsync(
                baseConstructionTestPath,
                SlotId,
                ContentCatalog,
                BaseConstructionCatalog,
                RepairRecipe,
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
        _status = "TASK-090/TASK-092/TASK-093/TASK-096/TASK-098 acceptance running";
        _productionQueueAcceptanceHud = "RUNNING";
        _queueTerminalAcceptanceHud = "RUNNING";
        _itemQualityDismantleAcceptanceHud = "RUNNING";
        _multiStationIndustryAcceptanceHud = "RUNNING";
        _productionNetworkHudAcceptanceHud = "RUNNING";
        _productionQueueAcceptanceReport = null;
        _itemQualityDismantleAcceptanceReport = null;
        _multiStationIndustryAcceptanceReport = null;
        _productionNetworkHudAcceptanceReport = null;
        _productionQueueAcceptanceTask =
            ProductionQueueAcceptanceRunner.RunAsync(
                testPath,
                SlotId,
                ContentCatalog,
                _lifetimeCancellation.Token);
        string propertiesTestPath = Path.Combine(
            directory,
            "save_1.item-properties-dismantle-test.db");
        _itemQualityDismantleAcceptanceTask =
            ItemQualityDismantleAcceptanceRunner.RunAsync(
                propertiesTestPath,
                SlotId,
                ContentCatalog,
                _lifetimeCancellation.Token);
        string multiStationTestPath = Path.Combine(
            directory,
            "save_1.multi-station-industry-test.db");
        _multiStationIndustryAcceptanceTask =
            MultiStationIndustryAcceptanceRunner.RunAsync(
                multiStationTestPath,
                SlotId,
                ContentCatalog,
                _lifetimeCancellation.Token);
        string productionNetworkHudTestPath = Path.Combine(
            directory,
            "save_1.production-network-hud-test.db");
        _productionNetworkHudAcceptanceTask =
            ProductionNetworkHudAcceptanceRunner.RunAsync(
                productionNetworkHudTestPath,
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
        string servicesTestPath = Path.Combine(
            directory,
            "save_1.station-services-test.db");
        _stationServicesAcceptanceHud = "RUNNING";
        _stationServicesAcceptanceReport = null;
        _stationServicesAcceptanceTask =
            StationServicesAcceptanceRunner.RunAsync(
                servicesTestPath,
                SlotId,
                ContentCatalog,
                StationServiceCatalog,
                RepairRecipe,
                _lifetimeCancellation.Token);
        _status =
            "TASK-082/TASK-102 research and station services acceptance running";
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
        string shipSystemsTestPath = Path.Combine(
            directory,
            "save_1.ship-systems-test.db");
        _shipSystemsAcceptanceHud = "RUNNING";
        _shipSystemsAcceptanceReport = null;
        _shipSystemsAcceptanceTask = ShipSystemsAcceptanceRunner.RunAsync(
            shipSystemsTestPath,
            SlotId,
            ContentCatalog,
            ShipSystemsCatalog,
            RepairRecipe,
            _lifetimeCancellation.Token);
        BeginStageOneVoyageAcceptance(directory);
        BeginGalaxyNavigationAcceptance(directory);
        BeginEcologyAcceptance(directory);
        BeginProceduralQuestAcceptance(directory);
        BeginPlayerSurvivalAcceptance(directory);
        BeginNpcFactionAcceptance(directory);
        BeginNpcNavigationAcceptance();
        _status =
            "TASK-076/TASK-110/TASK-112/TASK-114/TASK-116/TASK-118/TASK-120/TASK-122/TASK-124 runtime, ship systems, voyage, galaxy navigation, ecology, quests, survival, NPC/factions and navigation acceptance running";
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
            technologyProgress: TechnologyProgress.ToSaveData(),
            productionQueue: null,
            productionQueueNetwork:
                _gameplayProductionNetwork?.CreateSaveData(),
            stationServices: StationServices.CreateSaveData(),
            baseConstruction: BaseConstruction.CreateSaveData(),
            planetaryExploration: PlanetaryExploration.CreateSaveData(),
            shipSystems: ShipSystems.CreateSaveData(),
            stageOneVoyage: StageOneVoyage.CreateSaveData(),
            galaxyNavigation: GalaxyNavigation.CreateSaveData(),
            ecology: Ecology.CreateSaveData(),
            proceduralQuests: ProceduralQuests.CreateSaveData(),
            playerSurvival: PlayerSurvival.CreateSaveData(),
            npcFactions: NpcFactions.CreateSaveData());
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
        CloseStationServices();
        CloseBaseBuildMode();
        CloseDiscoveryCatalog();
        CloseShipManagement();
        CloseGalaxyMap();
        CloseEcologyCatalog();
        CloseMissionJournal();
        ClosePlayerEquipment();
        CloseNpcInteraction();
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
            _stationServicesAcceptanceTask is not null ||
            _baseConstructionAcceptanceTask is not null ||
            _planetaryExplorationAcceptanceTask is not null ||
            _shipSystemsAcceptanceTask is not null ||
            _stageOneVoyageAcceptanceTask is not null ||
            _galaxyNavigationAcceptanceTask is not null ||
            _ecologyAcceptanceTask is not null ||
            _proceduralQuestAcceptanceTask is not null ||
            _playerSurvivalAcceptanceTask is not null ||
            _npcFactionAcceptanceTask is not null ||
            _chemicalProcessAcceptanceTask is not null ||
            _productionQueueAcceptanceTask is not null ||
            _itemQualityDismantleAcceptanceTask is not null ||
            _multiStationIndustryAcceptanceTask is not null ||
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
            technologyProgress: TechnologyProgress.ToSaveData(),
            productionQueue: null,
            productionQueueNetwork:
                _gameplayProductionNetwork?.CreateSaveData(),
            stationServices: StationServices.CreateSaveData(),
            baseConstruction: BaseConstruction.CreateSaveData(),
            planetaryExploration: PlanetaryExploration.CreateSaveData(),
            shipSystems: ShipSystems.CreateSaveData(),
            stageOneVoyage: StageOneVoyage.CreateSaveData(),
            galaxyNavigation: GalaxyNavigation.CreateSaveData(),
            ecology: Ecology.CreateSaveData(),
            proceduralQuests: ProceduralQuests.CreateSaveData(),
            playerSurvival: PlayerSurvival.CreateSaveData(),
            npcFactions: NpcFactions.CreateSaveData());
        _state = SalvageRepairSliceState.Exiting;
        _status = $"graceful-exit flush rev={snapshot.Revision}";
        GD.Print(
            "Vertical slice graceful-exit flush started: " +
            $"revision={snapshot.Revision}; " +
            $"salvage={Session.SalvageQuantity}; " +
            $"shipRepaired={(Session.ShipRepaired ? 1 : 0)}; " +
            $"crafted={CountCraftedStationRecipes()}/{ObjectiveRecipes.Count}; " +
            $"researchPoints={TechnologyProgress.ResearchPoints}; " +
            $"unlockedTech={TechnologyProgress.UnlockedCount}; " +
            $"queueStations={_gameplayProductionNetwork?.StationIds.Count ?? 0}; " +
            $"queueJobs={_gameplayProductionNetwork?.TotalJobs ?? 0}; " +
            $"queueEnergy={(_gameplayProductionNetwork is null ? "n/a" :
                _gameplayProductionNetwork.Queues.Sum(queue => queue.EnergyRemaining)
                    .ToString("0.###", CultureInfo.InvariantCulture))}; " +
            $"baseModules={BaseConstruction.ModuleCount}; " +
            $"basePower={BaseConstruction.Power.Generation.ToString("0.###", CultureInfo.InvariantCulture)}/" +
            $"{BaseConstruction.Power.Consumption.ToString("0.###", CultureInfo.InvariantCulture)}; " +
            $"discoveries={PlanetaryExploration.DiscoveredCount}/" +
            $"{PlanetaryPoiCatalog.Definitions.Count}; " +
            $"resolvedPois={PlanetaryExploration.ResolvedCount}; " +
            $"shipClass={ShipSystems.ShipClassId}; " +
            $"shipModules={ShipSystems.InstalledModuleCount}; " +
            $"shipFuel={ShipSystems.Fuel.ToString("0.###", CultureInfo.InvariantCulture)}; " +
            $"commissioned={(ShipSystems.Commissioned ? 1 : 0)}; " +
            $"flightReady={(ShipSystems.FlightReady ? 1 : 0)}; " +
            $"hyperReady={(ShipSystems.HyperspaceReady ? 1 : 0)}; " +
            $"voyageLocation={StageOneVoyage.Location}; " +
            $"voyagePiloted={(StageOneVoyage.Piloted ? 1 : 0)}; " +
            $"voyageLoops={StageOneVoyage.CompletedLoops}; " +
            $"galaxySystem={GalaxyNavigation.CurrentSystem.SystemId}; " +
            $"galaxyVisited={GalaxyNavigation.VisitedSystemIds.Count}; " +
            $"hyperJumps={GalaxyNavigation.JumpCount}; " +
            $"ecologyFlora={Ecology.DiscoveredFloraCount}; " +
            $"ecologyFauna={Ecology.DiscoveredFaunaCount}; " +
            $"ecologyRemoved={Ecology.RemovedFloraCount}; " +
            $"missionsActive={ProceduralQuests.AcceptedCount}; " +
            $"missionsReady={ProceduralQuests.ReadyCount}; " +
            $"missionsCompleted={ProceduralQuests.CompletedCount}.");
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
            InitializeGameplayProductionNetwork(
                snapshot?.ProductionQueueNetwork,
                snapshot?.ProductionQueue);
            _stationServicesRuntime = new StationServicesRuntime(
                ContentCatalog,
                StationServiceCatalog,
                StationServicesAcceptanceRunner.NpcId,
                snapshot?.StationServices);
            _baseConstructionRuntime = new BaseConstructionRuntime(
                BaseConstructionCatalog,
                snapshot?.BaseConstruction);
            _planetaryExplorationRuntime = new PlanetaryExplorationRuntime(
                PlanetaryPoiCatalog,
                _planetaryPoiPlacements,
                snapshot?.PlanetaryExploration);
            _shipSystemsRuntime = new ShipSystemsRuntime(
                ShipSystemsCatalog,
                snapshot?.ShipSystems,
                commissioned: Session.ShipRepaired);
            InitializeStageOneVoyageRuntime(snapshot?.StageOneVoyage);
            InitializeGalaxyNavigationRuntime(snapshot?.GalaxyNavigation);
            InitializeEcologyRuntime(snapshot?.Ecology);
            InitializeNpcFactionRuntime(snapshot?.NpcFactions);
            InitializeProceduralQuestRuntime(snapshot?.ProceduralQuests);
            InitializePlayerSurvivalRuntime(snapshot?.PlayerSurvival);
            _revision = snapshot?.Revision ?? 0;
            if (snapshot is not null && _player is not null &&
                !StageOneVoyage.Piloted)
            {
                _player.GlobalPosition = new Vector3(
                    (float)snapshot.Player.PositionX,
                    (float)snapshot.Player.PositionY,
                    (float)snapshot.Player.PositionZ);
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
            CloseNpcInteraction();
            RebuildBaseConstructionScene();
            RebuildNpcFactionScene();
            ApplyPlanetaryPoiStateToScene();
            _craftTimer.Reset();
            _activeCraftingStation = null;
            _craftingInteractorName = "unknown";
            ApplySessionToScene();
            ApplyStageOneVoyageToScene();
            _state = SalvageRepairSliceState.Ready;
            _status = snapshot is null
                ? "new starter repair objective"
                : $"restored revision {_revision}";
            GD.Print(
                "TASK-062 vertical slice READY: " +
                $"revision={_revision}; " +
                $"salvage={Session.SalvageQuantity}; " +
                $"shipRepaired={(Session.ShipRepaired ? 1 : 0)}; " +
                $"crafted={CountCraftedStationRecipes()}/{ObjectiveRecipes.Count}; " +
                $"researchPoints={TechnologyProgress.ResearchPoints}; " +
                $"unlockedTech={TechnologyProgress.UnlockedCount}.");
            GD.Print(
                "TASK-102 station services restore PASS: " +
                $"credits={StationServices.PlayerCredits}; " +
                $"merchantCredits={StationServices.MerchantCredits}; " +
                $"reputation={StationServices.Reputation}; " +
                $"completedQuests={StationServices.CompletedQuestCount}; " +
                $"activeQuests={StationServices.ActiveQuestCount}; " +
                $"legacyFallback={(snapshot?.StationServices is null ? 1 : 0)}.");
            GD.Print(
                "TASK-106 base construction restore PASS: " +
                $"modules={BaseConstruction.ModuleCount}; " +
                $"stock={BaseConstructionCatalog.Modules.Values.Sum(module => BaseConstruction.GetStock(module.ModuleId))}; " +
                $"components={BaseConstruction.Power.ConnectedComponents}; " +
                $"generation={BaseConstruction.Power.Generation.ToString("0.###", CultureInfo.InvariantCulture)}; " +
                $"consumption={BaseConstruction.Power.Consumption.ToString("0.###", CultureInfo.InvariantCulture)}; " +
                $"battery={BaseConstruction.StoredEnergy.ToString("0.###", CultureInfo.InvariantCulture)}; " +
                $"legacyFallback={(snapshot?.BaseConstruction is null ? 1 : 0)}.");
            GD.Print(
                "TASK-108 planetary exploration restore PASS: " +
                $"discovered={PlanetaryExploration.DiscoveredCount}; " +
                $"resolved={PlanetaryExploration.ResolvedCount}; " +
                $"named={PlanetaryExploration.NamedCount}; " +
                $"points={PlanetaryExploration.DiscoveryPoints}; " +
                $"legacyFallback={(snapshot?.PlanetaryExploration is null ? 1 : 0)}.");
            GD.Print(
                "TASK-110 ship systems restore PASS: " +
                $"class={ShipSystems.ShipClassId}; " +
                $"modules={ShipSystems.InstalledModuleCount}; " +
                $"systems={ShipSystems.SystemHealth.Count}; " +
                $"offline={ShipSystems.DisabledSystemCount}; " +
                $"fuel={ShipSystems.Fuel.ToString("0.###", CultureInfo.InvariantCulture)}; " +
                $"commissioned={(ShipSystems.Commissioned ? 1 : 0)}; " +
                $"flightReady={(ShipSystems.FlightReady ? 1 : 0)}; " +
                $"hyperReady={(ShipSystems.HyperspaceReady ? 1 : 0)}; " +
                $"legacyFallback={(snapshot?.ShipSystems is null ? 1 : 0)}.");
            GD.Print(
                "TASK-112 voyage restore PASS: " +
                $"location={StageOneVoyage.Location}; " +
                $"piloted={(StageOneVoyage.Piloted ? 1 : 0)}; " +
                $"stationVisited={(StageOneVoyage.StationVisited ? 1 : 0)}; " +
                $"takeoffs={StageOneVoyage.TakeoffCount}; " +
                $"dockings={StageOneVoyage.DockingCount}; " +
                $"landings={StageOneVoyage.LandingCount}; " +
                $"loops={StageOneVoyage.CompletedLoops}; " +
                $"legacyFallback={(snapshot?.StageOneVoyage is null ? 1 : 0)}.");
            GD.Print(
                "TASK-114 galaxy navigation restore PASS: " +
                $"galaxy={GalaxyNavigation.CurrentSystem.GalaxyId}; " +
                $"system={GalaxyNavigation.CurrentSystem.SystemId}; " +
                $"sector={GalaxyNavigation.CurrentSystem.SectorX}," +
                $"{GalaxyNavigation.CurrentSystem.SectorY}," +
                $"{GalaxyNavigation.CurrentSystem.SectorZ}; " +
                $"star={GalaxyNavigation.CurrentSystem.StarType}; " +
                $"planets={GalaxyNavigation.CurrentSystem.Planets.Count}; " +
                $"visited={GalaxyNavigation.VisitedSystemIds.Count}; " +
                $"jumps={GalaxyNavigation.JumpCount}; " +
                $"distance={GalaxyNavigation.TotalDistanceLightYears.ToString("0.0", CultureInfo.InvariantCulture)}ly; " +
                $"legacyFallback={(snapshot?.GalaxyNavigation is null ? 1 : 0)}.");
            GD.Print(
                "TASK-116 ecology restore PASS: " +
                $"flora={Ecology.DiscoveredFloraCount}; " +
                $"fauna={Ecology.DiscoveredFaunaCount}; " +
                $"removed={Ecology.RemovedFloraCount}; " +
                $"points={Ecology.DiscoveryPoints}; " +
                $"active/simplified={EcologyPlan.ActiveFauna.Count}/{EcologyPlan.SimplifiedFauna.Count}; " +
                $"legacyFallback={(snapshot?.Ecology is null ? 1 : 0)}.");
            GD.Print(
                "TASK-118 procedural quests restore PASS: " +
                $"board={ProceduralQuests.Board.Count}; " +
                $"active={ProceduralQuests.AcceptedCount}; " +
                $"ready={ProceduralQuests.ReadyCount}; " +
                $"completed={ProceduralQuests.CompletedCount}; " +
                $"legacyFallback={(snapshot?.ProceduralQuests is null ? 1 : 0)}.");
            GD.Print(
                "TASK-120 player survival restore PASS: " +
                $"health={PlayerSurvival.Health:0.#}; shield={PlayerSurvival.Shield:0.#}; " +
                $"oxygen={PlayerSurvival.Oxygen:0.#}; hazard={PlayerSurvival.HazardProtection:0.#}; " +
                $"suit={PlayerSurvival.InstalledSuitModules.Count}; " +
                $"multitool={PlayerSurvival.InstalledMultitoolModules.Count}; " +
                $"mode={PlayerSurvival.ActiveMultitoolFunction}; " +
                $"legacyFallback={(snapshot?.PlayerSurvival is null ? 1 : 0)}.");
            GD.Print(
                "TASK-122 NPC/factions restore PASS: " +
                $"agents={NpcFactions.AliveCount}/{NpcFactions.AgentCount}; " +
                $"defeats={NpcFactions.TotalOpponentDefeats}; " +
                $"repDeltas={NpcFactions.CreateSaveData().Reputations.Count}; " +
                $"agentDeltas={NpcFactions.CreateSaveData().Agents.Count}; " +
                $"legacyFallback={(snapshot?.NpcFactions is null ? 1 : 0)}.");
            IReadOnlyList<ProductionQueueSaveData> restoredQueues =
                snapshot?.ProductionQueueNetwork?.Stations ??
                (snapshot?.ProductionQueue is null
                    ? Array.Empty<ProductionQueueSaveData>()
                    : new[] { snapshot.ProductionQueue! });
            ProductionQueueSaveData[] nonEmptyQueues = restoredQueues
                .Where(queue => queue.Jobs.Count > 0)
                .OrderBy(queue => queue.StationId, StringComparer.Ordinal)
                .ToArray();
            if (nonEmptyQueues.Length > 0)
            {
                ProductionQueueJobSaveData firstJob = nonEmptyQueues
                    .SelectMany(queue => queue.Jobs)
                    .OrderBy(job => job.JobSequence)
                    .First();
                GD.Print(
                    "TASK-092 player queue restore PASS: " +
                    $"stations={nonEmptyQueues.Length}; " +
                    $"jobs={nonEmptyQueues.Sum(queue => queue.Jobs.Count)}; " +
                    $"running={nonEmptyQueues.Sum(queue => queue.Jobs.Count(job => job.Status == ProductionQueueJobStatus.Running))}; " +
                    $"queued={nonEmptyQueues.Sum(queue => queue.Jobs.Count(job => job.Status == ProductionQueueJobStatus.Queued))}; " +
                    $"paused={nonEmptyQueues.Sum(queue => queue.Jobs.Count(job => job.Status == ProductionQueueJobStatus.Paused))}; " +
                    $"firstJob={firstJob.JobId}; " +
                    $"elapsed={firstJob.ElapsedSeconds.ToString("0.###", CultureInfo.InvariantCulture)}; " +
                    "offlineProgress=0.");
            }
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
            InitializeGameplayProductionNetwork(
                saveData: null,
                legacySaveData: null);
            _stationServicesRuntime = new StationServicesRuntime(
                ContentCatalog,
                StationServiceCatalog,
                StationServicesAcceptanceRunner.NpcId);
            _baseConstructionRuntime = new BaseConstructionRuntime(
                BaseConstructionCatalog);
            _planetaryExplorationRuntime = new PlanetaryExplorationRuntime(
                PlanetaryPoiCatalog,
                _planetaryPoiPlacements);
            _shipSystemsRuntime = new ShipSystemsRuntime(ShipSystemsCatalog);
            InitializeStageOneVoyageRuntime(saveData: null);
            InitializeGalaxyNavigationRuntime(saveData: null);
            InitializeEcologyRuntime(saveData: null);
            InitializeNpcFactionRuntime(saveData: null);
            InitializeProceduralQuestRuntime(saveData: null);
            InitializePlayerSurvivalRuntime(saveData: null);
            _revision = 0;
            _autosaveElapsedSeconds = 0.0;
            CloseRecipeSelector();
            CloseStationServices();
            CloseBaseBuildMode();
            CloseDiscoveryCatalog();
            CloseShipManagement();
            CloseGalaxyMap();
            CloseEcologyCatalog();
            CloseMissionJournal();
            ClosePlayerEquipment();
            CloseNpcInteraction();
            RebuildBaseConstructionScene();
            RebuildNpcFactionScene();
            ApplyPlanetaryPoiStateToScene();
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
            ApplyStageOneVoyageToScene();
            _state = SalvageRepairSliceState.Ready;
            _status =
                $"slot reset; collect {Session.RequiredSalvage} x " +
                Session.SalvageDefinitionId;
            GD.Print("TASK-062 vertical slice slot reset PASS.");
            GD.Print(
                "TASK-110 ship systems reset PASS: " +
                $"class={ShipSystems.ShipClassId}; " +
                $"modules={ShipSystems.InstalledModuleCount}; " +
                $"systems={ShipSystems.SystemHealth.Count}; " +
                $"offline={ShipSystems.DisabledSystemCount}; " +
                $"fuel={ShipSystems.Fuel.ToString("0.###", CultureInfo.InvariantCulture)}; " +
                $"commissioned={(ShipSystems.Commissioned ? 1 : 0)}; " +
                $"flightReady={(ShipSystems.FlightReady ? 1 : 0)}; " +
                $"hyperReady={(ShipSystems.HyperspaceReady ? 1 : 0)}.");
            GD.Print(
                "TASK-112 voyage reset PASS: " +
                $"location={StageOneVoyage.Location}; " +
                $"piloted={(StageOneVoyage.Piloted ? 1 : 0)}; " +
                $"stationVisited={(StageOneVoyage.StationVisited ? 1 : 0)}; " +
                $"takeoffs={StageOneVoyage.TakeoffCount}; " +
                $"dockings={StageOneVoyage.DockingCount}; " +
                $"landings={StageOneVoyage.LandingCount}; " +
                $"loops={StageOneVoyage.CompletedLoops}.");
            GD.Print(
                "TASK-114 galaxy navigation reset PASS: " +
                $"galaxy={GalaxyNavigation.CurrentSystem.GalaxyId}; " +
                $"system={GalaxyNavigation.CurrentSystem.SystemId}; " +
                $"sector={GalaxyNavigation.CurrentSystem.SectorX}," +
                $"{GalaxyNavigation.CurrentSystem.SectorY}," +
                $"{GalaxyNavigation.CurrentSystem.SectorZ}; " +
                $"visited={GalaxyNavigation.VisitedSystemIds.Count}; " +
                $"jumps={GalaxyNavigation.JumpCount}; " +
                $"distance={GalaxyNavigation.TotalDistanceLightYears.ToString("0.0", CultureInfo.InvariantCulture)}ly.");
            GD.Print(
                "TASK-116 ecology reset PASS: " +
                $"flora={Ecology.DiscoveredFloraCount}; " +
                $"fauna={Ecology.DiscoveredFaunaCount}; " +
                $"removed={Ecology.RemovedFloraCount}; " +
                $"points={Ecology.DiscoveryPoints}; " +
                $"regenerated={EcologyPlan.Flora.Count}; " +
                $"active/simplified={EcologyPlan.ActiveFauna.Count}/{EcologyPlan.SimplifiedFauna.Count}.");
            GD.Print(
                "TASK-118 procedural quests reset PASS: " +
                $"board={ProceduralQuests.Board.Count}; active={ProceduralQuests.AcceptedCount}; " +
                $"ready={ProceduralQuests.ReadyCount}; completed={ProceduralQuests.CompletedCount}; " +
                $"seed={ProceduralQuestCatalog.WorldSeed}.");
            GD.Print(
                "TASK-120 player survival reset PASS: " +
                $"health={PlayerSurvival.Health:0.#}; shield={PlayerSurvival.Shield:0.#}; " +
                $"stamina={PlayerSurvival.Stamina:0.#}; oxygen={PlayerSurvival.Oxygen:0.#}; " +
                $"suit={PlayerSurvival.InstalledSuitModules.Count}; " +
                $"multitool={PlayerSurvival.InstalledMultitoolModules.Count}; mode={PlayerSurvival.ActiveMultitoolFunction}.");
            GD.Print(
                "TASK-122 NPC/factions reset PASS: " +
                $"agents={NpcFactions.AliveCount}/{NpcFactions.AgentCount}; " +
                $"defeats={NpcFactions.TotalOpponentDefeats}; " +
                $"repDeltas={NpcFactions.CreateSaveData().Reputations.Count}; agentDeltas={NpcFactions.CreateSaveData().Agents.Count}.");
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

            UpdateCombinedResourceAcceptanceState();
        }
        catch (Exception exception)
        {
            Fail("acceptance", exception);
        }
    }

    private void PollCatalogResourceLifecycleAcceptanceTask()
    {
        if (_catalogResourceLifecycleAcceptanceTask is null ||
            !_catalogResourceLifecycleAcceptanceTask.IsCompleted)
        {
            return;
        }

        Task<CatalogResourceLifecycleAcceptanceReport> task =
            _catalogResourceLifecycleAcceptanceTask;
        _catalogResourceLifecycleAcceptanceTask = null;
        try
        {
            _catalogResourceLifecycleAcceptanceReport =
                task.GetAwaiter().GetResult();
            CatalogResourceLifecycleAcceptanceReport report =
                _catalogResourceLifecycleAcceptanceReport;
            _catalogResourceLifecycleAcceptanceHud = report.Passed
                ? $"PASS catalog={report.CatalogResources}, " +
                  $"physical={report.PhysicalResourceTypes}, " +
                  $"nodes={report.ResourceNodes}, " +
                  $"generated={report.GeneratedNodes}, " +
                  $"collectTypes={report.CollectedResourceTypes}, " +
                  $"collectNodes={report.CollectedResourceNodes}, " +
                  $"duplicate={(report.DuplicateRejected ? 1 : 0)}, " +
                  $"mirrors={(report.InventoryMirrorsSynchronized ? 1 : 0)}, " +
                  $"depletion={(report.DepletionPersisted ? 1 : 0)}, " +
                  $"restore={(report.ColdRestoreExact ? 1 : 0)}, " +
                  $"reset={(report.ResetReady ? 1 : 0)}, " +
                  $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}"
                : $"FAIL {report.Result}";
            _status = report.Result;
            string output = BuildCatalogResourceLifecycleAcceptanceOutput(
                report);
            if (report.Passed)
            {
                GD.Print(output);
            }
            else
            {
                GD.PushError(output);
            }

            UpdateCombinedResourceAcceptanceState();
        }
        catch (Exception exception)
        {
            Fail("catalog resource lifecycle acceptance", exception);
        }
    }

    private void UpdateCombinedResourceAcceptanceState()
    {
        if (_acceptanceTask is not null ||
            _catalogResourceLifecycleAcceptanceTask is not null ||
            _acceptanceReport is null ||
            _catalogResourceLifecycleAcceptanceReport is null)
        {
            return;
        }

        bool passed = _acceptanceReport.Passed &&
            _catalogResourceLifecycleAcceptanceReport.Passed;
        _state = passed
            ? SalvageRepairSliceState.Passed
            : SalvageRepairSliceState.Failed;
        _status = passed
            ? "TASK-062/TASK-100 resource acceptance passed"
            : "TASK-062/TASK-100 resource acceptance failed";
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

    private void PollPlanetaryExplorationAcceptanceTask()
    {
        if (_planetaryExplorationAcceptanceTask is null ||
            !_planetaryExplorationAcceptanceTask.IsCompleted)
        {
            return;
        }

        Task<PlanetaryExplorationAcceptanceReport> task =
            _planetaryExplorationAcceptanceTask;
        _planetaryExplorationAcceptanceTask = null;
        try
        {
            _planetaryExplorationAcceptanceReport =
                task.GetAwaiter().GetResult();
            PlanetaryExplorationAcceptanceReport report =
                _planetaryExplorationAcceptanceReport;
            _planetaryExplorationAcceptanceHud = report.Passed
                ? $"PASS types={report.PoiTypes}, " +
                  $"placements={report.Placements}, " +
                  $"deterministic={(report.Deterministic ? 1 : 0)}, " +
                  $"constraints={(report.Constraints ? 1 : 0)}, " +
                  $"spacing={(report.Spacing ? 1 : 0)}, " +
                  $"questBias={(report.QuestBias ? 1 : 0)}, " +
                  $"clearance={(report.InfrastructureClearance ? 1 : 0)}, " +
                  $"scan={(report.ScanAll ? 1 : 0)}, " +
                  $"resolve={(report.ResolveAll ? 1 : 0)}, " +
                  $"naming={(report.Naming ? 1 : 0)}, " +
                  $"restore={(report.ColdRestore ? 1 : 0)}, " +
                  $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}"
                : $"FAIL {report.Result}";
            bool industryPassed =
                _industryCatalogAcceptanceHud.StartsWith(
                    "PASS",
                    StringComparison.Ordinal);
            _state = report.Passed && industryPassed
                ? SalvageRepairSliceState.Passed
                : SalvageRepairSliceState.Failed;
            _status = report.Result;
            string output =
                "TASK-108 planetary exploration acceptance " +
                $"{(report.Passed ? "PASS" : "FAIL")}: " +
                $"poiTypes={report.PoiTypes}; " +
                $"placements={report.Placements}; " +
                $"deterministic={(report.Deterministic ? 1 : 0)}; " +
                $"constraints={(report.Constraints ? 1 : 0)}; " +
                $"spacing={(report.Spacing ? 1 : 0)}; " +
                $"questBias={(report.QuestBias ? 1 : 0)}; " +
                $"infrastructureClearance={(report.InfrastructureClearance ? 1 : 0)}; " +
                $"scanAll={(report.ScanAll ? 1 : 0)}; " +
                $"resolveAll={(report.ResolveAll ? 1 : 0)}; " +
                $"naming={(report.Naming ? 1 : 0)}; " +
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
                GD.Print(output);
            }
            else
            {
                GD.PushError(output);
            }
        }
        catch (Exception exception)
        {
            Fail("planetary exploration acceptance", exception);
        }
    }

    private void PollBaseConstructionAcceptanceTask()
    {
        if (_baseConstructionAcceptanceTask is null ||
            !_baseConstructionAcceptanceTask.IsCompleted)
        {
            return;
        }

        Task<BaseConstructionAcceptanceReport> task =
            _baseConstructionAcceptanceTask;
        _baseConstructionAcceptanceTask = null;
        try
        {
            _baseConstructionAcceptanceReport = task.GetAwaiter().GetResult();
            BaseConstructionAcceptanceReport report =
                _baseConstructionAcceptanceReport;
            _baseConstructionAcceptanceHud = report.Passed
                ? $"PASS modules={report.CatalogModules}, " +
                  $"placed={report.PlacedModules}, " +
                  $"snap={(report.Snapping ? 1 : 0)}, " +
                  $"collision={(report.CollisionRejected ? 1 : 0)}, " +
                  $"power={(report.PowerGraph ? 1 : 0)}, " +
                  $"limits={(report.Limits ? 1 : 0)}, " +
                  $"stress500={(report.Stress500 ? 1 : 0)}, " +
                  $"restore={(report.ColdRestore ? 1 : 0)}, " +
                  $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}"
                : $"FAIL {report.Result}";
            _state = report.Passed
                ? SalvageRepairSliceState.Passed
                : SalvageRepairSliceState.Failed;
            _status = report.Result;
            string output =
                "TASK-106 base construction acceptance " +
                $"{(report.Passed ? "PASS" : "FAIL")}: " +
                $"catalogModules={report.CatalogModules}; " +
                $"categories={report.Categories}; " +
                $"placed={report.PlacedModules}; " +
                $"anchor={(report.AnchorRule ? 1 : 0)}; " +
                $"snapping={(report.Snapping ? 1 : 0)}; " +
                $"collisionRejected={(report.CollisionRejected ? 1 : 0)}; " +
                $"disconnectedRejected={(report.DisconnectedRejected ? 1 : 0)}; " +
                $"powerGraph={(report.PowerGraph ? 1 : 0)}; " +
                $"battery={(report.Battery ? 1 : 0)}; " +
                $"toggle={(report.Toggle ? 1 : 0)}; " +
                $"removalRefund={(report.RemovalRefund ? 1 : 0)}; " +
                $"limits={(report.Limits ? 1 : 0)}; " +
                $"stress500={(report.Stress500 ? 1 : 0)}; " +
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
                GD.Print(output);
            }
            else
            {
                GD.PushError(output);
            }
        }
        catch (Exception exception)
        {
            Fail("base construction acceptance", exception);
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

            UpdateCombinedCatalogAndShipAcceptanceState();
        }
        catch (Exception exception)
        {
            Fail("catalog crafting matrix acceptance", exception);
        }
    }

    private void PollShipSystemsAcceptanceTask()
    {
        if (_shipSystemsAcceptanceTask is null ||
            !_shipSystemsAcceptanceTask.IsCompleted)
        {
            return;
        }

        Task<ShipSystemsAcceptanceReport> task = _shipSystemsAcceptanceTask;
        _shipSystemsAcceptanceTask = null;
        try
        {
            ShipSystemsAcceptanceReport report = task.GetAwaiter().GetResult();
            _shipSystemsAcceptanceReport = report;
            _shipSystemsAcceptanceHud = report.Passed
                ? $"PASS classes={report.ShipClasses}, systems={report.Systems}, " +
                  $"modules={report.Modules}, coverage={(report.CatalogCoverage ? 1 : 0)}, " +
                  $"slots={(report.SlotLimits ? 1 : 0)}, damage={(report.DamageLifecycle ? 1 : 0)}, " +
                  $"repair={(report.RepairLifecycle ? 1 : 0)}, commissioning={(report.PreRepairBlocked && report.PreRepairFlightReady && report.CommissionTransition && report.PostRepairFlightReady && report.ResetCommissioned ? 1 : 0)}, " +
                  $"ready={(report.FlightReadiness && report.HyperspaceReadiness ? 1 : 0)}, " +
                  $"fuel={(report.FuelLifecycle ? 1 : 0)}, restore={(report.ColdRestore ? 1 : 0)}, " +
                  $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}"
                : $"FAIL {report.Result}";
            _status = report.Result;
            string output = BuildShipSystemsAcceptanceOutput(report);
            if (report.Passed)
            {
                GD.Print(output);
            }
            else
            {
                GD.PushError(output);
            }

            UpdateCombinedCatalogAndShipAcceptanceState();
        }
        catch (Exception exception)
        {
            Fail("ship systems acceptance", exception);
        }
    }

    private void UpdateCombinedCatalogAndShipAcceptanceState()
    {
        if (_catalogMatrixAcceptanceTask is not null ||
            _shipSystemsAcceptanceTask is not null ||
            _stageOneVoyageAcceptanceTask is not null ||
            _galaxyNavigationAcceptanceTask is not null ||
            _ecologyAcceptanceTask is not null ||
            _proceduralQuestAcceptanceTask is not null ||
            _playerSurvivalAcceptanceTask is not null ||
            _npcFactionAcceptanceTask is not null ||
            NpcNavigationAcceptanceRunning ||
            _catalogMatrixAcceptanceReport is null ||
            _shipSystemsAcceptanceReport is null ||
            _stageOneVoyageAcceptanceReport is null ||
            _galaxyNavigationAcceptanceReport is null ||
            _ecologyAcceptanceReport is null ||
            _proceduralQuestAcceptanceReport is null ||
            _playerSurvivalAcceptanceReport is null ||
            _npcFactionAcceptanceReport is null ||
            _npcNavigationAcceptanceReport is null)
        {
            return;
        }

        bool passed = _catalogMatrixAcceptanceReport.Passed &&
            _shipSystemsAcceptanceReport.Passed &&
            _stageOneVoyageAcceptanceReport.Passed &&
            _galaxyNavigationAcceptanceReport.Passed &&
            _ecologyAcceptanceReport.Passed &&
            _proceduralQuestAcceptanceReport.Passed &&
            _playerSurvivalAcceptanceReport.Passed &&
            _npcFactionAcceptanceReport.Passed &&
            _npcNavigationAcceptanceReport.Passed;
        _state = passed
            ? SalvageRepairSliceState.Passed
            : SalvageRepairSliceState.Failed;
        _status = passed
            ? "TASK-076/TASK-110/TASK-112/TASK-114/TASK-116/TASK-118/TASK-120/TASK-122/TASK-124 runtime acceptance passed"
            : "TASK-076/TASK-110/TASK-112/TASK-114/TASK-116/TASK-118/TASK-120/TASK-122/TASK-124 runtime acceptance failed";
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
            _queueTerminalAcceptanceHud = report.Passed &&
                report.TerminalProgress &&
                report.TerminalEnergy &&
                report.TerminalReservations &&
                report.TerminalActions
                ? "PASS progress=1, energy=1, reservations=1, actions=1"
                : $"FAIL progress={(report.TerminalProgress ? 1 : 0)}, " +
                  $"energy={(report.TerminalEnergy ? 1 : 0)}, " +
                  $"reservations={(report.TerminalReservations ? 1 : 0)}, " +
                  $"actions={(report.TerminalActions ? 1 : 0)}";
            bool combinedPassed = report.Passed &&
                (_itemQualityDismantleAcceptanceReport?.Passed ?? true);
            _state = combinedPassed
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
                $"terminalProgress={(report.TerminalProgress ? 1 : 0)}; " +
                $"terminalEnergy={(report.TerminalEnergy ? 1 : 0)}; " +
                $"terminalReservations={(report.TerminalReservations ? 1 : 0)}; " +
                $"terminalActions={(report.TerminalActions ? 1 : 0)}; " +
                $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}; " +
                $"logWritten={(report.LogWritten ? 1 : 0)}; " +
                $"maxWriters={report.Diagnostics.MaximumConcurrentWriters}; " +
                $"integrity={report.Diagnostics.IntegrityResult}; " +
                $"elapsedMs={report.ElapsedMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)}; " +
                $"result={report.Result}";
            string terminalOutput =
                "TASK-092 production queue terminal acceptance " +
                $"{(report.Passed && report.TerminalProgress && report.TerminalEnergy && report.TerminalReservations && report.TerminalActions ? "PASS" : "FAIL")}: " +
                $"progress={(report.TerminalProgress ? 1 : 0)}; " +
                $"energy={(report.TerminalEnergy ? 1 : 0)}; " +
                $"reservations={(report.TerminalReservations ? 1 : 0)}; " +
                $"pauseResume={(report.TerminalActions ? 1 : 0)}; " +
                $"cancel={(report.TerminalActions ? 1 : 0)}; " +
                "result=queue projection exposes progress, energy, reservations and valid player actions";
            if (report.Passed)
            {
                GD.Print(output);
                GD.Print(terminalOutput);
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

    private void PollItemQualityDismantleAcceptanceTask()
    {
        if (_itemQualityDismantleAcceptanceTask is null ||
            !_itemQualityDismantleAcceptanceTask.IsCompleted)
        {
            return;
        }

        Task<ItemQualityDismantleAcceptanceReport> task =
            _itemQualityDismantleAcceptanceTask;
        _itemQualityDismantleAcceptanceTask = null;
        try
        {
            _itemQualityDismantleAcceptanceReport =
                task.GetAwaiter().GetResult();
            ItemQualityDismantleAcceptanceReport report =
                _itemQualityDismantleAcceptanceReport;
            _itemQualityDismantleAcceptanceHud = report.Passed
                ? $"PASS Q={report.Quality}, P={report.Purity}, " +
                  $"S={report.Stability}, dismantle={report.DismantleReturns}, " +
                  $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}"
                : $"FAIL {report.Result}";
            bool combinedPassed = report.Passed &&
                (_productionQueueAcceptanceReport?.Passed ?? true);
            _state = combinedPassed
                ? SalvageRepairSliceState.Passed
                : SalvageRepairSliceState.Failed;
            _status = report.Result;
            string output =
                "TASK-093 item quality and dismantle acceptance " +
                $"{(report.Passed ? "PASS" : "FAIL")}: " +
                $"recipe={report.RecipeId}; quality={report.Quality}; " +
                $"purity={report.Purity}; stability={report.Stability}; " +
                $"deterministic={(report.Deterministic ? 1 : 0)}; " +
                $"range={(report.InRecipeRange ? 1 : 0)}; " +
                $"qualitySensitive={(report.QualitySensitiveReturns ? 1 : 0)}; " +
                $"dismantleReturns={report.DismantleReturns}; " +
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
            Fail("item quality and dismantle acceptance", exception);
        }
    }

    private void PollMultiStationIndustryAcceptanceTask()
    {
        if (_multiStationIndustryAcceptanceTask is null ||
            !_multiStationIndustryAcceptanceTask.IsCompleted)
        {
            return;
        }

        Task<MultiStationIndustryAcceptanceReport> task =
            _multiStationIndustryAcceptanceTask;
        _multiStationIndustryAcceptanceTask = null;
        try
        {
            _multiStationIndustryAcceptanceReport =
                task.GetAwaiter().GetResult();
            MultiStationIndustryAcceptanceReport report =
                _multiStationIndustryAcceptanceReport;
            _multiStationIndustryAcceptanceHud = report.Passed
                ? $"PASS stations={report.PhysicalStations}, " +
                  $"recipes={report.RuntimeRecipes}, " +
                  $"routing={(report.WrongStationRejected ? 1 : 0)}, " +
                  $"repeatable={(report.RepeatableProcess ? 1 : 0)}, " +
                  $"chain={(report.ChainedProduction ? 1 : 0)}, " +
                  $"recharge={(report.EnergyRecharge ? 1 : 0)}, " +
                  $"properties={(report.PropertiesPersisted ? 1 : 0)}, " +
                  $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}"
                : $"FAIL {report.Result}";
            bool combinedPassed = report.Passed &&
                (_productionQueueAcceptanceReport?.Passed ?? true) &&
                (_itemQualityDismantleAcceptanceReport?.Passed ?? true);
            _state = combinedPassed
                ? SalvageRepairSliceState.Passed
                : SalvageRepairSliceState.Failed;
            _status = report.Result;
            string output =
                "TASK-096 multi-station industry acceptance " +
                $"{(report.Passed ? "PASS" : "FAIL")}: " +
                $"physicalStations={report.PhysicalStations}; " +
                $"recipes={report.RuntimeRecipes}; " +
                $"wrongStation={(report.WrongStationRejected ? 1 : 0)}; " +
                $"repeatable={(report.RepeatableProcess ? 1 : 0)}; " +
                $"chain={(report.ChainedProduction ? 1 : 0)}; " +
                $"recharge={(report.EnergyRecharge ? 1 : 0)}; " +
                $"properties={(report.PropertiesPersisted ? 1 : 0)}; " +
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
            Fail("multi-station industry acceptance", exception);
        }
    }

    private void PollProductionNetworkHudAcceptanceTask()
    {
        if (_productionNetworkHudAcceptanceTask is null ||
            !_productionNetworkHudAcceptanceTask.IsCompleted)
        {
            return;
        }

        Task<ProductionNetworkHudAcceptanceReport> task =
            _productionNetworkHudAcceptanceTask;
        _productionNetworkHudAcceptanceTask = null;
        try
        {
            _productionNetworkHudAcceptanceReport =
                task.GetAwaiter().GetResult();
            ProductionNetworkHudAcceptanceReport report =
                _productionNetworkHudAcceptanceReport;
            bool aggregate = report.AggregateCounts &&
                report.AggregateEnergy &&
                report.SimultaneousRunning;
            bool transitions = report.PauseResume &&
                report.Cancel &&
                report.Completion;
            _productionNetworkHudAcceptanceHud = report.Passed
                ? $"PASS stations={report.PhysicalStations}, " +
                  $"aggregate={(aggregate ? 1 : 0)}, " +
                  $"transitions={(transitions ? 1 : 0)}, " +
                  $"recharge={(report.EnergyRecharge ? 1 : 0)}, " +
                  $"restore={(report.ColdRestore ? 1 : 0)}, " +
                  $"fallback={(report.LegacyFallback ? 1 : 0)}, " +
                  $"unavailable={(report.FalseUnavailable ? 0 : 1)}"
                : $"FAIL {report.Result}";
            bool combinedPassed = report.Passed &&
                (_productionQueueAcceptanceReport?.Passed ?? true) &&
                (_itemQualityDismantleAcceptanceReport?.Passed ?? true) &&
                (_multiStationIndustryAcceptanceReport?.Passed ?? true);
            _state = combinedPassed
                ? SalvageRepairSliceState.Passed
                : SalvageRepairSliceState.Failed;
            _status = report.Result;
            string output =
                "TASK-098 production network HUD acceptance " +
                $"{(report.Passed ? "PASS" : "FAIL")}: " +
                $"stations={report.PhysicalStations}; " +
                $"aggregateCounts={(report.AggregateCounts ? 1 : 0)}; " +
                $"aggregateEnergy={(report.AggregateEnergy ? 1 : 0)}; " +
                $"simultaneousRunning={(report.SimultaneousRunning ? 1 : 0)}; " +
                $"pauseResume={(report.PauseResume ? 1 : 0)}; " +
                $"cancel={(report.Cancel ? 1 : 0)}; " +
                $"completion={(report.Completion ? 1 : 0)}; " +
                $"recharge={(report.EnergyRecharge ? 1 : 0)}; " +
                $"coldRestore={(report.ColdRestore ? 1 : 0)}; " +
                $"legacyFallback={(report.LegacyFallback ? 1 : 0)}; " +
                $"falseUnavailable={(report.FalseUnavailable ? 0 : 1)}; " +
                $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}; " +
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
            Fail("production network HUD acceptance", exception);
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

    private void PollStationServicesAcceptanceTask()
    {
        if (_stationServicesAcceptanceTask is null ||
            !_stationServicesAcceptanceTask.IsCompleted)
        {
            return;
        }

        Task<StationServicesAcceptanceReport> task =
            _stationServicesAcceptanceTask;
        _stationServicesAcceptanceTask = null;
        try
        {
            _stationServicesAcceptanceReport = task.GetAwaiter().GetResult();
            StationServicesAcceptanceReport report =
                _stationServicesAcceptanceReport;
            _stationServicesAcceptanceHud = report.Passed
                ? $"PASS economies={report.EconomyTypes}, factions={report.Factions}, " +
                  $"npc={report.Npcs}, quests={report.Quests}, " +
                  $"tradable={report.TradableItems}, " +
                  $"price={(report.PriceFormula ? 1 : 0)}, " +
                  $"daily={(report.DeterministicDaily ? 1 : 0)}, " +
                  $"trade={(report.BuySell ? 1 : 0)}, " +
                  $"graph={(report.QuestGraph ? 1 : 0)}, " +
                  $"restore={(report.ColdRestore ? 1 : 0)}, " +
                  $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}"
                : $"FAIL {report.Result}";
            _state = report.Passed
                ? SalvageRepairSliceState.Passed
                : SalvageRepairSliceState.Failed;
            _status = report.Result;
            string output =
                "TASK-102 station services acceptance " +
                $"{(report.Passed ? "PASS" : "FAIL")}: " +
                $"economies={report.EconomyTypes}; " +
                $"factions={report.Factions}; npcs={report.Npcs}; " +
                $"dialogueOptions={report.DialogueOptions}; " +
                $"quests={report.Quests}; questNodes={report.QuestNodes}; " +
                $"tradable={report.TradableItems}; " +
                $"priceFormula={(report.PriceFormula ? 1 : 0)}; " +
                $"deterministicDaily={(report.DeterministicDaily ? 1 : 0)}; " +
                $"offlineEconomy={(report.OfflineEconomy ? 1 : 0)}; " +
                $"supplyDemand={(report.SupplyDemandRepriced ? 1 : 0)}; " +
                $"buySell={(report.BuySell ? 1 : 0)}; " +
                $"atomicRejected={(report.AtomicRejected ? 1 : 0)}; " +
                $"creditConservation={(report.CreditConservation ? 1 : 0)}; " +
                $"questGraph={(report.QuestGraph ? 1 : 0)}; " +
                $"questFeasibility={(report.QuestFeasibility ? 1 : 0)}; " +
                $"questFlow={(report.QuestFlow ? 1 : 0)}; " +
                $"reputation={(report.Reputation ? 1 : 0)}; " +
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
                GD.Print(output);
            }
            else
            {
                GD.PushError(output);
            }
        }
        catch (Exception exception)
        {
            Fail("station services acceptance", exception);
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
            $"crafted={CountCraftedStationRecipes()}/{ObjectiveRecipes.Count}; pending=0");
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
        RefreshNpcNavigationObstacles();

        _shipTerminal?.SetRepaired(Session.ShipRepaired);
        foreach (PortableCraftingStation station in _craftingStations)
        {
            station.SetCrafting(false);
            CraftingRecipeDefinition[] stationRecipes = StationRecipes
                .Where(recipe =>
                    string.Equals(
                        recipe.RequiredStation,
                        station.StationId,
                        StringComparison.Ordinal) &&
                    !IndustryRecipePolicy.IsRepeatable(recipe))
                .ToArray();
            station.SetCrafted(
                stationRecipes.Length > 0 &&
                stationRecipes.All(recipe =>
                    Session.IsRecipeCrafted(recipe.RecipeId)));
        }

        _activeCraftingStation = null;
        ApplyGameplayProductionNetworkStationState();
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
            _hudMargin.OffsetBottom = 445.0f;
            _hudLabel.CustomMinimumSize = new Vector2(800.0f, 390.0f);
        }
        else if (_hudMode == SalvageRepairHudMode.Detailed)
        {
            _hudMargin.OffsetRight = 1140.0f;
            _hudMargin.OffsetBottom = 770.0f;
            _hudLabel.CustomMinimumSize = new Vector2(1090.0f, 715.0f);
        }
    }

    private void UpdateHud()
    {
        if (_hudLabel is null)
        {
            return;
        }

        if (_playerCoordinatesLabel is not null)
        {
            Node3D? coordinateSource =
                (_stageOneVoyageRuntime?.Piloted ?? false)
                    ? _voyageShip
                    : _player;
            string coordinateLabel =
                (_stageOneVoyageRuntime?.Piloted ?? false)
                    ? "SHIP POS"
                    : "PLAYER POS";
            _playerCoordinatesLabel.Text = coordinateSource is null
                ? $"{coordinateLabel} unavailable"
                : $"{coordinateLabel}  X={coordinateSource.GlobalPosition.X:0.0}  " +
                  $"Y={coordinateSource.GlobalPosition.Y:0.0}  " +
                  $"Z={coordinateSource.GlobalPosition.Z:0.0}";
        }

        if (_selectorStation is not null)
        {
            UpdateRecipeSelector();
        }

        if (_baseBuildMode)
        {
            UpdateBaseConstructionPanel();
        }

        if (_discoveryCatalogOpen)
        {
            UpdateDiscoveryCatalogPanel();
        }

        if (_shipManagementOpen)
        {
            UpdateShipManagementPanel();
        }

        string databaseLine = _diagnostics is null
            ? "DB: initializing"
            : $"DB: {_state} • schema={_diagnostics.SchemaVersion} • " +
              $"integrity={_diagnostics.IntegrityResult} • " +
              $"writes={_database?.CompletedWrites ?? 0}";
        CraftingStackDefinition primaryInput = RepairRecipe.Inputs[0];
        CraftingStackDefinition primaryOutput = RepairRecipe.Outputs[0];
        int craftedCount = CountCraftedStationRecipes();
        int totalStationRecipes = ObjectiveRecipes.Count;
        CraftingRecipeDefinition? nextRecipe = ObjectiveRecipes.FirstOrDefault(
            recipe => !Session.IsRecipeCrafted(recipe.RecipeId));
        ProductionQueueRuntime? activeQueue = _gameplayProductionNetwork?.Queues
            .FirstOrDefault(queue => queue.RunningCount > 0) ??
            _gameplayProductionNetwork?.Queues.FirstOrDefault(
                queue => queue.Jobs.Count > 0);
        ProductionQueueTerminalSnapshot? queueSnapshot = activeQueue is null
            ? null
            : ProductionQueueTerminalModel.Build(activeQueue);
        ProductionQueueTerminalJobRow? activeQueueJob = queueSnapshot?.Jobs
            .FirstOrDefault(job =>
                job.Status == ProductionQueueJobStatus.Running) ??
            queueSnapshot?.Jobs.FirstOrDefault();
        ProductionNetworkHudSnapshot networkHud =
            BuildGameplayProductionNetworkHudSnapshot();
        string networkLine = ProductionNetworkHudModel.FormatAggregate(networkHud);
        string detailedStationsLine =
            ProductionNetworkHudModel.FormatStations(networkHud, compact: false);
        string compactStationsLine =
            ProductionNetworkHudModel.FormatStations(networkHud, compact: true);
        string craftProcess = activeQueueJob is not null && activeQueue is not null
            ? $"QUEUE {activeQueue.StationId} {activeQueueJob.Status} " +
              $"{activeQueueJob.RecipeId} {activeQueueJob.ProgressBar} " +
              activeQueueJob.TimingText
            : _craftTimer.IsRunning
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
        else if (activeQueueJob is not null)
        {
            objective = $"Objective: queued production {activeQueueJob.RecipeId} " +
                $"({activeQueueJob.Progress01 * 100.0:0}%)";
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
            "interact with the fabricator to open Recipes/Research/Queue/Dismantle";
        string repairLine =
            $"Repair: {RepairRecipe.RecipeId} • " +
            $"{primaryInput.Quantity}x{primaryInput.DefinitionId} -> " +
            $"{primaryOutput.Quantity}x{primaryOutput.DefinitionId}";
        string matrixLine =
            $"Craft catalog: runtimeRecipes={StationRecipes.Count} • " +
            $"shipObjectives={craftedCount}/{totalStationRecipes} • " +
            $"pendingObjectives={totalStationRecipes - craftedCount} • " +
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
        string interaction = (_stageOneVoyageRuntime?.Piloted ?? false)
            ? StageOneVoyage.Location switch
            {
                StageOneVoyageLocation.PlanetSurface =>
                    "ship landed — E disembark",
                StageOneVoyageLocation.OutboundFlight =>
                    "fly to blue orbital beacon — Enter dock when slow and within 14 m",
                StageOneVoyageLocation.OrbitalStation =>
                    "docked — E station services, T undock after closing services",
                StageOneVoyageLocation.InboundFlight =>
                    "fly to green landing ring — Enter land when slow and within 18 m",
                _ => "voyage interaction unavailable"
            }
            : _player?.GetInteractionPrompt() ?? "interaction unavailable";
        string stationServicesLine =
            $"Station services: {StationServices.BuildSummary()} • " +
            $"NPC={StationServices.NpcId}";
        string baseConstructionLine =
            $"Base construction: {BaseConstruction.BuildSummary()}";
        string explorationLine =
            $"Exploration: POIs={PlanetaryPoiCatalog.Definitions.Count} • " +
            $"discovered={PlanetaryExploration.DiscoveredCount} • " +
            $"resolved={PlanetaryExploration.ResolvedCount} • " +
            $"named={PlanetaryExploration.NamedCount} • " +
            $"points={PlanetaryExploration.DiscoveryPoints} • scanner=P • catalog=J";
        ShipEffectiveStats shipStats = ShipSystems.GetEffectiveStats();
        string shipSystemsLine =
            $"Ship systems: class={GetShortContentId(ShipSystems.ShipClassId)} • " +
            $"modules={ShipSystems.InstalledModuleCount}/" +
            $"{shipStats.WeaponSlots + shipStats.TechnologySlots} • " +
            $"fuel={ShipSystems.Fuel:0.#}/{shipStats.FuelCapacity:0.#} • " +
            $"offline={ShipSystems.DisabledSystemCount}/" +
            $"{ShipSystemsCatalog.Systems.Count} • " +
            $"commissioned={(ShipSystems.Commissioned ? 1 : 0)} • " +
            $"flight={(ShipSystems.FlightReady ? "READY" : "BLOCKED")} • " +
            $"hyper={(ShipSystems.HyperspaceReady ? "READY" : "BLOCKED")} • manager=U";
        string voyageLine = BuildStageOneVoyageHudLine();
        string galaxyLine = BuildGalaxyNavigationHudLine();
        string ecologyLine = BuildEcologyHudLine();
        string npcFactionLine = BuildNpcFactionHudLine();
        string npcNavigationLine = BuildNpcNavigationHudLine();
        string missionLine = BuildProceduralQuestHudLine();

        if (_hudMode == SalvageRepairHudMode.Compact)
        {
            _hudLabel.Text =
                "VERTICAL SLICE 1 • INDUSTRY + EXPLORATION + SHIP SYSTEMS + STAGE 1 VOYAGE • H - HUD\n" +
                $"{databaseLine}\n" +
                $"Progress: salvage={Session.SalvageQuantity}/{Session.RequiredSalvage} • " +
                $"components={craftedCount}/{totalStationRecipes} • rev={_revision}\n" +
                $"Craft: {craftProcess}\n" +
                networkLine + "\n" +
                compactStationsLine + "\n" +
                stationServicesLine + "\n" +
                baseConstructionLine + "\n" +
                explorationLine + "\n" +
                shipSystemsLine + "\n" +
                voyageLine + "\n" +
                galaxyLine + "\n" +
                ecologyLine + "\n" +
                npcFactionLine + "\n" +
                npcNavigationLine + "\n" +
                missionLine + "\n" +
                $"{technologyLine}\n" +
                $"Interaction: {interaction}\n" +
                $"TASK-090 production queue (F1): {_productionQueueAcceptanceHud}\n" +
                $"TASK-092 queue terminal (F1): {_queueTerminalAcceptanceHud}\n" +
                $"TASK-093 item properties (F1): {_itemQualityDismantleAcceptanceHud}\n" +
                $"TASK-096 multi-station industry (F1): {_multiStationIndustryAcceptanceHud}\n" +
                $"TASK-098 production network HUD (F1): {_productionNetworkHudAcceptanceHud}\n" +
                $"TASK-100 resource lifecycle (F7): {_catalogResourceLifecycleAcceptanceHud}\n" +
                $"TASK-083 chemical runtime (F2): {_chemicalProcessAcceptanceHud}\n" +
                $"TASK-082 selector/research (F3): {_technologySelectorAcceptanceHud}\n" +
                $"TASK-102 station services (F3): {_stationServicesAcceptanceHud}\n" +
                $"TASK-106 base construction (F6): {_baseConstructionAcceptanceHud}\n" +
                $"TASK-080 industry catalog (F4): {_industryCatalogAcceptanceHud}\n" +
                $"TASK-108 planetary exploration (F4): {_planetaryExplorationAcceptanceHud}\n" +
                $"TASK-076 runtime matrix (F5): {_catalogMatrixAcceptanceHud}\n" +
                $"TASK-110 ship systems (F5): {_shipSystemsAcceptanceHud}\n" +
                $"TASK-112 Stage 1 voyage (F5): {_stageOneVoyageAcceptanceHud}\n" +
                $"TASK-114 galaxy navigation (F5): {_galaxyNavigationAcceptanceHud}\n" +
                $"TASK-116 ecology (F5): {_ecologyAcceptanceHud}\n" +
                $"TASK-118 procedural quests (F5): {_proceduralQuestAcceptanceHud}\n" +
                $"TASK-120 player survival (F5): {_playerSurvivalAcceptanceHud}\n" +
                $"TASK-122 NPC/factions (F5): {_npcFactionAcceptanceHud}\n" +
                $"TASK-124 NPC navigation (F5): {_npcNavigationAcceptanceHud}\n" +
                $"Status: {_status}\n" +
                "E - interact/select • I - exosuit/multitool • Q - mission journal on foot • U - ship management • M - system/galaxy map • V - ecology scan • O - ecology catalogue • P - POI scan • J - discoveries • G - base build • terminal/services: Tab tabs, Enter action, Esc close • " +
                "services: B buy, S sell, Q quests • F1 - production queue • " +
                "F2 - chemical runtime • " +
                "F3 - research + station services • F4 - industry + exploration • F5 - runtime catalog + ship systems + voyage + galaxy + ecology + procedural quests + player survival + NPC/factions + NPC navigation • " +
                "F6/F9/F10/F11/F12 - regressions • F7 - all resources";
            return;
        }

        _hudLabel.Text =
            "VERTICAL SLICE 1 - SALVAGE -> REPAIR -> INDUSTRY -> EXPLORE -> BOARD -> TAKEOFF -> STATION -> RETURN -> LAND -> AUTOSAVE • H - HUD\n" +
            databaseLine + "\n" +
            contentLine + "\n" +
            technologyLine + "\n" +
            repairLine + "\n" +
            matrixLine + "\n" +
            networkLine + "\n" +
            detailedStationsLine + "\n" +
            stationServicesLine + "\n" +
            baseConstructionLine + "\n" +
            explorationLine + "\n" +
            shipSystemsLine + "\n" +
            voyageLine + "\n" +
            galaxyLine + "\n" +
            ecologyLine + "\n" +
            npcFactionLine + "\n" +
            npcNavigationLine + "\n" +
            missionLine + "\n" +
            pendingPreview + "\n" +
            $"Craft process: {craftProcess}\n" +
            $"Resources: types={_resourceNodes.Select(node => node.ResourceDefinitionId).Distinct(StringComparer.Ordinal).Count()}/{ContentCatalog.Resources.Count} • " +
            $"nodes={_resourceNodes.Count} • collected={Session.CollectedNodeCount} • " +
            $"generated={_generatedResourcePlacements.Count}\n" +
            $"Snapshot: rev={_revision}\n" +
            objective + "\n" +
            ship + "\n" +
            $"Interaction: {interaction}\n" +
            autosave + "\n" +
            $"Last domain event: {_lastDomainEvent}\n" +
            $"TASK-090 production queue (F1): {_productionQueueAcceptanceHud}\n" +
            $"TASK-092 queue terminal (F1): {_queueTerminalAcceptanceHud}\n" +
            $"TASK-093 item properties (F1): {_itemQualityDismantleAcceptanceHud}\n" +
            $"TASK-096 multi-station industry (F1): {_multiStationIndustryAcceptanceHud}\n" +
            $"TASK-098 production network HUD (F1): {_productionNetworkHudAcceptanceHud}\n" +
            $"TASK-100 resource lifecycle (F7): {_catalogResourceLifecycleAcceptanceHud}\n" +
            $"TASK-083 chemical runtime (F2): {_chemicalProcessAcceptanceHud}\n" +
            $"TASK-082 selector/research (F3): {_technologySelectorAcceptanceHud}\n" +
            $"TASK-102 station services (F3): {_stationServicesAcceptanceHud}\n" +
            $"TASK-106 base construction (F6): {_baseConstructionAcceptanceHud}\n" +
            $"TASK-080 industry catalog (F4): {_industryCatalogAcceptanceHud}\n" +
            $"TASK-108 planetary exploration (F4): {_planetaryExplorationAcceptanceHud}\n" +
            $"TASK-076 runtime matrix (F5): {_catalogMatrixAcceptanceHud}\n" +
            $"TASK-110 ship systems (F5): {_shipSystemsAcceptanceHud}\n" +
            $"TASK-112 Stage 1 voyage (F5): {_stageOneVoyageAcceptanceHud}\n" +
            $"TASK-114 galaxy navigation (F5): {_galaxyNavigationAcceptanceHud}\n" +
            $"TASK-116 ecology (F5): {_ecologyAcceptanceHud}\n" +
            $"TASK-118 procedural quests (F5): {_proceduralQuestAcceptanceHud}\n" +
            $"TASK-120 player survival (F5): {_playerSurvivalAcceptanceHud}\n" +
            $"TASK-122 NPC/factions (F5): {_npcFactionAcceptanceHud}\n" +
            $"TASK-124 NPC navigation (F5): {_npcNavigationAcceptanceHud}\n" +
            $"TASK-072 legacy fourth path (F6): {_fourthCraftingAcceptanceHud}\n" +
            $"TASK-062 salvage/repair (F7): {_acceptanceHud}\n" +
            $"TASK-064 content (F9): {_contentAcceptanceHud}\n" +
            $"TASK-066 crafting (F10): {_craftingAcceptanceHud}\n" +
            $"TASK-068 craft time (F11): {_craftTimeAcceptanceHud}\n" +
            $"TASK-070 legacy third path (F12): {_thirdCraftingAcceptanceHud}\n" +
            $"Status: {_status}\n" +
            "WASD/Space - move • Shift sprint • Ctrl crouch • Space jetpack • I exosuit • E - interact/select • Q - procedural mission journal on foot • U - ship management • M - system/galaxy map • V - ecology scan • O - ecology catalogue • P - POI scanner • J - discoveries • G - base build • H - HUD • " +
            "terminal: Tab tabs, Q queue, D dismantle, Enter action, C cancel • " +
            "services: Tab tabs, B buy, S sell, Q quests, Enter action • " +
            "F1 - production queue acceptance • " +
            "F2 - chemical runtime acceptance • " +
            "F3 - research + station services acceptance • F4 - industry + planetary exploration • " +
            "F5 - runtime matrix + ship systems + Stage 1 voyage + galaxy navigation + ecology + procedural quests + player survival + NPC/factions + NPC navigation • F6 - base construction + legacy regression • " +
            "F9/F10/F11/F12 - regressions • F7 - all resources • " +
            "F8 - reset • voyage: E board/services/disembark, Enter dock/land, T launch/undock, K assist, F2 camera • Esc - close selector/release mouse";
    }

    private ProductionNetworkHudSnapshot
        BuildGameplayProductionNetworkHudSnapshot()
    {
        ProductionNetworkRuntime? network = _gameplayProductionNetwork;
        if (network is null)
        {
            return ProductionNetworkHudSnapshot.Unavailable(
                "runtime is not initialized");
        }

        try
        {
            IReadOnlyDictionary<string, string> displayNames =
                _craftingStations
                    .GroupBy(
                        station => station.StationId,
                        StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.First().Name.ToString(),
                        StringComparer.Ordinal);
            return ProductionNetworkHudModel.Build(network, displayNames);
        }
        catch (Exception exception)
        {
            return ProductionNetworkHudSnapshot.Unavailable(
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private int CountCraftedStationRecipes()
    {
        return ObjectiveRecipes.Count(recipe =>
            Session.IsRecipeCrafted(recipe.RecipeId));
    }

    private string BuildPendingRecipePreview()
    {
        CraftingRecipeDefinition[] pending = ObjectiveRecipes
            .Where(recipe => !Session.IsRecipeCrafted(recipe.RecipeId))
            .Take(3)
            .ToArray();
        if (pending.Length == 0)
        {
            return "Pending recipes: none";
        }

        int totalPending = ObjectiveRecipes.Count -
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

    private static string BuildCatalogResourceLifecycleAcceptanceOutput(
        CatalogResourceLifecycleAcceptanceReport report)
    {
        return "TASK-100 catalog resource lifecycle acceptance " +
            $"{(report.Passed ? "PASS" : "FAIL")}: " +
            $"catalog={report.CatalogResources}; " +
            $"physicalTypes={report.PhysicalResourceTypes}; " +
            $"nodes={report.ResourceNodes}; " +
            $"generated={report.GeneratedNodes}; " +
            $"collectedTypes={report.CollectedResourceTypes}; " +
            $"collectedNodes={report.CollectedResourceNodes}; " +
            $"metadata={(report.CatalogMetadataValid ? 1 : 0)}; " +
            $"placement={(report.DeterministicPlacement ? 1 : 0)}; " +
            $"unique={(report.UniqueNodes ? 1 : 0)}; " +
            $"duplicateRejected={(report.DuplicateRejected ? 1 : 0)}; " +
            $"mirrors={(report.InventoryMirrorsSynchronized ? 1 : 0)}; " +
            $"depletion={(report.DepletionPersisted ? 1 : 0)}; " +
            $"coldRestore={(report.ColdRestoreExact ? 1 : 0)}; " +
            $"reset={(report.ResetReady ? 1 : 0)}; " +
            $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}; " +
            $"logWritten={(report.LogWritten ? 1 : 0)}; " +
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

    private static string BuildShipSystemsAcceptanceOutput(
        ShipSystemsAcceptanceReport report)
    {
        return "TASK-110 ship systems acceptance " +
            $"{(report.Passed ? "PASS" : "FAIL")}: " +
            $"classes={report.ShipClasses}; " +
            $"systems={report.Systems}; " +
            $"modules={report.Modules}; " +
            $"catalogCoverage={(report.CatalogCoverage ? 1 : 0)}; " +
            $"classStats={(report.ClassStats ? 1 : 0)}; " +
            $"installAll={(report.InstallAll ? 1 : 0)}; " +
            $"slotLimits={(report.SlotLimits ? 1 : 0)}; " +
            $"duplicateRejected={(report.DuplicateRejected ? 1 : 0)}; " +
            $"derivedStats={(report.DerivedStats ? 1 : 0)}; " +
            $"damageLifecycle={(report.DamageLifecycle ? 1 : 0)}; " +
            $"repairLifecycle={(report.RepairLifecycle ? 1 : 0)}; " +
            $"moduleDisable={(report.ModuleDisable ? 1 : 0)}; " +
            $"flightReadiness={(report.FlightReadiness ? 1 : 0)}; " +
            $"hyperspaceReadiness={(report.HyperspaceReadiness ? 1 : 0)}; " +
            $"fuelLifecycle={(report.FuelLifecycle ? 1 : 0)}; " +
            $"inventoryConservation={(report.InventoryConservation ? 1 : 0)}; " +
            $"preRepairBlocked={(report.PreRepairBlocked ? 1 : 0)}; " +
            $"preRepairFlightReady={(report.PreRepairFlightReady ? 1 : 0)}; " +
            $"commissionTransition={(report.CommissionTransition ? 1 : 0)}; " +
            $"postRepairFlightReady={(report.PostRepairFlightReady ? 1 : 0)}; " +
            $"resetCommissioned={(report.ResetCommissioned ? 1 : 0)}; " +
            $"coldRestore={(report.ColdRestore ? 1 : 0)}; " +
            $"legacyFallback={(report.LegacyFallback ? 1 : 0)}; " +
            $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}; " +
            $"logWritten={(report.LogWritten ? 1 : 0)}; " +
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
