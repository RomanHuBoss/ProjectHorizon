using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed record GoldenPlanetExpectation(
    string PlanetId,
    string Archetype,
    int OrbitIndex,
    int MoonCount,
    bool HasAtmosphere,
    bool HasWater,
    long Seed);

public sealed record GoldenSystemCase(
    long UniverseSeed,
    int SectorX,
    int SectorY,
    int SectorZ,
    string SystemId,
    string DisplayName,
    string StarType,
    string EconomyType,
    int DangerLevel,
    int PlanetCount,
    IReadOnlyList<GoldenPlanetExpectation> Planets,
    string Checksum);

public sealed record GoldenPoiExpectation(
    string InstanceId,
    string PoiTypeId,
    double PositionX,
    double PositionY,
    double PositionZ,
    double RotationDegrees,
    double ControlHeight,
    double SlopeDegrees,
    double DistanceToWater,
    int Danger);

public sealed record GoldenPoiFixture(
    long WorldSeed,
    string RegionKey,
    int ExpectedCount,
    string Checksum,
    IReadOnlyList<GoldenPoiExpectation> Placements);

public sealed record GoldenSeedManifest(
    int SchemaVersion,
    int GeneratorVersion,
    IReadOnlyList<GoldenSystemCase> SystemCases,
    GoldenPoiFixture PoiFixture);

/// <summary>
/// Versioned section-36 golden contract. The checked-in JSON contains values
/// generated independently from this implementation. If deterministic world
/// generation changes intentionally, bump ProjectHorizonGenerator.Version and
/// regenerate the manifest in the same reviewed change.
/// </summary>
public static class GoldenSeedContract
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict
    };

    public static GoldenSeedManifest LoadFromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        GoldenSeedManifest manifest = JsonSerializer.Deserialize<GoldenSeedManifest>(
            json,
            JsonOptions) ?? throw new InvalidOperationException(
            "Golden seed manifest deserialized to null.");
        if (manifest.SchemaVersion != CurrentSchemaVersion ||
            manifest.GeneratorVersion <= 0 ||
            manifest.SystemCases is null || manifest.SystemCases.Count < 3 ||
            manifest.PoiFixture is null ||
            manifest.PoiFixture.Placements is null)
        {
            throw new InvalidOperationException("Golden seed manifest is invalid.");
        }

        return manifest;
    }

    public static bool VerifySystemCase(
        GoldenSystemCase expected,
        out string mismatch)
    {
        ArgumentNullException.ThrowIfNull(expected);
        GalaxySystemDefinition actual = new GalaxyNavigationRuntime(
            expected.UniverseSeed).GenerateSystem(
            expected.SectorX,
            expected.SectorY,
            expected.SectorZ);

        string checksum = ComputeSystemChecksum(expected.UniverseSeed, actual);
        bool scalarMatch =
            string.Equals(actual.SystemId, expected.SystemId, StringComparison.Ordinal) &&
            string.Equals(actual.DisplayName, expected.DisplayName, StringComparison.Ordinal) &&
            string.Equals(actual.StarType.ToString(), expected.StarType, StringComparison.Ordinal) &&
            string.Equals(actual.EconomyType, expected.EconomyType, StringComparison.Ordinal) &&
            actual.DangerLevel == expected.DangerLevel &&
            actual.Planets.Count == expected.PlanetCount &&
            string.Equals(checksum, expected.Checksum, StringComparison.OrdinalIgnoreCase);
        if (!scalarMatch)
        {
            mismatch = $"system={actual.SystemId}/{expected.SystemId}; " +
                $"star={actual.StarType}/{expected.StarType}; " +
                $"planets={actual.Planets.Count}/{expected.PlanetCount}; " +
                $"checksum={checksum}/{expected.Checksum}";
            return false;
        }

        if (expected.Planets.Count != actual.Planets.Count)
        {
            mismatch = "planet expectation count mismatch";
            return false;
        }

        for (int index = 0; index < actual.Planets.Count; index++)
        {
            GalaxyPlanetDefinition value = actual.Planets[index];
            GoldenPlanetExpectation wanted = expected.Planets[index];
            if (!string.Equals(value.PlanetId, wanted.PlanetId, StringComparison.Ordinal) ||
                !string.Equals(value.Archetype, wanted.Archetype, StringComparison.Ordinal) ||
                value.OrbitIndex != wanted.OrbitIndex ||
                value.MoonCount != wanted.MoonCount ||
                value.HasAtmosphere != wanted.HasAtmosphere ||
                value.HasWater != wanted.HasWater ||
                value.Seed != wanted.Seed)
            {
                mismatch = $"planet[{index}] changed";
                return false;
            }
        }

        mismatch = string.Empty;
        return true;
    }

    public static bool VerifyPoiFixture(
        GoldenPoiFixture expected,
        PlanetaryPoiCatalog catalog,
        out string mismatch)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.WorldSeed != expected.WorldSeed ||
            !string.Equals(catalog.RegionKey, expected.RegionKey, StringComparison.Ordinal))
        {
            mismatch = "POI seed/region changed";
            return false;
        }

        IReadOnlyList<PlanetaryPoiPlacement> actual = PlanetaryPoiPlanner.Plan(catalog);
        string checksum = ComputePoiChecksum(catalog, actual);
        if (actual.Count != expected.ExpectedCount ||
            expected.Placements.Count != actual.Count ||
            !string.Equals(checksum, expected.Checksum, StringComparison.OrdinalIgnoreCase))
        {
            mismatch = $"poi count={actual.Count}/{expected.ExpectedCount}; " +
                $"checksum={checksum}/{expected.Checksum}";
            return false;
        }

        for (int index = 0; index < actual.Count; index++)
        {
            PlanetaryPoiPlacement value = actual[index];
            GoldenPoiExpectation wanted = expected.Placements[index];
            if (!string.Equals(value.InstanceId, wanted.InstanceId, StringComparison.Ordinal) ||
                !string.Equals(value.PoiTypeId, wanted.PoiTypeId, StringComparison.Ordinal) ||
                !Close(value.PositionX, wanted.PositionX) ||
                !Close(value.PositionY, wanted.PositionY) ||
                !Close(value.PositionZ, wanted.PositionZ) ||
                !Close(value.RotationDegrees, wanted.RotationDegrees) ||
                !Close(value.Environment.Height, wanted.ControlHeight) ||
                !Close(value.Environment.SlopeDegrees, wanted.SlopeDegrees) ||
                !Close(value.Environment.DistanceToWater, wanted.DistanceToWater) ||
                value.Environment.Danger != wanted.Danger)
            {
                mismatch = $"poi[{index}] {value.InstanceId} changed";
                return false;
            }
        }

        mismatch = string.Empty;
        return true;
    }

    public static string ComputeSystemChecksum(
        long universeSeed,
        GalaxySystemDefinition system)
    {
        ArgumentNullException.ThrowIfNull(system);
        StringBuilder text = new();
        text.Append("seed=").Append(universeSeed)
            .Append(";sector=").Append(system.SectorX).Append(',')
            .Append(system.SectorY).Append(',').Append(system.SectorZ)
            .Append(";system=").Append(system.SystemId)
            .Append(";name=").Append(system.DisplayName)
            .Append(";star=").Append(system.StarType)
            .Append(";economy=").Append(system.EconomyType)
            .Append(";danger=").Append(system.DangerLevel)
            .Append(";planets=").Append(system.Planets.Count);
        foreach (GalaxyPlanetDefinition planet in system.Planets)
        {
            text.Append('|').Append(planet.PlanetId).Append(',')
                .Append(planet.Archetype).Append(',')
                .Append(planet.OrbitIndex).Append(',')
                .Append(planet.MoonCount).Append(',')
                .Append(planet.HasAtmosphere ? '1' : '0').Append(',')
                .Append(planet.HasWater ? '1' : '0').Append(',')
                .Append(planet.Seed);
        }
        return Sha256(text.ToString());
    }

    public static string ComputePoiChecksum(
        PlanetaryPoiCatalog catalog,
        IReadOnlyList<PlanetaryPoiPlacement> placements)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(placements);
        StringBuilder text = new();
        text.Append("worldSeed=").Append(catalog.WorldSeed)
            .Append(";region=").Append(catalog.RegionKey)
            .Append(";count=").Append(placements.Count);
        foreach (PlanetaryPoiPlacement value in placements)
        {
            text.Append('|').Append(value.InstanceId).Append(',')
                .Append(value.PoiTypeId).Append(',')
                .Append(Fixed(value.PositionX)).Append(',')
                .Append(Fixed(value.PositionY)).Append(',')
                .Append(Fixed(value.PositionZ)).Append(',')
                .Append(Fixed(value.RotationDegrees)).Append(',')
                .Append(Fixed(value.Environment.Height)).Append(',')
                .Append(Fixed(value.Environment.SlopeDegrees)).Append(',')
                .Append(Fixed(value.Environment.DistanceToWater)).Append(',')
                .Append(value.Environment.Danger);
        }
        return Sha256(text.ToString());
    }

    private static string Fixed(double value) =>
        value.ToString("0.000", CultureInfo.InvariantCulture);

    private static bool Close(double left, double right) =>
        Math.Abs(left - right) <= 0.000_001;

    private static string Sha256(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
