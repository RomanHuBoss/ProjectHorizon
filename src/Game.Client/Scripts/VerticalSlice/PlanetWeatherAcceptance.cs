using System;
using System.Collections.Generic;
using System.Linq;

public sealed record PlanetWeatherAcceptanceReport(
    bool Passed,
    int Planets,
    bool Deterministic,
    bool DayNightCycle,
    bool WeatherVariation,
    bool HazardProfiles,
    bool SaveRestore,
    bool PlanetPhaseVariation,
    bool OverrideControl,
    int Samples,
    string Result)
{
    public string BuildOutputLine() =>
        $"TASK-166 planetary weather acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"planets={Planets}; deterministic={(Deterministic ? 1 : 0)}; " +
        $"dayNight={(DayNightCycle ? 1 : 0)}; weatherVariation={(WeatherVariation ? 1 : 0)}; " +
        $"hazards={(HazardProfiles ? 1 : 0)}; saveRestore={(SaveRestore ? 1 : 0)}; " +
        $"planetPhase={(PlanetPhaseVariation ? 1 : 0)}; override={(OverrideControl ? 1 : 0)}; " +
        $"samples={Samples}; result={Result}";
}

public static class PlanetWeatherAcceptanceRunner
{
    public static PlanetWeatherAcceptanceReport Run(
        IReadOnlyList<PlanetEnvironmentProfile> environments)
    {
        try
        {
            PlanetEnvironmentProfile[] landable = environments
                .Where(profile => profile.Landable)
                .Take(4)
                .ToArray();
            if (landable.Length == 0)
            {
                throw new InvalidOperationException("No landable planet profiles supplied.");
            }

            bool deterministic = true;
            bool dayNight = true;
            bool weatherVariation = false;
            bool hazardProfiles = false;
            int samples = 0;
            HashSet<PlanetWeatherKind> kinds = new();
            List<double> noonPhases = new();

            foreach (PlanetEnvironmentProfile environment in landable)
            {
                PlanetWeatherRuntime runtime = new(environment);
                double baseHours = runtime.GameHours;
                PlanetWeatherState first = PlanetWeatherRuntime.BuildState(environment, baseHours);
                PlanetWeatherState second = PlanetWeatherRuntime.BuildState(environment, baseHours);
                deterministic &= first == second;

                double minElevation = double.PositiveInfinity;
                double maxElevation = double.NegativeInfinity;
                for (int hour = 0; hour < 24; hour += 2)
                {
                    PlanetWeatherState state = PlanetWeatherRuntime.BuildState(
                        environment,
                        baseHours + hour);
                    minElevation = Math.Min(minElevation, state.SunElevationDegrees);
                    maxElevation = Math.Max(maxElevation, state.SunElevationDegrees);
                    samples++;
                }
                dayNight &= minElevation < -20.0 && maxElevation > 20.0;

                for (int cell = 0; cell < 96; cell++)
                {
                    PlanetWeatherState state = PlanetWeatherRuntime.BuildState(
                        environment,
                        baseHours + cell * PlanetWeatherRuntime.WeatherCellHours);
                    kinds.Add(state.Kind);
                    if (state.ToxicHazardBonus > 0.0 ||
                        state.LifeSupportDrainBonus > 0.0 ||
                        state.TemperatureHazardBonus > 0.0)
                    {
                        hazardProfiles = true;
                    }
                    samples++;
                }
                noonPhases.Add(runtime.SetLocalHour(12.0).SunAzimuthDegrees);
            }

            weatherVariation = kinds.Contains(PlanetWeatherKind.Clear) &&
                kinds.Contains(PlanetWeatherKind.Wind) &&
                kinds.Contains(PlanetWeatherKind.Storm);

            PlanetWeatherRuntime persistence = new(landable[0]);
            persistence.Advance(137.25);
            PlanetWeatherSaveData save = persistence.CreateSaveData();
            PlanetWeatherRuntime restored = new(landable[0], save);
            bool saveRestore = Math.Abs(
                restored.CreateSaveData().GameHours - save.GameHours) <= 0.000001 &&
                restored.Current == persistence.Current;

            bool planetPhase = noonPhases
                .Select(value => Math.Round(value, 3))
                .Distinct()
                .Count() > 1 || landable.Length == 1;

            PlanetWeatherRuntime overrideRuntime = new(landable[0]);
            PlanetWeatherState overrideState = overrideRuntime
                .SetDeveloperOverride(PlanetWeatherKind.Storm);
            PlanetWeatherState midnight = overrideRuntime.SetLocalHour(0.0);
            bool overrideControl = overrideState.Kind == PlanetWeatherKind.Storm &&
                midnight.Kind == PlanetWeatherKind.Storm &&
                midnight.LocalHour < 0.001 && midnight.SunElevationDegrees < 0.0;

            bool passed = deterministic && dayNight && weatherVariation &&
                hazardProfiles && saveRestore && planetPhase && overrideControl;
            return new PlanetWeatherAcceptanceReport(
                passed,
                landable.Length,
                deterministic,
                dayNight,
                weatherVariation,
                hazardProfiles,
                saveRestore,
                planetPhase,
                overrideControl,
                samples,
                passed
                    ? "deterministic time, weather, hazards and persistence verified"
                    : "one or more planetary weather invariants failed");
        }
        catch (Exception exception)
        {
            return new PlanetWeatherAcceptanceReport(
                false, 0, false, false, false, false, false, false, false, 0,
                $"acceptance exception: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
