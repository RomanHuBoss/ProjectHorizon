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
    private string _playerEquipmentFeedback = "";
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
        _player.WeaponFired = HandlePlayerWeaponFired;
    }


    private void HandlePlayerWeaponFired()
    {
        RecordPlayerMultitoolUse(
            PlayerMultitoolFunction.Weapon,
            "hitscan");
        PlayPlayerWeaponAudio();
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
        PlayPlayerDamageAudio();
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
            _status = L("ui.survival.incapacitated");
            ShowApplicationDeathScreen("ui.death.life_support");
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
        _playerEquipmentFeedback = L("ui.survival.equipment_opened");
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
            _playerEquipmentFeedback = LF("ui.survival.inventory_selected", ("item", id));
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
            _playerEquipmentFeedback = LF("ui.survival.install_blocked", ("result", result));
        }
        else
        {
            _playerEquipmentFeedback = LF("ui.survival.installed_feedback", ("item", id));
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
            _playerEquipmentFeedback = LF("ui.survival.uninstalled_feedback", ("item", id));
            GD.Print(
                "TASK-120 player equipment uninstall PASS: " +
                $"definition={id}; refund=1.");
        }
        else
        {
            _playerEquipmentFeedback = LF("ui.survival.uninstall_blocked", ("result", result));
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
        if (_playerEquipmentPanel is null || _playerEquipmentLabel is null || _playerSurvivalRuntime is null)
        {
            return;
        }
        _playerEquipmentPanel.Visible = _playerEquipmentOpen;
        if (!_playerEquipmentOpen) return;

        PlayerSurvivalEffectiveStats stats = PlayerSurvival.GetEffectiveStats();
        string environment = _lastPlayerEnvironmentTick?.Archetype ?? ResolveCurrentEnvironmentArchetype();
        string tabName = L($"ui.survival.tab.{_playerEquipmentTab.ToString().ToLowerInvariant()}");
        List<string> lines = new()
        {
            L("ui.survival.header"),
            LF("ui.survival.tab_line", ("tab", tabName)),
            LF("ui.survival.environment", ("environment", environment), ("swimming", PlayerSurvival.Swimming ? 1 : 0), ("alive", PlayerSurvival.IsAlive ? 1 : 0)),
            LF("ui.survival.vitals",
                ("health", PlayerSurvival.Health.ToString("0.#", CultureInfo.InvariantCulture)), ("maxHealth", stats.MaximumHealth.ToString("0.#", CultureInfo.InvariantCulture)),
                ("shield", PlayerSurvival.Shield.ToString("0.#", CultureInfo.InvariantCulture)), ("maxShield", stats.MaximumShield.ToString("0.#", CultureInfo.InvariantCulture)),
                ("stamina", PlayerSurvival.Stamina.ToString("0.#", CultureInfo.InvariantCulture)), ("maxStamina", stats.MaximumStamina.ToString("0.#", CultureInfo.InvariantCulture))),
            LF("ui.survival.support",
                ("life", PlayerSurvival.LifeSupport.ToString("0.#", CultureInfo.InvariantCulture)), ("maxLife", stats.MaximumLifeSupport.ToString("0.#", CultureInfo.InvariantCulture)),
                ("hazard", PlayerSurvival.HazardProtection.ToString("0.#", CultureInfo.InvariantCulture)), ("maxHazard", stats.MaximumHazardProtection.ToString("0.#", CultureInfo.InvariantCulture)),
                ("oxygen", PlayerSurvival.Oxygen.ToString("0.#", CultureInfo.InvariantCulture)), ("maxOxygen", stats.MaximumOxygen.ToString("0.#", CultureInfo.InvariantCulture))),
            LF("ui.survival.energy",
                ("jetpack", PlayerSurvival.JetpackEnergy.ToString("0.#", CultureInfo.InvariantCulture)), ("maxJetpack", stats.MaximumJetpackEnergy.ToString("0.#", CultureInfo.InvariantCulture)),
                ("multitool", PlayerSurvival.MultitoolEnergy.ToString("0.#", CultureInfo.InvariantCulture)), ("maxMultitool", stats.MaximumMultitoolEnergy.ToString("0.#", CultureInfo.InvariantCulture)),
                ("mode", PlayerSurvival.ActiveMultitoolFunction)),
            LF("ui.survival.protection", ("temperature", stats.TemperatureProtection.ToString("0.#", CultureInfo.InvariantCulture)), ("radiation", stats.RadiationProtection.ToString("0.#", CultureInfo.InvariantCulture)), ("toxic", stats.ToxicProtection.ToString("0.#", CultureInfo.InvariantCulture))),
            LF("ui.survival.slots", ("suit", PlayerSurvival.InstalledSuitModules.Count), ("suitMax", PlayerSurvivalCatalog.SuitSlotLimit), ("tool", PlayerSurvival.InstalledMultitoolModules.Count), ("toolMax", PlayerSurvivalCatalog.MultitoolSlotLimit)),
            string.Empty
        };
        IReadOnlyList<string> entries = PlayerEquipmentEntries;
        if (_playerEquipmentTab == PlayerEquipmentTab.Overview)
        {
            lines.Add(L("ui.survival.controls"));
            lines.Add(L("ui.survival.multitool_controls"));
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
                        : L("ui.survival.state.item"),
                    PlayerEquipmentTab.Suit => L(PlayerSurvival.InstalledSuitModules.Contains(id, StringComparer.Ordinal) ? "ui.survival.state.installed" : "ui.survival.state.available"),
                    PlayerEquipmentTab.Multitool => L(PlayerSurvival.InstalledMultitoolModules.Contains(id, StringComparer.Ordinal) ? "ui.survival.state.installed" : "ui.survival.state.available"),
                    _ => L("ui.survival.state.consumable")
                };
                lines.Add($"{marker} " + LF("ui.survival.entry", ("item", id), ("state", state), ("inventory", inventory)));
            }
        }
        lines.Add(string.Empty);
        lines.Add(LF("ui.common.result", ("result", _playerEquipmentFeedback)));
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
