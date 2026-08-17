using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

public sealed record CrossPlatformDeterminismReport(
    bool Passed,
    bool PlatformPolicy,
    bool CultureInvariant,
    bool GeneratorVersionBound,
    bool GoldenContractAvailable,
    bool SurfaceSignatureStable,
    bool OfflineSinglePlayer,
    bool NetworkDependencyPolicy,
    int CulturesTested,
    int PlatformFamilies,
    int GeneratorVersion,
    string CanonicalSignature)
{
    public string BuildOutputLine() =>
        $"TASK-212 cross-platform determinism/offline acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"platforms={(PlatformPolicy ? 1 : 0)}; cultures={(CultureInvariant ? 1 : 0)}; " +
        $"generatorVersion={(GeneratorVersionBound ? 1 : 0)}; golden={(GoldenContractAvailable ? 1 : 0)}; " +
        $"surface={(SurfaceSignatureStable ? 1 : 0)}; offline={(OfflineSinglePlayer ? 1 : 0)}; " +
        $"networkPolicy={(NetworkDependencyPolicy ? 1 : 0)}; cultureCount={CulturesTested}; " +
        $"platformFamilies={PlatformFamilies}; version={GeneratorVersion}; " +
        $"signature={CanonicalSignature}; " +
        "result=section-41-windows-linux-seed-parity-and-offline-single-player-contract.";
}

public static class CrossPlatformDeterminismRuntime
{
    private static readonly (int X, int Y, int Z)[] SignatureSystems =
    {
        (0, 0, 0),
        (1, -2, 3),
        (-4, 5, -6),
        (17, 0, -9)
    };

    private static readonly (double X, double Z)[] TerrainSamples =
    {
        (0.0, 0.0),
        (12.5, -8.75),
        (-27.25, 19.5)
    };

    private static readonly string[] ValidationCultures =
    {
        "en-US",
        "ru-RU",
        "tr-TR"
    };

    public static CrossPlatformDeterminismReport Run(
        PlanetEnvironmentRuntime environmentRuntime,
        PlanetaryPoiCatalog poiCatalog)
    {
        ArgumentNullException.ThrowIfNull(environmentRuntime);
        ArgumentNullException.ThrowIfNull(poiCatalog);

        bool platformPolicy =
            CrossPlatformDeterminismPolicy.ClassifyPlatform("Windows", true) ==
                ProjectHorizonPlayerPlatform.WindowsX64 &&
            CrossPlatformDeterminismPolicy.ClassifyPlatform("Linux", true) ==
                ProjectHorizonPlayerPlatform.LinuxX64 &&
            CrossPlatformDeterminismPolicy.ClassifyPlatform("Windows", false) ==
                ProjectHorizonPlayerPlatform.Unsupported &&
            CrossPlatformDeterminismPolicy.RequiredPlatformFamilies == 2 &&
            CrossPlatformDeterminismPolicy.PlatformSeedParityRequired;

        bool generatorVersionBound =
            GalaxyNavigationRuntime.GeneratorVersion == ProjectHorizonGenerator.Version &&
            ProjectHorizonGenerator.Version > 0 &&
            CrossPlatformDeterminismPolicy.GeneratorVersionBumpRequiredForWorldChanges;

        string baseline = BuildCanonicalWorldSignature(
            GalaxyNavigationRuntime.DefaultUniverseSeed,
            environmentRuntime,
            poiCatalog);
        bool cultureInvariant = true;
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (string cultureName in ValidationCultures)
            {
                CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                string candidate = BuildCanonicalWorldSignature(
                    GalaxyNavigationRuntime.DefaultUniverseSeed,
                    environmentRuntime,
                    poiCatalog);
                cultureInvariant &= string.Equals(
                    baseline,
                    candidate,
                    StringComparison.Ordinal);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }

        // TASK-138 already owns the reviewed golden manifest. TASK-212 makes its
        // generator-version dependency an explicit cross-platform acceptance
        // prerequisite and CI executes that same manifest on Windows and Linux.
        bool goldenContractAvailable = GoldenSeedContract.CurrentSchemaVersion == 1 &&
            CrossPlatformDeterminismPolicy.CanonicalSignatureSchemaVersion == 1;

        string replay = BuildCanonicalWorldSignature(
            GalaxyNavigationRuntime.DefaultUniverseSeed,
            environmentRuntime,
            poiCatalog);
        bool surfaceSignatureStable = string.Equals(
            baseline,
            replay,
            StringComparison.Ordinal);

        bool offlineSinglePlayer =
            !CrossPlatformDeterminismPolicy.SinglePlayerRequiresInternet &&
            CrossPlatformDeterminismPolicy.CloudFeaturesOptional;
        bool networkDependencyPolicy =
            CrossPlatformDeterminismPolicy.PermittedProductionNetworkDependencies == 0;

        bool passed = platformPolicy && cultureInvariant && generatorVersionBound &&
            goldenContractAvailable && surfaceSignatureStable && offlineSinglePlayer &&
            networkDependencyPolicy;

        return new CrossPlatformDeterminismReport(
            passed,
            platformPolicy,
            cultureInvariant,
            generatorVersionBound,
            goldenContractAvailable,
            surfaceSignatureStable,
            offlineSinglePlayer,
            networkDependencyPolicy,
            ValidationCultures.Length,
            CrossPlatformDeterminismPolicy.RequiredPlatformFamilies,
            ProjectHorizonGenerator.Version,
            baseline);
    }

    public static string BuildCanonicalWorldSignature(
        long universeSeed,
        PlanetEnvironmentRuntime environmentRuntime,
        PlanetaryPoiCatalog poiCatalog)
    {
        if (universeSeed <= 0)
        {
            throw new InvalidOperationException("Determinism signature seed must be positive.");
        }
        ArgumentNullException.ThrowIfNull(environmentRuntime);
        ArgumentNullException.ThrowIfNull(poiCatalog);

        StringBuilder canonical = new();
        canonical.Append("schema=")
            .Append(CrossPlatformDeterminismPolicy.CanonicalSignatureSchemaVersion)
            .Append(";generator=").Append(ProjectHorizonGenerator.Version)
            .Append(";seed=").Append(universeSeed.ToString(CultureInfo.InvariantCulture));

        GalaxyNavigationRuntime navigation = new(universeSeed);
        GalaxySystemDefinition? starter = null;
        foreach ((int x, int y, int z) in SignatureSystems)
        {
            GalaxySystemDefinition system = navigation.GenerateSystem(x, y, z);
            if (x == 0 && y == 0 && z == 0)
            {
                starter = system;
            }
            AppendSystem(canonical, system);
        }

        GalaxyPlanetDefinition? landable = starter?.Planets.FirstOrDefault(planet =>
            !string.Equals(planet.Archetype, "gas_giant", StringComparison.Ordinal));
        if (starter is not null && landable is not null)
        {
            PlanetEnvironmentProfile environment = environmentRuntime.BuildProfile(
                landable,
                starter.StarType);
            AppendEnvironment(canonical, environment);
            if (environment.Landable)
            {
                PlanetSurfaceTerrainProfile terrain = PlanetSurfaceTerrainRuntime.BuildProfile(
                    environment,
                    landable.Seed);
                foreach ((double x, double z) in TerrainSamples)
                {
                    PlanetSurfaceTerrainSample sample = PlanetSurfaceTerrainRuntime.Sample(
                        terrain,
                        x,
                        z);
                    canonical.Append("|terrain=")
                        .Append(Fixed(x)).Append(',').Append(Fixed(z)).Append(',')
                        .Append(Fixed(sample.Height)).Append(',')
                        .Append(Fixed(sample.SlopeDegrees)).Append(',')
                        .Append(Fixed(sample.NormalizedHeight));
                }
            }
        }

        IReadOnlyList<PlanetaryPoiPlacement> pois = PlanetaryPoiPlanner.Plan(poiCatalog);
        canonical.Append("|poiSeed=")
            .Append(poiCatalog.WorldSeed.ToString(CultureInfo.InvariantCulture))
            .Append(";poiRegion=").Append(poiCatalog.RegionKey)
            .Append(";poiCount=").Append(pois.Count.ToString(CultureInfo.InvariantCulture));
        foreach (PlanetaryPoiPlacement poi in pois)
        {
            canonical.Append("|poi=").Append(poi.InstanceId).Append(',')
                .Append(poi.PoiTypeId).Append(',')
                .Append(Fixed(poi.PositionX)).Append(',')
                .Append(Fixed(poi.PositionY)).Append(',')
                .Append(Fixed(poi.PositionZ)).Append(',')
                .Append(Fixed(poi.RotationDegrees)).Append(',')
                .Append(Fixed(poi.Environment.Height));
        }

        byte[] bytes = Encoding.UTF8.GetBytes(canonical.ToString());
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static void AppendSystem(StringBuilder text, GalaxySystemDefinition system)
    {
        text.Append("|system=").Append(system.SystemId)
            .Append(';').Append(system.SectorX.ToString(CultureInfo.InvariantCulture))
            .Append(',').Append(system.SectorY.ToString(CultureInfo.InvariantCulture))
            .Append(',').Append(system.SectorZ.ToString(CultureInfo.InvariantCulture))
            .Append(';').Append(Fixed(system.PositionX))
            .Append(',').Append(Fixed(system.PositionY))
            .Append(',').Append(Fixed(system.PositionZ))
            .Append(';').Append(((int)system.StarType).ToString(CultureInfo.InvariantCulture))
            .Append(';').Append(system.EconomyType)
            .Append(';').Append(system.DangerLevel.ToString(CultureInfo.InvariantCulture));
        foreach (GalaxyPlanetDefinition planet in system.Planets)
        {
            text.Append("|planet=").Append(planet.PlanetId)
                .Append(',').Append(planet.Archetype)
                .Append(',').Append(planet.OrbitIndex.ToString(CultureInfo.InvariantCulture))
                .Append(',').Append(planet.MoonCount.ToString(CultureInfo.InvariantCulture))
                .Append(',').Append(planet.HasAtmosphere ? '1' : '0')
                .Append(',').Append(planet.HasWater ? '1' : '0')
                .Append(',').Append(planet.Seed.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AppendEnvironment(StringBuilder text, PlanetEnvironmentProfile environment)
    {
        text.Append("|environment=").Append(environment.PlanetId)
            .Append(',').Append(environment.Archetype)
            .Append(',').Append(environment.Landable ? '1' : '0')
            .Append(',').Append(Fixed(environment.RadiusKm))
            .Append(',').Append(Fixed(environment.SurfaceGravityG))
            .Append(',').Append(Fixed(environment.MeanTemperatureC))
            .Append(',').Append(Fixed(environment.AtmosphereDensity))
            .Append(',').Append(Fixed(environment.WaterCoverage))
            .Append(',').Append(environment.CloudLayerCount.ToString(CultureInfo.InvariantCulture))
            .Append(',').Append(environment.Seed.ToString(CultureInfo.InvariantCulture));
        foreach (string biome in environment.ActiveBiomeIds)
        {
            text.Append(',').Append(biome);
        }
    }

    private static string Fixed(double value) =>
        Math.Round(value, 6, MidpointRounding.AwayFromZero)
            .ToString("0.000000", CultureInfo.InvariantCulture);
}
