using System;
using System.Collections.Generic;
using System.Linq;

public enum PlanetaryPoiScanResult
{
    Discovered = 0,
    AlreadyDiscovered = 1,
    UnknownPoi = 2
}

public enum PlanetaryPoiInteractionResult
{
    Resolved = 0,
    AlreadyResolved = 1,
    ScanRequired = 2,
    UnknownPoi = 3
}

public sealed record PlanetaryPoiRuntimeState(
    PlanetaryPoiPlacement Placement,
    PlanetaryPoiDefinition Definition,
    bool Discovered,
    bool Resolved,
    string CustomName);

public sealed class PlanetaryExplorationRuntime
{
    private readonly PlanetaryPoiCatalog _catalog;
    private readonly Dictionary<string, PlanetaryPoiRuntimeState> _states;

    public PlanetaryExplorationRuntime(
        PlanetaryPoiCatalog catalog,
        IReadOnlyList<PlanetaryPoiPlacement> placements,
        PlanetaryExplorationSaveData? saveData = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(placements);
        if (placements.Count != PlanetaryPoiCatalog.ExpectedPoiTypeCount)
        {
            throw new InvalidOperationException(
                "Planetary exploration runtime requires one placement for each " +
                $"of the {PlanetaryPoiCatalog.ExpectedPoiTypeCount} POI types.");
        }

        _catalog = catalog;
        _states = new Dictionary<string, PlanetaryPoiRuntimeState>(
            StringComparer.Ordinal);
        Dictionary<string, PlanetaryPoiStateSaveData> savedStates = saveData is null
            ? new Dictionary<string, PlanetaryPoiStateSaveData>(
                StringComparer.Ordinal)
            : saveData.Pois.ToDictionary(
                state => state.InstanceId,
                StringComparer.Ordinal);
        foreach (PlanetaryPoiPlacement placement in placements)
        {
            if (!GameContentCatalog.IsStableId(placement.InstanceId) ||
                !_catalog.Definitions.ContainsKey(placement.PoiTypeId) ||
                !_states.TryAdd(
                    placement.InstanceId,
                    BuildState(placement, savedStates)))
            {
                throw new InvalidOperationException(
                    "Planetary exploration placements contain invalid or " +
                    $"duplicate instance {placement.InstanceId}.");
            }
        }

        if (saveData is not null)
        {
            ValidateSaveIdentity(saveData);
            int expectedPoints = _states.Values.Sum(state =>
                (state.Discovered ? state.Definition.DiscoveryPoints : 0) +
                (state.Resolved ? state.Definition.ResolutionPoints : 0));
            if (saveData.DiscoveryPoints != expectedPoints)
            {
                throw new InvalidOperationException(
                    "Planetary exploration discovery-point total does not " +
                    "match the persisted POI states.");
            }

            DiscoveryPoints = saveData.DiscoveryPoints;
        }
    }

    public long WorldSeed => _catalog.WorldSeed;

    public string RegionKey => _catalog.RegionKey;

    public int DiscoveryPoints { get; private set; }

    public int DiscoveredCount => _states.Values.Count(state => state.Discovered);

    public int ResolvedCount => _states.Values.Count(state => state.Resolved);

    public int NamedCount => _states.Values.Count(state =>
        !string.IsNullOrWhiteSpace(state.CustomName));

    public IReadOnlyList<PlanetaryPoiRuntimeState> States => _states.Values
        .OrderBy(state => state.Placement.InstanceId, StringComparer.Ordinal)
        .ToArray();

    public PlanetaryPoiRuntimeState GetState(string instanceId)
    {
        return _states.TryGetValue(
            instanceId,
            out PlanetaryPoiRuntimeState? state)
            ? state
            : throw new KeyNotFoundException(
                $"Unknown planetary POI instance {instanceId}.");
    }

    public PlanetaryPoiScanResult Scan(
        string instanceId,
        out string message)
    {
        if (!_states.TryGetValue(
            instanceId,
            out PlanetaryPoiRuntimeState? current))
        {
            message = GameLocalizationService.Format("ui.poi.unknown_instance", ("instance", instanceId));
            return PlanetaryPoiScanResult.UnknownPoi;
        }

        if (current.Discovered)
        {
            message = GameLocalizationService.Format("ui.poi.already_discovered", ("name", DisplayName(current)));
            return PlanetaryPoiScanResult.AlreadyDiscovered;
        }

        bool scanOnly = string.Equals(
            current.Definition.InteractionKind,
            "ScanOnly",
            StringComparison.Ordinal);
        PlanetaryPoiRuntimeState updated = current with
        {
            Discovered = true,
            Resolved = scanOnly
        };
        _states[instanceId] = updated;
        DiscoveryPoints += current.Definition.DiscoveryPoints;
        if (scanOnly)
        {
            DiscoveryPoints += current.Definition.ResolutionPoints;
        }

        message = GameLocalizationService.Format(
            scanOnly ? "ui.poi.discovered_resolved" : "ui.poi.discovered",
            ("name", DisplayName(updated)));
        return PlanetaryPoiScanResult.Discovered;
    }

    public PlanetaryPoiInteractionResult Interact(
        string instanceId,
        out string message)
    {
        if (!_states.TryGetValue(
            instanceId,
            out PlanetaryPoiRuntimeState? current))
        {
            message = GameLocalizationService.Format("ui.poi.unknown_instance", ("instance", instanceId));
            return PlanetaryPoiInteractionResult.UnknownPoi;
        }

        if (!current.Discovered)
        {
            message = GameLocalizationService.Format("ui.poi.scan_first", ("name", DisplayName(current)));
            return PlanetaryPoiInteractionResult.ScanRequired;
        }

        if (current.Resolved)
        {
            message = GameLocalizationService.Format("ui.poi.already_resolved", ("name", DisplayName(current)));
            return PlanetaryPoiInteractionResult.AlreadyResolved;
        }

        PlanetaryPoiRuntimeState updated = current with { Resolved = true };
        _states[instanceId] = updated;
        DiscoveryPoints += current.Definition.ResolutionPoints;
        message = GameLocalizationService.Format(
            "ui.poi.resolved",
            ("name", DisplayName(updated)),
            ("interaction", current.Definition.InteractionKind));
        return PlanetaryPoiInteractionResult.Resolved;
    }

    public bool TryRename(
        string instanceId,
        string customName,
        out string message)
    {
        if (!_states.TryGetValue(
            instanceId,
            out PlanetaryPoiRuntimeState? current))
        {
            message = GameLocalizationService.Format("ui.poi.unknown_instance", ("instance", instanceId));
            return false;
        }

        string normalized = customName.Trim();
        if (!current.Discovered)
        {
            message = GameLocalizationService.Text("ui.poi.scan_before_naming");
            return false;
        }

        if (!current.Definition.CanBeNamed)
        {
            message = GameLocalizationService.Format("ui.poi.cannot_name", ("name", DisplayName(current)));
            return false;
        }

        if (normalized.Length is < 3 or > 40)
        {
            message = GameLocalizationService.Text("ui.poi.name_length");
            return false;
        }

        _states[instanceId] = current with { CustomName = normalized };
        message = GameLocalizationService.Format("ui.poi.named", ("instance", instanceId), ("name", normalized));
        return true;
    }

    public PlanetaryExplorationSaveData CreateSaveData()
    {
        return new PlanetaryExplorationSaveData(
            WorldSeed,
            RegionKey,
            DiscoveryPoints,
            States.Select(state => new PlanetaryPoiStateSaveData(
                state.Placement.InstanceId,
                state.Placement.PoiTypeId,
                state.Discovered,
                state.Resolved,
                state.CustomName)).ToArray());
    }

    public string DisplayName(PlanetaryPoiRuntimeState state)
    {
        return string.IsNullOrWhiteSpace(state.CustomName)
            ? GameLocalizationService.Text(state.Definition.LocalizationKey)
            : state.CustomName;
    }

    private PlanetaryPoiRuntimeState BuildState(
        PlanetaryPoiPlacement placement,
        IReadOnlyDictionary<string, PlanetaryPoiStateSaveData> savedStates)
    {
        PlanetaryPoiDefinition definition = _catalog.GetDefinition(
            placement.PoiTypeId);
        if (!savedStates.TryGetValue(
            placement.InstanceId,
            out PlanetaryPoiStateSaveData? saved))
        {
            return new PlanetaryPoiRuntimeState(
                placement,
                definition,
                false,
                false,
                string.Empty);
        }

        if (!string.Equals(
                saved.PoiTypeId,
                placement.PoiTypeId,
                StringComparison.Ordinal) ||
            (saved.Resolved && !saved.Discovered) ||
            (!definition.CanBeNamed &&
             !string.IsNullOrWhiteSpace(saved.CustomName)))
        {
            throw new InvalidOperationException(
                $"Saved POI state {saved.InstanceId} does not match the " +
                "deterministic catalog placement.");
        }

        return new PlanetaryPoiRuntimeState(
            placement,
            definition,
            saved.Discovered,
            saved.Resolved,
            saved.CustomName.Trim());
    }

    private void ValidateSaveIdentity(PlanetaryExplorationSaveData saveData)
    {
        if (saveData.WorldSeed != WorldSeed ||
            !string.Equals(
                saveData.RegionKey,
                RegionKey,
                StringComparison.Ordinal) ||
            saveData.DiscoveryPoints < 0 ||
            saveData.Pois.Count != _states.Count ||
            saveData.Pois.Select(state => state.InstanceId)
                .Distinct(StringComparer.Ordinal).Count() != _states.Count)
        {
            throw new InvalidOperationException(
                "Planetary exploration save does not match the current seed, " +
                "region or deterministic POI set.");
        }
    }
}
