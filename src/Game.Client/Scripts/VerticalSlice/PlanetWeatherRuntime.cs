using System;

public enum PlanetWeatherKind
{
    Clear = 0,
    Wind = 1,
    Storm = 2,
    Toxic = 3
}

public sealed record PlanetWeatherState(
    string PlanetId,
    long DayIndex,
    double LocalHour,
    PlanetWeatherKind Kind,
    double Intensity,
    double WindMetersPerSecond,
    double WindDirectionDegrees,
    double Precipitation,
    double Visibility,
    double SunAzimuthDegrees,
    double SunElevationDegrees,
    double Daylight,
    double CloudMultiplier,
    double FogMultiplier,
    double TemperatureOffsetC,
    double TemperatureHazardBonus,
    double ToxicHazardBonus,
    double LifeSupportDrainBonus,
    double FaunaSpeedMultiplier);

/// <summary>
/// Pure deterministic planetary weather/diurnal runtime. No Godot API is used,
/// so weather identity can be reproduced in F5/xUnit/persistence tests.
/// </summary>
public sealed class PlanetWeatherRuntime
{
    public const double DefaultDayDurationSeconds = 600.0;
    public const double WeatherCellHours = 2.0;

    private PlanetEnvironmentProfile _environment;
    private double _gameHours;
    private PlanetWeatherKind? _developerOverride;

    public PlanetWeatherRuntime(
        PlanetEnvironmentProfile environment,
        PlanetWeatherSaveData? saveData = null)
    {
        _environment = environment ??
            throw new ArgumentNullException(nameof(environment));
        if (saveData is not null &&
            double.IsFinite(saveData.GameHours) &&
            saveData.GameHours >= 0.0)
        {
            _gameHours = saveData.GameHours;
        }
        else
        {
            // A fresh starter world begins in readable morning light, while
            // each planet still has a deterministic solar phase.
            _gameHours = NormalizeHours(9.0 - PlanetPhaseHours(environment.Seed));
        }
    }

    public PlanetEnvironmentProfile Environment => _environment;
    public double GameHours => _gameHours;
    public bool HasDeveloperOverride => _developerOverride.HasValue;

    public PlanetWeatherState Current => BuildState(_environment, _gameHours, _developerOverride);

    public void SetEnvironment(PlanetEnvironmentProfile environment)
    {
        _environment = environment ??
            throw new ArgumentNullException(nameof(environment));
    }

    public PlanetWeatherState Advance(double deltaSeconds)
    {
        if (double.IsFinite(deltaSeconds) && deltaSeconds > 0.0)
        {
            _gameHours += deltaSeconds * 24.0 / DefaultDayDurationSeconds;
        }
        return Current;
    }

    public PlanetWeatherState SetLocalHour(double localHour)
    {
        if (!double.IsFinite(localHour))
        {
            throw new ArgumentOutOfRangeException(nameof(localHour));
        }
        double target = NormalizeHours(localHour);
        double phase = PlanetPhaseHours(_environment.Seed);
        double adjusted = _gameHours + phase;
        double dayBase = Math.Floor(adjusted / 24.0) * 24.0;
        double candidate = dayBase + target - phase;
        if (candidate < 0.0)
        {
            candidate += 24.0;
        }
        _gameHours = candidate;
        return Current;
    }

    public PlanetWeatherState SetDeveloperOverride(PlanetWeatherKind? weather)
    {
        _developerOverride = weather;
        return Current;
    }

    public PlanetWeatherSaveData CreateSaveData() => new(_gameHours);

    public static PlanetWeatherState BuildState(
        PlanetEnvironmentProfile environment,
        double gameHours,
        PlanetWeatherKind? overrideKind = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (!double.IsFinite(gameHours) || gameHours < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(gameHours));
        }

        double local = LocalHour(environment, gameHours);
        long day = (long)Math.Floor(
            (gameHours + PlanetPhaseHours(environment.Seed)) / 24.0);
        long weatherCell = (long)Math.Floor(
            (gameHours + PlanetPhaseHours(environment.Seed)) / WeatherCellHours);
        ulong weatherSeed = unchecked((ulong)environment.Seed) ^
            unchecked((ulong)weatherCell * 0x9E3779B97F4A7C15UL);

        PlanetWeatherKind kind = overrideKind ?? SelectWeather(environment, weatherSeed);
        double intensity = kind == PlanetWeatherKind.Clear
            ? 0.10 + Unit(weatherSeed, 0x21UL) * 0.18
            : 0.42 + Unit(weatherSeed, 0x2FUL) * 0.58;
        double windBase = kind switch
        {
            PlanetWeatherKind.Clear => 1.0,
            PlanetWeatherKind.Wind => 7.0,
            PlanetWeatherKind.Storm => 13.0,
            PlanetWeatherKind.Toxic => 5.0,
            _ => 1.0
        };
        double wind = (windBase + Unit(weatherSeed, 0x39UL) * 7.0) *
            Math.Clamp(0.45 + environment.AtmosphereDensity * 0.60, 0.20, 1.60) *
            (0.65 + intensity * 0.45);
        double windDirection = Unit(weatherSeed, 0x45UL) * 360.0;

        double wetness = Math.Clamp(
            environment.BaseMoisture * 0.72 + environment.WaterCoverage * 0.46,
            0.0,
            1.0);
        double precipitation = kind == PlanetWeatherKind.Storm
            ? Math.Clamp(wetness * (0.35 + intensity * 0.80), 0.0, 1.0)
            : kind == PlanetWeatherKind.Toxic
                ? Math.Clamp(environment.ToxicityLevel * intensity * 0.42, 0.0, 0.75)
                : 0.0;
        double visibility = kind switch
        {
            PlanetWeatherKind.Clear => 1.0,
            PlanetWeatherKind.Wind => 0.90 - intensity * 0.10,
            PlanetWeatherKind.Storm => 0.68 - intensity * 0.34,
            PlanetWeatherKind.Toxic => 0.58 - intensity * 0.28,
            _ => 1.0
        };
        visibility = Math.Clamp(visibility, 0.22, 1.0);

        double solarPhase = (local - 6.0) / 24.0 * Math.PI * 2.0;
        double maximumElevation = 52.0 + Unit(unchecked((ulong)environment.Seed), 0x51UL) * 18.0;
        double sunElevation = Math.Sin(solarPhase) * maximumElevation;
        double sunAzimuth = NormalizeDegrees(
            82.0 + local / 24.0 * 360.0 +
            Unit(unchecked((ulong)environment.Seed), 0x63UL) * 42.0);
        double daylight = SmoothStep(-7.0, 14.0, sunElevation);

        double cloudMultiplier = kind switch
        {
            PlanetWeatherKind.Clear => 0.58,
            PlanetWeatherKind.Wind => 0.86,
            PlanetWeatherKind.Storm => 1.50,
            PlanetWeatherKind.Toxic => 1.24,
            _ => 1.0
        };
        double fogMultiplier = kind switch
        {
            PlanetWeatherKind.Clear => 0.72,
            PlanetWeatherKind.Wind => 0.95,
            PlanetWeatherKind.Storm => 1.55,
            PlanetWeatherKind.Toxic => 1.85,
            _ => 1.0
        };

        double dayTemperature = Math.Sin((local - 9.0) / 24.0 * Math.PI * 2.0) *
            environment.TemperatureVariationC * 0.18;
        double weatherTemperature = kind switch
        {
            PlanetWeatherKind.Storm => -3.5 * intensity,
            PlanetWeatherKind.Wind => -1.0 * intensity,
            PlanetWeatherKind.Toxic => 1.5 * intensity,
            _ => 0.0
        };
        double temperatureOffset = dayTemperature + weatherTemperature;
        double temperatureHazard = Math.Clamp(
            Math.Abs(temperatureOffset) / 65.0 * (kind == PlanetWeatherKind.Storm ? 1.25 : 0.55),
            0.0,
            0.32);
        double toxicHazard = kind == PlanetWeatherKind.Toxic
            ? Math.Clamp(0.18 + environment.ToxicityLevel * 0.42 * intensity, 0.0, 0.65)
            : 0.0;
        double lifeSupportBonus = kind switch
        {
            PlanetWeatherKind.Storm => 0.018 * intensity,
            PlanetWeatherKind.Toxic => 0.055 * intensity,
            _ => 0.0
        };
        double faunaMultiplier = kind switch
        {
            PlanetWeatherKind.Clear => 1.0,
            PlanetWeatherKind.Wind => 0.88,
            PlanetWeatherKind.Storm => 0.58,
            PlanetWeatherKind.Toxic => 0.72,
            _ => 1.0
        };

        return new PlanetWeatherState(
            environment.PlanetId,
            day,
            local,
            kind,
            intensity,
            wind,
            windDirection,
            precipitation,
            visibility,
            sunAzimuth,
            sunElevation,
            daylight,
            cloudMultiplier,
            fogMultiplier,
            temperatureOffset,
            temperatureHazard,
            toxicHazard,
            lifeSupportBonus,
            faunaMultiplier);
    }

    private static PlanetWeatherKind SelectWeather(
        PlanetEnvironmentProfile environment,
        ulong seed)
    {
        if (environment.AtmosphereDensity <= 0.03)
        {
            return PlanetWeatherKind.Clear;
        }

        double toxicChance = environment.ToxicityLevel >= 0.28
            ? Math.Clamp(environment.ToxicityLevel * 0.20, 0.0, 0.22)
            : 0.0;
        double stormChance = Math.Clamp(
            environment.AtmosphereDensity *
            (environment.CloudDensity * 0.18 + environment.BaseMoisture * 0.10) +
            (environment.Archetype is "desert" or "volcanic" ? 0.055 : 0.0),
            0.0,
            0.30);
        double windChance = Math.Clamp(
            0.14 + environment.AtmosphereDensity * 0.10,
            0.10,
            0.34);
        double roll = Unit(seed, 0x71UL);
        if (roll < toxicChance)
        {
            return PlanetWeatherKind.Toxic;
        }
        if (roll < toxicChance + stormChance)
        {
            return PlanetWeatherKind.Storm;
        }
        if (roll < toxicChance + stormChance + windChance)
        {
            return PlanetWeatherKind.Wind;
        }
        return PlanetWeatherKind.Clear;
    }

    private static double LocalHour(
        PlanetEnvironmentProfile environment,
        double gameHours) => NormalizeHours(
            gameHours + PlanetPhaseHours(environment.Seed));

    private static double PlanetPhaseHours(long seed) =>
        Unit(unchecked((ulong)seed), 0x91UL) * 24.0;

    private static double NormalizeHours(double hours)
    {
        double result = hours % 24.0;
        return result < 0.0 ? result + 24.0 : result;
    }

    private static double NormalizeDegrees(double degrees)
    {
        double result = degrees % 360.0;
        return result < 0.0 ? result + 360.0 : result;
    }

    private static double SmoothStep(double edge0, double edge1, double value)
    {
        double t = Math.Clamp((value - edge0) / (edge1 - edge0), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    private static double Unit(ulong seed, ulong salt)
    {
        ulong value = Mix(seed ^ salt);
        return (value >> 11) * (1.0 / 9007199254740992.0);
    }

    private static ulong Mix(ulong value)
    {
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
