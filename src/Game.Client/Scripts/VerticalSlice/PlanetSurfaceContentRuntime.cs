using System;
using System.Collections.Generic;
using System.Linq;

public sealed record PlanetSurfaceContentProfile(
    string PlanetId,
    long WorldSeed,
    string RegionKey,
    PlanetEnvironmentProfile Environment,
    double Habitability,
    bool WaterHabitatEnabled,
    IReadOnlyList<string> ActiveBiomeIds);

public sealed class PlanetSurfaceContentRuntime
{
    private readonly PlanetEnvironmentRuntime _environment;
    private readonly EcologyCatalog _ecologyCatalog;
    private readonly PlanetaryPoiCatalog _poiCatalog;

    public PlanetSurfaceContentRuntime(
        PlanetEnvironmentRuntime environment,
        EcologyCatalog ecologyCatalog,
        PlanetaryPoiCatalog poiCatalog)
    {
        _environment = environment ??
            throw new ArgumentNullException(nameof(environment));
        _ecologyCatalog = ecologyCatalog ??
            throw new ArgumentNullException(nameof(ecologyCatalog));
        _poiCatalog = poiCatalog ??
            throw new ArgumentNullException(nameof(poiCatalog));
    }

    public PlanetSurfaceContentProfile BuildProfile(
        GalaxyPlanetDefinition planet,
        GalaxyStarType starType)
    {
        ArgumentNullException.ThrowIfNull(planet);
        PlanetEnvironmentProfile environment = _environment.BuildProfile(
            planet,
            starType);
        if (!environment.Landable)
        {
            throw new InvalidOperationException(
                $"Planet {planet.PlanetId} does not expose a landable surface.");
        }

        double thermalPenalty = Math.Clamp(
            Math.Abs(environment.MeanTemperatureC - 16.0) / 70.0,
            0.0,
            1.0);
        double hazardPenalty = Math.Max(
            environment.RadiationLevel,
            environment.ToxicityLevel);
        double atmospherePenalty = environment.AtmosphereDensity <= 0.02
            ? 0.30
            : environment.AtmosphereDensity < 0.25 ? 0.12 : 0.0;
        double waterBonus = Math.Min(0.10, environment.WaterCoverage * 0.16);
        double habitability = Math.Clamp(
            1.0 - thermalPenalty * 0.42 - hazardPenalty * 0.42 -
            atmospherePenalty + waterBonus,
            0.15,
            1.0);

        return new PlanetSurfaceContentProfile(
            planet.PlanetId,
            NormalizeSeed(planet.Seed),
            BuildRegionKey(planet.PlanetId),
            environment,
            habitability,
            environment.WaterCoverage >= 0.12,
            environment.ActiveBiomeIds.ToArray());
    }

    public EcologyPlan BuildEcologyPlan(PlanetSurfaceContentProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return EcologyPlanner.PlanPlanet(
            _ecologyCatalog,
            profile.WorldSeed,
            profile.ActiveBiomeIds,
            profile.Environment.WaterCoverage,
            profile.Habitability);
    }

    public IReadOnlyList<PlanetaryPoiPlacement> BuildPoiPlan(
        PlanetSurfaceContentProfile profile,
        IReadOnlyCollection<string>? activeQuestTags = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return PlanetaryPoiPlanner.PlanPlanet(
            _poiCatalog,
            profile.WorldSeed,
            profile.RegionKey,
            _environment,
            profile.Environment,
            activeQuestTags);
    }

    public static string BuildRegionKey(string planetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planetId);
        string suffix = planetId.StartsWith("planet.", StringComparison.Ordinal)
            ? planetId["planet.".Length..]
            : planetId;
        string token = suffix.Replace('.', '_').Replace('-', '_');
        string regionKey = $"region.surface.{token}";
        if (!GameContentCatalog.IsStableId(regionKey))
        {
            throw new InvalidOperationException(
                $"Planet ID {planetId} cannot produce a stable surface region key.");
        }

        return regionKey;
    }

    private static long NormalizeSeed(long seed)
    {
        if (seed == long.MinValue)
        {
            return long.MaxValue;
        }

        long normalized = Math.Abs(seed);
        return normalized == 0 ? 1 : normalized;
    }
}
