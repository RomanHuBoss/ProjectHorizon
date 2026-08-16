using System;

public sealed record PlanetSurfaceTerrainProfile(
    string PlanetId,
    string Archetype,
    long WorldSeed,
    double HalfExtent,
    int Resolution,
    double HeightAmplitude,
    double BaseFrequency,
    double SafeTerraceRadius,
    double FullReliefRadius,
    double MaximumWalkableSlopeDegrees,
    bool WaterBasinsEnabled);

public readonly record struct PlanetSurfaceTerrainSample(
    double Height,
    double SlopeDegrees,
    double NormalizedHeight);

public static class PlanetSurfaceTerrainRuntime
{
    public const double DefaultHalfExtent = 40.0;
    public const int DefaultResolution = 65;
    public const double WaterInteractionSurfaceY = 0.55;
    public const double AquaticHabitatSurfaceY = 0.04;

    public static PlanetSurfaceTerrainProfile BuildProfile(
        PlanetEnvironmentProfile environment,
        long worldSeed)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (!environment.Landable || worldSeed <= 0)
        {
            throw new InvalidOperationException(
                "Planet terrain requires a positive seed and a landable environment.");
        }

        (double amplitude, double frequency, double maximumSlope) =
            environment.Archetype switch
            {
                "desert" => (2.65, 0.048, 34.0),
                "frozen" => (2.85, 0.040, 35.0),
                "volcanic" => (4.75, 0.055, 39.0),
                "toxic" => (3.10, 0.047, 36.0),
                "radioactive" => (3.75, 0.052, 38.0),
                "barren" => (4.10, 0.050, 38.0),
                "oceanic" => (1.95, 0.037, 32.0),
                _ => (2.55, 0.043, 34.0)
            };

        return new PlanetSurfaceTerrainProfile(
            environment.PlanetId,
            environment.Archetype,
            worldSeed,
            DefaultHalfExtent,
            DefaultResolution,
            amplitude,
            frequency,
            SafeTerraceRadius: 16.0,
            FullReliefRadius: 23.0,
            MaximumWalkableSlopeDegrees: maximumSlope,
            WaterBasinsEnabled: environment.WaterCoverage >= 0.12);
    }

    public static PlanetSurfaceTerrainSample Sample(
        PlanetSurfaceTerrainProfile profile,
        double x,
        double z)
    {
        ArgumentNullException.ThrowIfNull(profile);
        double height = SampleHeight(profile, x, z);
        const double step = 0.45;
        double dx = SampleHeight(profile, x + step, z) -
            SampleHeight(profile, x - step, z);
        double dz = SampleHeight(profile, x, z + step) -
            SampleHeight(profile, x, z - step);
        double gradient = Math.Sqrt(dx * dx + dz * dz) / (2.0 * step);
        double slope = Math.Atan(gradient) * 180.0 / Math.PI;
        double normalized = Math.Clamp(
            (height / Math.Max(0.001, profile.HeightAmplitude) + 1.0) * 0.5,
            0.0,
            1.0);
        return new PlanetSurfaceTerrainSample(height, slope, normalized);
    }

    public static double SampleHeight(
        PlanetSurfaceTerrainProfile profile,
        double x,
        double z)
    {
        ArgumentNullException.ThrowIfNull(profile);
        double seedX = (profile.WorldSeed % 10007L) * 0.0137;
        double seedZ = (profile.WorldSeed % 8191L) * 0.0173;
        double nx = (x + seedX) * profile.BaseFrequency;
        double nz = (z - seedZ) * profile.BaseFrequency;
        double baseNoise = Fbm(nx, nz, profile.WorldSeed, 4);
        double secondary = Fbm(
            nx * 1.91 + 8.7,
            nz * 1.91 - 4.2,
            profile.WorldSeed ^ 0x5A17B93DL,
            3);
        double shape = profile.Archetype switch
        {
            "desert" => DesertShape(baseNoise, secondary, x, z),
            "frozen" => FrozenShape(baseNoise, secondary, x, z),
            "volcanic" => VolcanicShape(baseNoise, secondary, x, z),
            "toxic" => 0.74 * baseNoise + 0.26 * Math.Sin(nx * 5.2 + secondary),
            "radioactive" => 0.58 * baseNoise + 0.42 * Ridge(secondary),
            "barren" => 0.48 * baseNoise + 0.52 * Ridge(baseNoise + secondary * 0.4),
            "oceanic" => 0.82 * baseNoise + 0.18 * secondary,
            _ => 0.72 * baseNoise + 0.28 * secondary
        };

        double radialDistance = Math.Sqrt(x * x + z * z);
        double reliefWeight = SmoothStep(
            profile.SafeTerraceRadius,
            profile.FullReliefRadius,
            radialDistance);
        double height = profile.HeightAmplitude * shape * reliefWeight;

        if (profile.WaterBasinsEnabled)
        {
            height = ApplyBasinFloor(height, x, z, 22.0, 22.0, 7.2, 1.20);
            height = ApplyBasinFloor(height, x, z, -25.5, 25.5, 9.2, 1.05);
        }

        return Math.Clamp(
            height,
            -profile.HeightAmplitude - 1.25,
            profile.HeightAmplitude);
    }

    public static bool IsWalkable(
        PlanetSurfaceTerrainProfile profile,
        double x,
        double z) =>
        Sample(profile, x, z).SlopeDegrees <=
            profile.MaximumWalkableSlopeDegrees;

    public static string MorphologySignature(PlanetSurfaceTerrainProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        (double X, double Z)[] probes =
        {
            (-31.0, -27.0), (-26.0, 8.0), (-18.0, 30.0),
            (24.0, -29.0), (31.0, -4.0), (28.0, 30.0),
            (-32.0, 24.0), (0.0, 31.0), (19.0, 27.0)
        };
        string[] values = new string[probes.Length];
        for (int index = 0; index < probes.Length; index++)
        {
            PlanetSurfaceTerrainSample sample = Sample(
                profile,
                probes[index].X,
                probes[index].Z);
            values[index] = $"{sample.Height:0.00}/{sample.SlopeDegrees:0.0}";
        }
        return string.Join("|", values);
    }

    private static double DesertShape(
        double baseNoise,
        double secondary,
        double x,
        double z)
    {
        double dune = Math.Sin(x * 0.22 + Math.Sin(z * 0.09)) * 0.32;
        double mesa = Math.Tanh(baseNoise * 2.1) * 0.46;
        return Math.Clamp(mesa + dune + secondary * 0.18, -1.0, 1.0);
    }

    private static double FrozenShape(
        double baseNoise,
        double secondary,
        double x,
        double z)
    {
        double ridge = Ridge(baseNoise * 0.72 + secondary * 0.28) - 0.35;
        double drift = Math.Sin((x + z) * 0.075) * 0.16;
        return Math.Clamp(ridge * 0.78 + drift, -1.0, 1.0);
    }

    private static double VolcanicShape(
        double baseNoise,
        double secondary,
        double x,
        double z)
    {
        double rugged = (Ridge(baseNoise) - 0.38) * 0.95 + secondary * 0.32;
        double dx = x + 15.5;
        double dz = z - 18.0;
        double distance = Math.Sqrt(dx * dx + dz * dz);
        double crater = -0.58 * Math.Exp(-(distance * distance) / 42.0) +
            0.34 * Math.Exp(-((distance - 8.0) * (distance - 8.0)) / 8.0);
        return Math.Clamp(rugged + crater, -1.0, 1.0);
    }

    private static double ApplyBasinFloor(
        double currentHeight,
        double x,
        double z,
        double centerX,
        double centerZ,
        double radius,
        double depth)
    {
        double dx = x - centerX;
        double dz = z - centerZ;
        double distance = Math.Sqrt(dx * dx + dz * dz);
        if (distance >= radius)
        {
            return currentHeight;
        }
        double weight = 1.0 - SmoothStep(radius * 0.45, radius, distance);
        double basinFloor = -depth * weight;
        return Math.Min(currentHeight, basinFloor);
    }

    private static double Fbm(
        double x,
        double z,
        long seed,
        int octaves)
    {
        double value = 0.0;
        double amplitude = 0.58;
        double frequency = 1.0;
        double total = 0.0;
        for (int octave = 0; octave < octaves; octave++)
        {
            value += ValueNoise(x * frequency, z * frequency, seed + octave * 7919L) * amplitude;
            total += amplitude;
            amplitude *= 0.52;
            frequency *= 2.03;
        }
        return total <= 0.0 ? 0.0 : value / total;
    }

    private static double ValueNoise(double x, double z, long seed)
    {
        int x0 = (int)Math.Floor(x);
        int z0 = (int)Math.Floor(z);
        int x1 = x0 + 1;
        int z1 = z0 + 1;
        double tx = Fade(x - x0);
        double tz = Fade(z - z0);
        double a = Lerp(HashValue(x0, z0, seed), HashValue(x1, z0, seed), tx);
        double b = Lerp(HashValue(x0, z1, seed), HashValue(x1, z1, seed), tx);
        return Lerp(a, b, tz);
    }

    private static double HashValue(int x, int z, long seed)
    {
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            hash = (hash ^ (ulong)(uint)x) * 1099511628211UL;
            hash = (hash ^ (ulong)(uint)z) * 1099511628211UL;
            hash = (hash ^ (ulong)seed) * 1099511628211UL;
            hash ^= hash >> 32;
            hash *= 0xD6E8FEB86659FD93UL;
            hash ^= hash >> 32;
            return ((hash & 0xFFFFFFUL) / 8388607.5) - 1.0;
        }
    }

    private static double Ridge(double value) => 1.0 - Math.Abs(value);

    private static double Fade(double value) =>
        value * value * (3.0 - 2.0 * value);

    private static double SmoothStep(double edge0, double edge1, double value)
    {
        if (edge1 <= edge0)
        {
            return value >= edge1 ? 1.0 : 0.0;
        }
        double t = Math.Clamp((value - edge0) / (edge1 - edge0), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    private static double Lerp(double a, double b, double t) =>
        a + (b - a) * t;
}
