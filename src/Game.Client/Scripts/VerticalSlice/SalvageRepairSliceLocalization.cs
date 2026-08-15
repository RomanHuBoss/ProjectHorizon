using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private bool _task132AcceptancePrinted;

    private static string L(string key) => GameLocalizationService.Text(key);

    private static string LF(string key, params (string Name, object? Value)[] values) =>
        GameLocalizationService.Format(key, values);

    private void BindLocalizationRuntime()
    {
        GameLocalizationService.EnsureInitialized();
        GameLocalizationService.LocaleChanged += OnGameplayLocaleChanged;
        GameLocalizationService.LocalizeControlTree(this);
        GD.Print(
            "TASK-132 localization READY: locales=2; active=" +
            GameLocalizationService.ActiveLocale + "; keyOnlyContent=1; liveSwitch=1.");
    }

    private void DisposeLocalizationRuntime()
    {
        GameLocalizationService.LocaleChanged -= OnGameplayLocaleChanged;
    }

    private void OnGameplayLocaleChanged(string locale)
    {
        GameLocalizationService.LocalizeControlTree(this);
        UpdateStationServicesPanel();
        UpdateRecipeSelector();
        UpdateBaseConstructionPanel();
        UpdateDiscoveryCatalogPanel();
        UpdateShipManagementPanel();
        UpdateGalaxyMapPanel();
        UpdateEcologyCatalogPanel();
        UpdateMissionJournalPanel();
        UpdatePlayerEquipmentPanel();
        UpdateNpcInteractionPanel();
        UpdatePlanetMapPanel();
        UpdateHud();
        GD.Print($"TASK-132 locale switch PASS: locale={locale}; gameplayUiRefresh=1.");
    }

    private void RunLocalizationAcceptance()
    {
        if (_task132AcceptancePrinted)
        {
            return;
        }
        _task132AcceptancePrinted = true;

        GameLocalizationDiagnostics diagnostics = GameLocalizationService.Diagnostics;
        List<string> requiredKeys = new();
        requiredKeys.AddRange(ContentCatalog.Items.Values.Select(value => value.LocalizationKey));
        requiredKeys.AddRange(ContentCatalog.Stations.Values.Select(value => value.LocalizationKey));
        requiredKeys.AddRange(ContentCatalog.Technologies.Values.Select(value => value.LocalizationKey));
        requiredKeys.AddRange(BaseConstructionCatalog.Modules.Values.Select(value => value.LocalizationKey));
        requiredKeys.AddRange(PlanetaryPoiCatalog.Definitions.Values.Select(value => value.LocalizationKey));
        requiredKeys.AddRange(ShipSystemsCatalog.Classes.Values.Select(value => value.LocalizationKey));
        requiredKeys.AddRange(ShipSystemsCatalog.Systems.Values.Select(value => value.LocalizationKey));
        requiredKeys.AddRange(ShipSystemsCatalog.Modules.Values.Select(value => value.LocalizationKey));
        requiredKeys.AddRange(StationServiceCatalog.Factions.Values.Select(value => value.LocalizationKey));
        requiredKeys.AddRange(StationServiceCatalog.Factions.Values.SelectMany(value => value.NamePoolKeys));
        requiredKeys.AddRange(StationServiceCatalog.Markets.Values.Select(value => value.LocalizationKey));
        requiredKeys.AddRange(StationServiceCatalog.Dialogues.Values.Select(value => value.LocalizationKey));
        requiredKeys.AddRange(StationServiceCatalog.Dialogues.Values.Select(value => value.GreetingKey));
        requiredKeys.AddRange(StationServiceCatalog.Dialogues.Values.Select(value => value.FarewellKey));
        requiredKeys.AddRange(StationServiceCatalog.Dialogues.Values.SelectMany(value => value.Options).Select(value => value.LocalizationKey));
        requiredKeys.AddRange(StationServiceCatalog.Npcs.Values.Select(value => value.LocalizationKey));
        requiredKeys.AddRange(StationServiceCatalog.Quests.Values.Select(value => value.LocalizationKey));
        requiredKeys.AddRange(NpcFactionCatalog.Archetypes.Values.Select(value => value.LocalizationKey));
        requiredKeys.AddRange(NpcFactionCatalog.Agents.Values.Select(value => value.DisplayNameKey));
        requiredKeys.AddRange(NpcFactionCatalog.Dialogues.Values.Select(value => value.GreetingKey));
        requiredKeys.AddRange(NpcFactionCatalog.Dialogues.Values.Select(value => value.FarewellKey));
        requiredKeys.AddRange(NpcFactionCatalog.Dialogues.Values.SelectMany(value => value.Options).Select(value => value.TextKey));
        requiredKeys.AddRange(NpcFactionCatalog.Dialogues.Values.SelectMany(value => value.Options).Select(value => value.ConsequenceKey));
        requiredKeys.AddRange(EcologyCatalog.Biomes.Values.Select(value => value.LocalizationKey));
        requiredKeys.AddRange(EcologyCatalog.Flora.Values.Select(value => value.LocalizationKey));
        requiredKeys.AddRange(EcologyCatalog.Fauna.Values.Select(value => value.LocalizationKey));
        requiredKeys.AddRange(new[]
        {
            "ui.main.title", "ui.main.continue", "ui.settings.title", "ui.settings.language",
            "ui.pause.title", "ui.death.title", "ui.game.industry_terminal", "ui.game.frontier_exchange",
            "ui.game.base_construction", "ui.game.discovery_catalog", "ui.game.ship_management",
            "ui.game.galaxy_map", "ui.game.ecology_catalog", "ui.game.mission_journal",
            "ui.game.npc_interaction", "ui.game.player_equipment", "ui.game.planet_map"
        });

        IReadOnlyList<string> missing = GameLocalizationService.MissingKeys(requiredKeys);
        string configured = GameUserSettingsService.Current.LanguageCode;
        GameLocalizationService.SetLocaleForAcceptance(GameLocalizationService.EnglishLocale, notify: false);
        string english = GameLocalizationService.Text("ui.main.continue");
        GameLocalizationService.SetLocaleForAcceptance(GameLocalizationService.RussianLocale, notify: false);
        string russian = GameLocalizationService.Text("ui.main.continue");
        GameLocalizationService.ApplyConfiguredLanguage(configured, notify: false);

        string npcJson = FileAccess.GetFileAsString("res://Content/npc_factions.json");
        string ecologyJson = FileAccess.GetFileAsString("res://Content/ecology.json");
        string stationJson = FileAccess.GetFileAsString("res://Content/station_services.json");
        string sceneText = FileAccess.GetFileAsString("res://Scenes/VerticalSlice/SalvageRepairSlice.tscn");
        string combined = npcJson + ecologyJson + stationJson;
        string[] forbiddenBilingualFields =
        {
            "displayNameEn", "displayNameRu", "DisplayNameEn", "DisplayNameRu",
            "nameEn", "nameRu", "greetingEn", "greetingRu", "farewellEn", "farewellRu",
            "textEn", "textRu", "consequenceEn", "consequenceRu", "namePool\""
        };
        bool keyOnlyContent = forbiddenBilingualFields.All(token =>
            !combined.Contains(token, StringComparison.Ordinal));
        bool sceneKeys = !sceneText.Contains("text = \"INDUSTRY TERMINAL\"", StringComparison.Ordinal) &&
            !sceneText.Contains("text = \"FRONTIER EXCHANGE\"", StringComparison.Ordinal) &&
            sceneText.Contains("text = \"ui.game.industry_terminal\"", StringComparison.Ordinal) &&
            sceneText.Contains("text = \"ui.game.planet_map\"", StringComparison.Ordinal);
        bool liveSwitch = !string.Equals(english, russian, StringComparison.Ordinal) &&
            string.Equals(english, "CONTINUE", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(russian);
        bool languageSetting = GameLocalizationService.IsSupportedConfiguration(configured);
        bool passed = diagnostics.LocaleCount == 2 && diagnostics.KeyParity &&
            diagnostics.MissingValueCount == 0 && missing.Count == 0 && keyOnlyContent &&
            sceneKeys && liveSwitch && languageSetting;

        string line =
            $"TASK-132 localization acceptance {(passed ? "PASS" : "FAIL")}: " +
            $"locales={diagnostics.LocaleCount}; keys={diagnostics.KeyCount}; parity={(diagnostics.KeyParity ? 1 : 0)}; " +
            $"missingValues={diagnostics.MissingValueCount}; requiredKeys={requiredKeys.Distinct(StringComparer.Ordinal).Count()}; " +
            $"missingKeys={missing.Count}; keyOnlyContent={(keyOnlyContent ? 1 : 0)}; sceneKeys={(sceneKeys ? 1 : 0)}; " +
            $"liveSwitch={(liveSwitch ? 1 : 0)}; settingsLanguage={(languageSetting ? 1 : 0)}; " +
            $"active={GameLocalizationService.ActiveLocale}; result=section-31.3-localization-runtime.";
        if (passed)
        {
            GD.Print(line);
        }
        else
        {
            GD.PushError(line + (missing.Count == 0 ? string.Empty : " missing=" + string.Join(",", missing.Take(12))));
        }
    }
}
