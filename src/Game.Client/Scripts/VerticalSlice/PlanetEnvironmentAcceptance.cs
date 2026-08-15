using System;
using System.Collections.Generic;
using System.Linq;

public sealed record PlanetEnvironmentAcceptanceReport(
    bool Passed,
    int StarterPlanets,
    int StarterArchetypes,
    int CatalogArchetypes,
    bool Deterministic,
    bool RadiusBounds,
    bool BiomeCoverage,
    bool BiomeFactorSampling,
    bool WaterPolicy,
    bool AtmospherePolicy,
    bool CloudPolicy,
    bool GasGiantNonLandable,
    bool CurrentPlanetRoundTrip,
    int Samples,
    string Result)
{
    public string BuildHudLine() =>
        $"{(Passed ? "PASS" : "FAIL")} planets={StarterPlanets}/4, " +
        $"types={StarterArchetypes}/4, catalog={CatalogArchetypes}/9, " +
        $"det={(Deterministic ? 1 : 0)}, biomes={(BiomeFactorSampling ? 1 : 0)}, " +
        $"water={(WaterPolicy ? 1 : 0)}, atmo={(AtmospherePolicy ? 1 : 0)}, " +
        $"clouds={(CloudPolicy ? 1 : 0)}, restore={(CurrentPlanetRoundTrip ? 1 : 0)}";

    public string BuildOutputLine() =>
        "TASK-150 planet environment acceptance " +
        $"{(Passed ? "PASS" : "FAIL")}: " +
        $"starterPlanets={StarterPlanets}/4; " +
        $"starterArchetypes={StarterArchetypes}/4; " +
        $"catalogArchetypes={CatalogArchetypes}/9; " +
        $"deterministic={(Deterministic ? 1 : 0)}; " +
        $"radiusBounds={(RadiusBounds ? 1 : 0)}; " +
        $"biomeCoverage={(BiomeCoverage ? 1 : 0)}; " +
        $"biomeFactorSampling={(BiomeFactorSampling ? 1 : 0)}; " +
        $"waterPolicy={(WaterPolicy ? 1 : 0)}; " +
        $"atmospherePolicy={(AtmospherePolicy ? 1 : 0)}; " +
        $"cloudPolicy={(CloudPolicy ? 1 : 0)}; " +
        $"gasGiantNonLandable={(GasGiantNonLandable ? 1 : 0)}; " +
        $"currentPlanetRoundTrip={(CurrentPlanetRoundTrip ? 1 : 0)}; " +
        $"samples={Samples}; result={Result}";
}

public static class PlanetEnvironmentAcceptanceRunner
{
    public static PlanetEnvironmentAcceptanceReport Run(
        PlanetEnvironmentCatalog catalog,
        EcologyCatalog ecology)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(ecology);
        PlanetEnvironmentRuntime environment = new(catalog, ecology);
        GalaxyNavigationRuntime navigation = new(
            GalaxyNavigationRuntime.DefaultUniverseSeed);
        GalaxySystemDefinition starter = navigation.CurrentSystem;

        int starterPlanets = starter.Planets.Count;
        int starterArchetypes = starter.Planets
            .Select(planet => planet.Archetype)
            .Distinct(StringComparer.Ordinal)
            .Count();
        bool deterministic = true;
        bool radiusBounds = true;
        bool biomeCoverage = true;
        bool biomeFactorSampling = true;
        bool waterPolicy = true;
        bool atmospherePolicy = true;
        bool cloudPolicy = true;
        bool gasGiantNonLandable = true;
        int samples = 0;

        foreach (GalaxyPlanetDefinition planet in starter.Planets)
        {
            PlanetEnvironmentProfile left = environment.BuildProfile(
                planet,
                starter.StarType);
            PlanetEnvironmentProfile right = environment.BuildProfile(
                planet,
                starter.StarType);
            deterministic &= ProfilesEqual(left, right);
            radiusBounds &= left.RadiusKm is >= 20.0 and <= 80.0;
            biomeCoverage &= left.ActiveBiomeIds.Count is >= 1 and <= 8 &&
                left.ActiveBiomeIds.All(ecology.Biomes.ContainsKey);
            waterPolicy &= planet.HasWater
                ? left.WaterCoverage > 0.0
                : left.WaterCoverage == 0.0;
            atmospherePolicy &= planet.HasAtmosphere
                ? left.AtmosphereDensity > 0.0
                : left.AtmosphereDensity == 0.0;
            cloudPolicy &= left.CloudLayerCount is >= 0 and <= 2 &&
                (planet.HasAtmosphere || left.CloudLayerCount == 0);

            foreach ((double latitude, double elevation, double waterDistance, double noise) in
                SampleInputs)
            {
                PlanetEnvironmentSample sample = environment.SampleBiome(
                    left,
                    latitude,
                    elevation,
                    waterDistance,
                    noise);
                samples++;
                biomeFactorSampling &=
                    left.ActiveBiomeIds.Contains(
                        sample.BiomeId,
                        StringComparer.Ordinal) &&
                    double.IsFinite(sample.TemperatureC) &&
                    sample.Moisture is >= 0.0 and <= 1.0;
            }
        }

        foreach (PlanetEnvironmentArchetypeDefinition definition in
            catalog.Archetypes.Values)
        {
            bool gasGiant = string.Equals(
                definition.Archetype,
                "gas_giant",
                StringComparison.Ordinal);
            GalaxyPlanetDefinition synthetic = new(
                $"planet.acceptance.{definition.Archetype}",
                definition.Archetype,
                1,
                0,
                definition.AtmosphereDensityMax > 0.0,
                definition.WaterCoverageMax > 0.0,
                5_000_000L + definition.Archetype.Length * 97L);
            PlanetEnvironmentProfile profile = environment.BuildProfile(
                synthetic,
                GalaxyStarType.YellowStar);
            deterministic &= ProfilesEqual(
                profile,
                environment.BuildProfile(
                    synthetic,
                    GalaxyStarType.YellowStar));
            radiusBounds &= profile.RadiusKm is >= 20.0 and <= 80.0;
            cloudPolicy &= profile.CloudLayerCount is >= 0 and <= 2;
            if (gasGiant)
            {
                gasGiantNonLandable &= !profile.Landable &&
                    profile.ActiveBiomeIds.Count == 0;
            }
            else
            {
                biomeCoverage &= profile.Landable &&
                    profile.ActiveBiomeIds.Count is >= 1 and <= 8;
            }
        }

        bool currentPlanetRoundTrip = false;
        if (starter.Planets.Count >= 2)
        {
            GalaxyPlanetDefinition second = starter.Planets[1];
            if (navigation.TrySelectCurrentPlanet(
                    second.PlanetId,
                    out _))
            {
                GalaxyNavigationRuntime restored = new(
                    navigation.CreateSaveData());
                currentPlanetRoundTrip = string.Equals(
                    restored.CurrentPlanetId,
                    second.PlanetId,
                    StringComparison.Ordinal);
            }
        }

        bool passed = starterPlanets == 4 &&
            starterArchetypes == 4 &&
            catalog.Archetypes.Count == 9 &&
            deterministic &&
            radiusBounds &&
            biomeCoverage &&
            biomeFactorSampling &&
            waterPolicy &&
            atmospherePolicy &&
            cloudPolicy &&
            gasGiantNonLandable &&
            currentPlanetRoundTrip;
        return new PlanetEnvironmentAcceptanceReport(
            passed,
            starterPlanets,
            starterArchetypes,
            catalog.Archetypes.Count,
            deterministic,
            radiusBounds,
            biomeCoverage,
            biomeFactorSampling,
            waterPolicy,
            atmospherePolicy,
            cloudPolicy,
            gasGiantNonLandable,
            currentPlanetRoundTrip,
            samples,
            passed
                ? "four-planet starter system, nine archetypes, climate biome sampling and current-planet persistence verified"
                : "one or more multi-planet environment invariants failed");
    }

    private static readonly (
        double Latitude,
        double Elevation,
        double WaterDistance,
        double Noise)[] SampleInputs =
    {
        (0.0, 0.05, 1.0, -0.7),
        (22.0, 0.25, 8.0, 0.2),
        (48.0, 0.55, 30.0, 0.8),
        (76.0, 0.82, 75.0, -0.1)
    };

    private static bool ProfilesEqual(
        PlanetEnvironmentProfile left,
        PlanetEnvironmentProfile right) =>
        string.Equals(left.PlanetId, right.PlanetId, StringComparison.Ordinal) &&
        string.Equals(left.Archetype, right.Archetype, StringComparison.Ordinal) &&
        Math.Abs(left.RadiusKm - right.RadiusKm) < 0.0000001 &&
        Math.Abs(left.SurfaceGravityG - right.SurfaceGravityG) < 0.0000001 &&
        Math.Abs(left.MeanTemperatureC - right.MeanTemperatureC) < 0.0000001 &&
        Math.Abs(left.AtmosphereDensity - right.AtmosphereDensity) < 0.0000001 &&
        Math.Abs(left.WaterCoverage - right.WaterCoverage) < 0.0000001 &&
        left.CloudLayerCount == right.CloudLayerCount &&
        left.ActiveBiomeIds.SequenceEqual(
            right.ActiveBiomeIds,
            StringComparer.Ordinal);
}
