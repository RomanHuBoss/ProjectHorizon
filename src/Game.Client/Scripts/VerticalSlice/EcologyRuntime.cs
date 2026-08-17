using System;
using System.Collections.Generic;
using System.Linq;

public sealed record EcologyBehaviorContext(
    double DistanceToThreat,
    double Hunger,
    double Thirst,
    double Fatigue,
    double GroupDistance,
    double DistanceFromTerritory,
    bool AtWater,
    bool HitRecently);

public sealed class EcologyRuntime
{
    private readonly EcologyCatalog _catalog;
    private readonly EcologyPlan _plan;
    private readonly long _worldSeed;
    private readonly string _regionKey;
    private readonly HashSet<string> _discoveredFloraIds =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _discoveredFaunaIds =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _removedFloraInstanceIds =
        new(StringComparer.Ordinal);
    private readonly FaunaStatisticalSimulationRuntime _farFaunaSimulation;

    public EcologyRuntime(
        EcologyCatalog catalog,
        EcologyPlan plan,
        EcologySaveData? saveData = null)
        : this(
            catalog,
            plan,
            catalog?.WorldSeed ?? 0,
            catalog?.RegionKey ?? string.Empty,
            saveData)
    {
    }

    public EcologyRuntime(
        EcologyCatalog catalog,
        EcologyPlan plan,
        long worldSeed,
        string regionKey,
        EcologySaveData? saveData = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(plan);
        if (worldSeed <= 0 ||
            !GameContentCatalog.IsStableId(regionKey) ||
            !regionKey.StartsWith("region.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Ecology runtime identity is invalid.");
        }

        _catalog = catalog;
        _plan = plan;
        _worldSeed = worldSeed;
        _regionKey = regionKey;
        _farFaunaSimulation = new FaunaStatisticalSimulationRuntime(plan.SimplifiedFauna);

        if (plan.ActiveFauna.Count > catalog.ActiveFaunaLimit ||
            plan.SimplifiedFauna.Count > catalog.SimplifiedFaunaLimit)
        {
            throw new InvalidOperationException(
                "Ecology plan exceeds active/simplified fauna limits.");
        }

        if (saveData is null)
        {
            return;
        }

        if (saveData.WorldSeed != _worldSeed ||
            !string.Equals(
                saveData.RegionKey,
                _regionKey,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Ecology save identity does not match the catalog.");
        }

        foreach (string floraId in saveData.DiscoveredFloraIds)
        {
            if (!catalog.Flora.ContainsKey(floraId) ||
                !_discoveredFloraIds.Add(floraId))
            {
                throw new InvalidOperationException(
                    "Ecology save contains unknown or duplicate flora discovery.");
            }
        }

        foreach (string faunaId in saveData.DiscoveredFaunaIds)
        {
            if (!catalog.Fauna.ContainsKey(faunaId) ||
                !_discoveredFaunaIds.Add(faunaId))
            {
                throw new InvalidOperationException(
                    "Ecology save contains unknown or duplicate fauna discovery.");
            }
        }

        HashSet<string> knownFloraInstances = plan.Flora
            .Select(placement => placement.InstanceId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (string instanceId in saveData.RemovedFloraInstanceIds)
        {
            if (!knownFloraInstances.Contains(instanceId) ||
                !_removedFloraInstanceIds.Add(instanceId))
            {
                throw new InvalidOperationException(
                    "Ecology save contains unknown or duplicate removed flora.");
            }
        }

        int expectedPoints = CalculateDiscoveryPoints();
        if (saveData.DiscoveryPoints != expectedPoints)
        {
            throw new InvalidOperationException(
                "Ecology discovery-point total does not match saved deltas.");
        }
    }

    public long WorldSeed => _worldSeed;

    public string RegionKey => _regionKey;

    public int DiscoveredFloraCount => _discoveredFloraIds.Count;

    public int DiscoveredFaunaCount => _discoveredFaunaIds.Count;

    public int RemovedFloraCount => _removedFloraInstanceIds.Count;

    public int DiscoveryPoints => CalculateDiscoveryPoints();

    public long SimplifiedTicks => _farFaunaSimulation.TickCount;

    public FaunaStatisticalSnapshot FarFaunaSnapshot =>
        _farFaunaSimulation.CreateSnapshot();

    public IReadOnlyCollection<string> DiscoveredFloraIds =>
        _discoveredFloraIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();

    public IReadOnlyCollection<string> DiscoveredFaunaIds =>
        _discoveredFaunaIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();

    public IReadOnlyCollection<string> RemovedFloraInstanceIds =>
        _removedFloraInstanceIds
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

    public IReadOnlyList<EcologyFloraPlacement> FloraPlacements => _plan.Flora;

    public IReadOnlyList<EcologyFaunaSpawn> ActiveFauna => _plan.ActiveFauna;

    public IReadOnlyList<EcologyFaunaSpawn> SimplifiedFauna =>
        _plan.SimplifiedFauna;

    public bool IsFloraRemoved(string instanceId)
    {
        return _removedFloraInstanceIds.Contains(instanceId);
    }

    public bool TryScanFlora(
        string instanceId,
        out EcologyFloraDefinition definition,
        out string message)
    {
        EcologyFloraPlacement placement = _plan.Flora.FirstOrDefault(
            candidate => string.Equals(
                candidate.InstanceId,
                instanceId,
                StringComparison.Ordinal)) ??
            throw new KeyNotFoundException(
                $"Unknown ecology flora instance {instanceId}.");
        definition = _catalog.GetFlora(placement.FloraId);
        if (!_discoveredFloraIds.Add(definition.FloraId))
        {
            message = GameLocalizationService.Format(
                "ui.game.ecology.already_catalogued",
                ("name", GameLocalizationService.Text(definition.LocalizationKey)));
            return false;
        }

        message = GameLocalizationService.Format(
            "ui.game.ecology.catalogued_flora",
            ("name", GameLocalizationService.Text(definition.LocalizationKey)));
        return true;
    }

    public bool TryScanFauna(
        string instanceId,
        out EcologyFaunaDefinition definition,
        out string message)
    {
        EcologyFaunaSpawn spawn = _plan.ActiveFauna
            .Concat(_plan.SimplifiedFauna)
            .FirstOrDefault(candidate => string.Equals(
                candidate.InstanceId,
                instanceId,
                StringComparison.Ordinal)) ??
            throw new KeyNotFoundException(
                $"Unknown ecology fauna instance {instanceId}.");
        definition = _catalog.GetFauna(spawn.FaunaId);
        if (!_discoveredFaunaIds.Add(definition.FaunaId))
        {
            message = GameLocalizationService.Format(
                "ui.game.ecology.already_catalogued",
                ("name", GameLocalizationService.Text(definition.LocalizationKey)));
            return false;
        }

        message = GameLocalizationService.Format(
            "ui.game.ecology.catalogued_fauna",
            ("name", GameLocalizationService.Text(definition.LocalizationKey)));
        return true;
    }

    public bool TryHarvestFlora(
        string instanceId,
        out EcologyFloraDefinition definition,
        out string message)
    {
        EcologyFloraPlacement placement = _plan.Flora.FirstOrDefault(
            candidate => string.Equals(
                candidate.InstanceId,
                instanceId,
                StringComparison.Ordinal)) ??
            throw new KeyNotFoundException(
                $"Unknown ecology flora instance {instanceId}.");
        definition = _catalog.GetFlora(placement.FloraId);
        _discoveredFloraIds.Add(definition.FloraId);
        if (!_removedFloraInstanceIds.Add(instanceId))
        {
            message = GameLocalizationService.Format(
                "ui.game.ecology.already_harvested",
                ("name", GameLocalizationService.Text(definition.LocalizationKey)));
            return false;
        }

        message = GameLocalizationService.Format(
            "ui.game.ecology.harvested",
            ("name", GameLocalizationService.Text(definition.LocalizationKey)),
            ("yield", definition.HarvestDefinitionId));
        return true;
    }

    public void TickSimplified(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }

        _farFaunaSimulation.Tick(deltaSeconds);
    }

    public EcologySaveData CreateSaveData()
    {
        return new EcologySaveData(
            _worldSeed,
            _regionKey,
            DiscoveryPoints,
            DiscoveredFloraIds.ToArray(),
            DiscoveredFaunaIds.ToArray(),
            RemovedFloraInstanceIds.ToArray());
    }

    public static double GetUpdateFrequencyHz(double distanceMeters) =>
        FaunaBehaviorRuntime.GetDecisionFrequencyHz(distanceMeters);

    public static string SelectBehavior(
        EcologyFaunaDefinition definition,
        EcologyBehaviorContext context) =>
        FaunaBehaviorRuntime.SelectBehavior(definition, context);

    private int CalculateDiscoveryPoints()
    {
        int flora = _discoveredFloraIds.Sum(
            id => _catalog.GetFlora(id).ScanPoints);
        int fauna = _discoveredFaunaIds.Sum(
            id => _catalog.GetFauna(id).ScanPoints);
        return flora + fauna + _removedFloraInstanceIds.Count;
    }
}
