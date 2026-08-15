using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

public enum PlayerEquipmentTab
{
    Overview = 0,
    Inventory = 1,
    Suit = 2,
    Multitool = 3,
    Consumables = 4
}

public partial class SalvageRepairSlice
{
    private PlayerSurvivalCatalog? _playerSurvivalCatalog;
    private PlayerSurvivalRuntime? _playerSurvivalRuntime;
    private PanelContainer? _playerEquipmentPanel;
    private Label? _playerEquipmentLabel;
    private bool _playerEquipmentOpen;
    private PlayerEquipmentTab _playerEquipmentTab;
    private int _playerEquipmentSelection;
    private string _playerEquipmentFeedback = "I equipment • Shift sprint • Ctrl crouch • Space jetpack";
    private PlayerEnvironmentTickReport? _lastPlayerEnvironmentTick;
    private Task<PlayerSurvivalAcceptanceReport>? _playerSurvivalAcceptanceTask;
    private PlayerSurvivalAcceptanceReport? _playerSurvivalAcceptanceReport;
    private string _playerSurvivalAcceptanceHud = "READY";

    private PlayerSurvivalCatalog PlayerSurvivalCatalog =>
        _playerSurvivalCatalog ??
        throw new InvalidOperationException("Player survival catalog is unavailable.");

    private PlayerSurvivalRuntime PlayerSurvival =>
        _playerSurvivalRuntime ??
        throw new InvalidOperationException("Player survival runtime is unavailable.");

    private void BindPlayerSurvivalSceneNodes()
    {
        _playerEquipmentPanel = GetNodeOrNull<PanelContainer>("Hud/PlayerEquipment");
        _playerEquipmentLabel = GetNodeOrNull<Label>("Hud/PlayerEquipment/Label");
        if (_playerEquipmentPanel is null || _playerEquipmentLabel is null)
        {
            throw new InvalidOperationException(
                "Vertical slice scene is missing player equipment HUD.");
        }
    }

    private static PlayerSurvivalCatalog LoadPlayerSurvivalCatalog(
        GameContentCatalog contentCatalog)
    {
        const string path = "res://Content/player_survival.json";
        using Godot.FileAccess file = Godot.FileAccess.Open(
            path,
            Godot.FileAccess.ModeFlags.Read) ??
            throw new InvalidOperationException($"Unable to open {path}.");
        PlayerSurvivalCatalog catalog = PlayerSurvivalCatalog.LoadFromJson(
            file.GetAsText(),
            contentCatalog);
        GD.Print(
            "TASK-120 player survival catalog READY: " +
            $"schema={catalog.SchemaVersion}; suit={catalog.SuitModules.Count}; " +
            $"multitool={catalog.MultitoolModules.Count}; consumables={catalog.Consumables.Count}; " +
            $"environments={catalog.Environments.Count}; slots={catalog.SuitSlotLimit}/{catalog.MultitoolSlotLimit}.");
        return catalog;
    }

    private void InitializePlayerSurvivalRuntime(PlayerSurvivalSaveData? saveData)
    {
        _playerSurvivalRuntime = new PlayerSurvivalRuntime(
            PlayerSurvivalCatalog,
            saveData);
        _playerEquipmentOpen = false;
        _playerEquipmentTab = PlayerEquipmentTab.Overview;
        _playerEquipmentSelection = 0;
        _playerEquipmentFeedback = saveData is null
            ? "fresh/legacy exosuit initialized"
            : "exosuit, vitals and multitool restored";
        if (_playerEquipmentPanel is not null)
        {
            _playerEquipmentPanel.Visible = false;
        }
        BindPlayerControllerSurvivalBridge();
    }

    private void BindPlayerControllerSurvivalBridge()
    {
        if (_player is null || _playerSurvivalRuntime is null)
        {
            return;
        }
        _player.MovementResources = _playerSurvivalRuntime;
        _player.ExternalDamageHandler = HandlePlayerDamage;
        _player.WeaponFired = () => RecordPlayerMultitoolUse(
            PlayerMultitoolFunction.Weapon,
            "hitscan");
    }

    private void HandlePlayerDamage(double amount, string source)
    {
        if (_playerSurvivalRuntime is null)
        {
            return;
        }
        double healthBefore = PlayerSurvival.Health;
        double shieldBefore = PlayerSurvival.Shield;
        PlayerSurvival.ApplyDamage(amount);
        _lastDomainEvent = $"PlayerDamaged({source},{amount:0.0})";
        _playerEquipmentFeedback =
            $"damage {amount:0.0} from {source}; H={PlayerSurvival.Health:0.#} S={PlayerSurvival.Shield:0.#}";
        QueueCurrentSnapshot(AutosaveTrigger.PlayerChanged);
        GD.Print(
            "TASK-120 player damage PASS: " +
            $"source={source}; amount={amount:0.0}; " +
            $"shield={shieldBefore:0.#}->{PlayerSurvival.Shield:0.#}; " +
            $"health={healthBefore:0.#}->{PlayerSurvival.Health:0.#}.");
    }

    private void UpdatePlayerSurvival(double delta)
    {
        if (_playerSurvivalRuntime is null || _stageOneVoyageRuntime is null ||
            _galaxyNavigationRuntime is null)
        {
            return;
        }
        string archetype = ResolveCurrentEnvironmentArchetype();
        PlayerEnvironmentDefinition environment =
            PlayerSurvivalCatalog.GetEnvironment(archetype);
        bool activeOnFoot = !StageOneVoyage.Piloted;
        bool safeInterior = StageOneVoyage.Location == StageOneVoyageLocation.OrbitalStation;
        _lastPlayerEnvironmentTick = PlayerSurvival.Tick(
            environment,
            delta,
            activeOnFoot,
            safeInterior);
        if (!PlayerSurvival.IsAlive && activeOnFoot)
        {
            _status = "player incapacitated: return to main menu or start a new profile";
            ShowApplicationDeathScreen(
                "Life-support failure • health depleted • gameplay simulation paused");
        }
        if (_playerEquipmentOpen)
        {
            UpdatePlayerEquipmentPanel();
        }
    }

    private string ResolveCurrentEnvironmentArchetype()
    {
        GalaxySystemDefinition system = GalaxyNavigation.CurrentSystem;
        if (system.Planets.Count == 0)
        {
            return "barren";
        }
        string archetype = system.Planets[0].Archetype;
        return PlayerSurvivalCatalog.Environments.ContainsKey(archetype)
            ? archetype
            : "barren";
    }

    private void RecordPlayerMultitoolUse(
        PlayerMultitoolFunction function,
        string source)
    {
        if (_playerSurvivalRuntime is null || StageOneVoyage.Piloted)
        {
            return;
        }
        if (!PlayerSurvival.TryUseMultitool(function, out double spent))
        {
            _playerEquipmentFeedback =
                $"{function} blocked: multitool energy {PlayerSurvival.MultitoolEnergy:0.#}";
            return;
        }
        _playerEquipmentFeedback =
            $"{function} {source}: -{spent:0.#} energy; effectiveness=" +
            PlayerSurvival.GetMultitoolEffectiveness(function)
                .ToString("0.00", CultureInfo.InvariantCulture);
    }

    private bool HandlePlayerSurvivalInput(Key physical, Key logical)
    {
        if (_playerEquipmentOpen)
        {
            if (Matches(physical, logical, Key.Escape) ||
                Matches(physical, logical, Key.I))
            {
                ClosePlayerEquipment();
            }
            else if (Matches(physical, logical, Key.Tab))
            {
                _playerEquipmentTab = (PlayerEquipmentTab)(
                    ((int)_playerEquipmentTab + 1) %
                    Enum.GetValues<PlayerEquipmentTab>().Length);
                _playerEquipmentSelection = 0;
                UpdatePlayerEquipmentPanel();
            }
            else if (Matches(physical, logical, Key.Up))
            {
                MovePlayerEquipmentSelection(-1);
            }
            else if (Matches(physical, logical, Key.Down))
            {
                MovePlayerEquipmentSelection(1);
            }
            else if (Matches(physical, logical, Key.X))
            {
                UninstallSelectedPlayerEquipment();
            }
            else if (Matches(physical, logical, Key.Enter))
            {
                ConfirmPlayerEquipmentSelection();
            }
            return true;
        }

        if (Matches(physical, logical, Key.I) &&
            !StageOneVoyage.Piloted &&
            (_state == SalvageRepairSliceState.Ready ||
             _state == SalvageRepairSliceState.Passed))
        {
            OpenPlayerEquipment();
            return true;
        }
        if (Matches(physical, logical, Key.Z) &&
            !StageOneVoyage.Piloted &&
            _playerSurvivalRuntime is not null)
        {
            PlayerSurvival.CycleMultitoolFunction();
            _playerEquipmentFeedback =
                $"multitool mode={PlayerSurvival.ActiveMultitoolFunction}";
            GD.Print(
                "TASK-120 multitool mode PASS: " +
                $"mode={PlayerSurvival.ActiveMultitoolFunction}.");
            return true;
        }
        return false;
    }

    private void OpenPlayerEquipment()
    {
        CloseRecipeSelector();
        CloseStationServices();
        CloseBaseBuildMode();
        CloseDiscoveryCatalog();
        CloseShipManagement();
        CloseGalaxyMap();
        CloseEcologyCatalog();
        CloseMissionJournal();
        _playerEquipmentOpen = true;
        _playerEquipmentTab = PlayerEquipmentTab.Overview;
        _playerEquipmentSelection = 0;
        _playerEquipmentFeedback = "equipment opened";
        UpdatePlayerEquipmentPanel();
    }

    private void ClosePlayerEquipment()
    {
        _playerEquipmentOpen = false;
        if (_playerEquipmentPanel is not null)
        {
            _playerEquipmentPanel.Visible = false;
        }
    }

    private IReadOnlyList<string> PlayerEquipmentEntries =>
        _playerEquipmentTab switch
        {
            PlayerEquipmentTab.Inventory => Session.AvailableInventory
                .Where(stack => stack.Quantity > 0)
                .Select(stack => stack.DefinitionId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray(),
            PlayerEquipmentTab.Suit => PlayerSurvivalCatalog.SuitModules.Keys
                .OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            PlayerEquipmentTab.Multitool => PlayerSurvivalCatalog.MultitoolModules.Keys
                .OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            PlayerEquipmentTab.Consumables => PlayerSurvivalCatalog.Consumables.Keys
                .OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            _ => Array.Empty<string>()
        };

    private void MovePlayerEquipmentSelection(int delta)
    {
        IReadOnlyList<string> entries = PlayerEquipmentEntries;
        if (entries.Count == 0)
        {
            return;
        }
        _playerEquipmentSelection = (_playerEquipmentSelection + delta) % entries.Count;
        if (_playerEquipmentSelection < 0)
        {
            _playerEquipmentSelection += entries.Count;
        }
        UpdatePlayerEquipmentPanel();
    }

    private void ConfirmPlayerEquipmentSelection()
    {
        IReadOnlyList<string> entries = PlayerEquipmentEntries;
        if (entries.Count == 0)
        {
            return;
        }
        string id = entries[Math.Clamp(_playerEquipmentSelection, 0, entries.Count - 1)];
        if (_playerEquipmentTab == PlayerEquipmentTab.Inventory)
        {
            _playerEquipmentFeedback = $"inventory item selected: {id}";
            UpdatePlayerEquipmentPanel();
            return;
        }
        if (_playerEquipmentTab == PlayerEquipmentTab.Consumables)
        {
            UseSelectedSurvivalConsumable(id);
            return;
        }

        if (!TryConsumeSharedInventory(id, 1, out string inventoryResult))
        {
            _playerEquipmentFeedback = inventoryResult;
            UpdatePlayerEquipmentPanel();
            return;
        }
        PlayerEquipmentMutationResult result = _playerEquipmentTab == PlayerEquipmentTab.Suit
            ? PlayerSurvival.InstallSuitModule(id, out _)
            : PlayerSurvival.InstallMultitoolModule(id, out _);
        if (result != PlayerEquipmentMutationResult.Applied)
        {
            GrantSharedInventory(id, 1);
            _playerEquipmentFeedback = $"install blocked: {result}";
        }
        else
        {
            _playerEquipmentFeedback = $"installed {id}";
            QueueCurrentSnapshot(AutosaveTrigger.PlayerChanged);
            GD.Print(
                "TASK-120 player equipment install PASS: " +
                $"definition={id}; tab={_playerEquipmentTab}; " +
                $"suit={PlayerSurvival.InstalledSuitModules.Count}; " +
                $"multitool={PlayerSurvival.InstalledMultitoolModules.Count}.");
        }
        UpdatePlayerEquipmentPanel();
    }

    private void UninstallSelectedPlayerEquipment()
    {
        if (_playerEquipmentTab is not PlayerEquipmentTab.Suit and
            not PlayerEquipmentTab.Multitool)
        {
            return;
        }
        IReadOnlyList<string> entries = PlayerEquipmentEntries;
        if (entries.Count == 0)
        {
            return;
        }
        string id = entries[Math.Clamp(_playerEquipmentSelection, 0, entries.Count - 1)];
        PlayerEquipmentMutationResult result = _playerEquipmentTab == PlayerEquipmentTab.Suit
            ? PlayerSurvival.UninstallSuitModule(id, out _)
            : PlayerSurvival.UninstallMultitoolModule(id, out _);
        if (result == PlayerEquipmentMutationResult.Applied)
        {
            GrantSharedInventory(id, 1);
            QueueCurrentSnapshot(AutosaveTrigger.PlayerChanged);
            _playerEquipmentFeedback = $"uninstalled {id}; refund=1";
            GD.Print(
                "TASK-120 player equipment uninstall PASS: " +
                $"definition={id}; refund=1.");
        }
        else
        {
            _playerEquipmentFeedback = $"uninstall blocked: {result}";
        }
        UpdatePlayerEquipmentPanel();
    }

    private void UseSelectedSurvivalConsumable(string definitionId)
    {
        if (!TryConsumeSharedInventory(definitionId, 1, out string inventoryResult))
        {
            _playerEquipmentFeedback = inventoryResult;
            UpdatePlayerEquipmentPanel();
            return;
        }
        PlayerEquipmentMutationResult result = PlayerSurvival.UseConsumable(
            definitionId,
            out string useResult);
        if (result != PlayerEquipmentMutationResult.Applied)
        {
            GrantSharedInventory(definitionId, 1);
            _playerEquipmentFeedback = useResult;
        }
        else
        {
            _playerEquipmentFeedback = useResult;
            QueueCurrentSnapshot(AutosaveTrigger.PlayerChanged);
            GD.Print(
                "TASK-120 player consumable PASS: " +
                $"definition={definitionId}; health={PlayerSurvival.Health:0.#}; " +
                $"oxygen={PlayerSurvival.Oxygen:0.#}; hazard={PlayerSurvival.HazardProtection:0.#}.");
        }
        UpdatePlayerEquipmentPanel();
    }

    private void UpdatePlayerEquipmentPanel()
    {
        if (_playerEquipmentPanel is null || _playerEquipmentLabel is null ||
            _playerSurvivalRuntime is null)
        {
            return;
        }
        _playerEquipmentPanel.Visible = _playerEquipmentOpen;
        if (!_playerEquipmentOpen)
        {
            return;
        }
        PlayerSurvivalEffectiveStats stats = PlayerSurvival.GetEffectiveStats();
        string environment = _lastPlayerEnvironmentTick?.Archetype ??
            ResolveCurrentEnvironmentArchetype();
        List<string> lines = new()
        {
            "INVENTORY / EXOSUIT & MULTITOOL — TASK-120/TASK-130",
            $"Tab={_playerEquipmentTab} | I/Esc close | Tab switch | Enter inspect/install/use | X uninstall",
            $"Environment={environment} | swimming={(PlayerSurvival.Swimming ? 1 : 0)} | alive={(PlayerSurvival.IsAlive ? 1 : 0)}",
            $"Health {PlayerSurvival.Health:0.#}/{stats.MaximumHealth:0.#} | Shield {PlayerSurvival.Shield:0.#}/{stats.MaximumShield:0.#} | Stamina {PlayerSurvival.Stamina:0.#}/{stats.MaximumStamina:0.#}",
            $"LifeSupport {PlayerSurvival.LifeSupport:0.#}/{stats.MaximumLifeSupport:0.#} | Hazard {PlayerSurvival.HazardProtection:0.#}/{stats.MaximumHazardProtection:0.#} | Oxygen {PlayerSurvival.Oxygen:0.#}/{stats.MaximumOxygen:0.#}",
            $"Jetpack {PlayerSurvival.JetpackEnergy:0.#}/{stats.MaximumJetpackEnergy:0.#} | Multitool {PlayerSurvival.MultitoolEnergy:0.#}/{stats.MaximumMultitoolEnergy:0.#} | mode={PlayerSurvival.ActiveMultitoolFunction}",
            $"Protection T/R/X={stats.TemperatureProtection:0.#}/{stats.RadiationProtection:0.#}/{stats.ToxicProtection:0.#}",
            $"Suit slots {PlayerSurvival.InstalledSuitModules.Count}/{PlayerSurvivalCatalog.SuitSlotLimit} | Multitool slots {PlayerSurvival.InstalledMultitoolModules.Count}/{PlayerSurvivalCatalog.MultitoolSlotLimit}",
            string.Empty
        };
        IReadOnlyList<string> entries = PlayerEquipmentEntries;
        if (_playerEquipmentTab == PlayerEquipmentTab.Overview)
        {
            lines.Add("Shift sprint | Ctrl crouch | hold Space airborne jetpack | water: Space up/Ctrl down");
            lines.Add("Z cycles multitool mode; scanner/mining/weapon/analyzer/repair consume shared multitool energy.");
        }
        else
        {
            for (int index = 0; index < entries.Count; index++)
            {
                string id = entries[index];
                string marker = index == _playerEquipmentSelection ? ">" : " ";
                int inventory = Session.GetAvailableQuantity(id);
                string state = _playerEquipmentTab switch
                {
                    PlayerEquipmentTab.Inventory => ContentCatalog.Items.TryGetValue(id, out GameItemDefinition? item)
                        ? item.Category.ToUpperInvariant()
                        : "ITEM",
                    PlayerEquipmentTab.Suit => PlayerSurvival.InstalledSuitModules.Contains(id, StringComparer.Ordinal) ? "INSTALLED" : "AVAILABLE",
                    PlayerEquipmentTab.Multitool => PlayerSurvival.InstalledMultitoolModules.Contains(id, StringComparer.Ordinal) ? "INSTALLED" : "AVAILABLE",
                    _ => "CONSUMABLE"
                };
                lines.Add($"{marker} {id} | {state} | inv={inventory}");
            }
        }
        lines.Add(string.Empty);
        lines.Add($"Result: {_playerEquipmentFeedback}");
        _playerEquipmentLabel.Text = string.Join("\n", lines);
    }

    private void BeginPlayerSurvivalAcceptance(string directory)
    {
        string path = Path.Combine(directory, "save_1.player-survival-test.db");
        _playerSurvivalAcceptanceHud = "RUNNING";
        _playerSurvivalAcceptanceReport = null;
        _playerSurvivalAcceptanceTask = PlayerSurvivalAcceptanceRunner.RunAsync(
            path,
            SlotId,
            PlayerSurvivalCatalog,
            RepairRecipe,
            _lifetimeCancellation.Token);
    }

    private void PollPlayerSurvivalAcceptanceTask()
    {
        if (_playerSurvivalAcceptanceTask is null ||
            !_playerSurvivalAcceptanceTask.IsCompleted)
        {
            return;
        }
        Task<PlayerSurvivalAcceptanceReport> task = _playerSurvivalAcceptanceTask;
        _playerSurvivalAcceptanceTask = null;
        try
        {
            PlayerSurvivalAcceptanceReport report = task.GetAwaiter().GetResult();
            _playerSurvivalAcceptanceReport = report;
            _playerSurvivalAcceptanceHud = report.Passed ? "PASS" : "FAIL";
            GD.Print(
                $"TASK-120 player survival acceptance {(report.Passed ? "PASS" : "FAIL")}: " +
                $"suit={report.SuitModules}; multitool={report.MultitoolModules}; " +
                $"consumables={report.Consumables}; environments={report.Environments}; " +
                $"coverage={(report.CatalogCoverage ? 1 : 0)}; protection={(report.ProtectionRuntime ? 1 : 0)}; " +
                $"hazards={(report.HazardRuntime ? 1 : 0)}; oxygen={(report.OxygenRuntime ? 1 : 0)}; " +
                $"movement={(report.MovementResources ? 1 : 0)}; multitoolRuntime={(report.MultitoolRuntime ? 1 : 0)}; " +
                $"damage={(report.DamageRuntime ? 1 : 0)}; consumablesRuntime={(report.ConsumablesRuntime ? 1 : 0)}; " +
                $"slots={(report.EquipmentSlots ? 1 : 0)}; coldRestore={(report.ColdRestore ? 1 : 0)}; " +
                $"legacyFallback={(report.LegacyFallback ? 1 : 0)}; roundTrip={(report.ExactRoundTrip ? 1 : 0)}; " +
                $"repeatedSave={(report.RepeatedSave ? 1 : 0)}; logWritten={(report.LogWritten ? 1 : 0)}; " +
                $"maxWriters={report.Diagnostics.MaximumConcurrentWriters}; integrity={report.Diagnostics.IntegrityResult}; " +
                $"elapsedMs={report.ElapsedMilliseconds:0.0}; result={report.Result}");
            UpdateCombinedCatalogAndShipAcceptanceState();
        }
        catch (Exception exception)
        {
            _playerSurvivalAcceptanceHud = "FAIL";
            GD.PushError($"TASK-120 player survival acceptance FAIL: {exception.Message}");
        }
    }
}
