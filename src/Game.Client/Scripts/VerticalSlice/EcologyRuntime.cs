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
    private readonly HashSet<string> _discoveredFloraIds =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _discoveredFaunaIds =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _removedFloraInstanceIds =
        new(StringComparer.Ordinal);
    private double _simplifiedAccumulator;
    private long _simplifiedTicks;

    public EcologyRuntime(
        EcologyCatalog catalog,
        EcologyPlan plan,
        EcologySaveData? saveData = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(plan);
        _catalog = catalog;
        _plan = plan;

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

        if (saveData.WorldSeed != catalog.WorldSeed ||
            !string.Equals(
                saveData.RegionKey,
                catalog.RegionKey,
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

    public int DiscoveredFloraCount => _discoveredFloraIds.Count;

    public int DiscoveredFaunaCount => _discoveredFaunaIds.Count;

    public int RemovedFloraCount => _removedFloraInstanceIds.Count;

    public int DiscoveryPoints => CalculateDiscoveryPoints();

    public long SimplifiedTicks => _simplifiedTicks;

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

        _simplifiedAccumulator += deltaSeconds;
        double interval = 1.0 / SystemFrequencyPolicy.DistantAiHz;
        while (_simplifiedAccumulator >= interval)
        {
            _simplifiedAccumulator -= interval;
            _simplifiedTicks++;
        }
    }

    public EcologySaveData CreateSaveData()
    {
        return new EcologySaveData(
            _catalog.WorldSeed,
            _catalog.RegionKey,
            DiscoveryPoints,
            DiscoveredFloraIds.ToArray(),
            DiscoveredFaunaIds.ToArray(),
            RemovedFloraInstanceIds.ToArray());
    }

    public static double GetUpdateFrequencyHz(double distanceMeters)
    {
        if (!double.IsFinite(distanceMeters) || distanceMeters < 0.0)
        {
            return 0.0;
        }

        if (distanceMeters <= 20.0)
        {
            return SystemFrequencyPolicy.NearbyAiHz;
        }

        if (distanceMeters <= 50.0)
        {
            return SystemFrequencyPolicy.DistantAiHz;
        }

        return 0.0;
    }

    public static string SelectBehavior(
        EcologyFaunaDefinition definition,
        EcologyBehaviorContext context)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(context);
        bool Has(string behavior) => definition.Behaviors.Contains(
            behavior,
            StringComparer.Ordinal);

        if (context.HitRecently)
        {
            if (definition.Aggression >= 0.60 &&
                context.DistanceToThreat <= 8.0 &&
                Has("Attack"))
            {
                return "Attack";
            }

            if (Has("Flee"))
            {
                return "Flee";
            }
        }

        if (context.DistanceFromTerritory > definition.TerritoryRadius &&
            Has("ReturnToTerritory"))
        {
            return "ReturnToTerritory";
        }

        if (context.Fatigue >= 0.82 && Has("Sleep"))
        {
            return "Sleep";
        }

        if (context.Thirst >= 0.74 &&
            context.AtWater &&
            Has("Drink"))
        {
            return "Drink";
        }

        if (context.Hunger >= 0.70 &&
            string.Equals(
                definition.Diet,
                "Herbivore",
                StringComparison.Ordinal) &&
            Has("Graze"))
        {
            return "Graze";
        }

        if (context.GroupDistance >= 12.0 && Has("FollowGroup"))
        {
            return "FollowGroup";
        }

        if (definition.Aggression >= 0.35 &&
            context.DistanceToThreat <= 10.0 &&
            Has("Threaten"))
        {
            return "Threaten";
        }

        if (context.DistanceToThreat <= 18.0 && Has("Investigate"))
        {
            return "Investigate";
        }

        return Has("Wander") ? "Wander" : "Idle";
    }

    private int CalculateDiscoveryPoints()
    {
        int flora = _discoveredFloraIds.Sum(
            id => _catalog.GetFlora(id).ScanPoints);
        int fauna = _discoveredFaunaIds.Sum(
            id => _catalog.GetFauna(id).ScanPoints);
        return flora + fauna + _removedFloraInstanceIds.Count;
    }
}
