using System;
using System.Collections.Generic;
using System.Linq;

public sealed record FaunaStatisticalSpeciesSnapshot(
    string FaunaId,
    int Population,
    double Activity,
    double TerritoryPressure);

public sealed record FaunaStatisticalSnapshot(
    long Ticks,
    int Population,
    int Species,
    double MeanActivity,
    IReadOnlyList<FaunaStatisticalSpeciesSnapshot> SpeciesStates);

/// <summary>
/// Aggregate far-fauna simulation. No scene nodes are required for the 80
/// simplified population entries; only deterministic species-level statistics
/// advance at 0.5 Hz.
/// </summary>
public sealed class FaunaStatisticalSimulationRuntime
{
    private readonly Dictionary<string, SpeciesState> _species;
    private double _accumulator;
    private long _ticks;

    public FaunaStatisticalSimulationRuntime(
        IReadOnlyList<EcologyFaunaSpawn> simplifiedFauna)
    {
        ArgumentNullException.ThrowIfNull(simplifiedFauna);
        _species = simplifiedFauna
            .GroupBy(spawn => spawn.FaunaId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new SpeciesState(
                    group.Count(),
                    0.45 + ((EcologyPlanner.StableHash(group.Key) % 31) / 100.0),
                    0.25 + ((EcologyPlanner.StableHash(group.Key + ".territory") % 27) / 100.0)),
                StringComparer.Ordinal);
    }

    public long TickCount => _ticks;

    public void Tick(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }
        _accumulator += deltaSeconds;
        double interval = 1.0 / FaunaBehaviorRuntime.FarStatisticalFrequencyHz;
        while (_accumulator >= interval)
        {
            _accumulator -= interval;
            _ticks++;
            foreach ((string faunaId, SpeciesState state) in _species)
            {
                double phase = (_ticks * 0.17) +
                    ((EcologyPlanner.StableHash(faunaId) % 997) / 997.0);
                state.Activity = Math.Clamp(
                    0.52 + 0.30 * Math.Sin(phase),
                    0.10,
                    0.92);
                state.TerritoryPressure = Math.Clamp(
                    state.TerritoryPressure * 0.90 +
                    (0.20 + 0.18 * Math.Cos(phase * 0.61)) * 0.10,
                    0.05,
                    0.85);
            }
        }
    }

    public FaunaStatisticalSnapshot CreateSnapshot()
    {
        FaunaStatisticalSpeciesSnapshot[] states = _species
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new FaunaStatisticalSpeciesSnapshot(
                pair.Key,
                pair.Value.Population,
                pair.Value.Activity,
                pair.Value.TerritoryPressure))
            .ToArray();
        return new FaunaStatisticalSnapshot(
            _ticks,
            states.Sum(state => state.Population),
            states.Length,
            states.Length == 0 ? 0.0 : states.Average(state => state.Activity),
            states);
    }

    private sealed class SpeciesState
    {
        public SpeciesState(int population, double activity, double territoryPressure)
        {
            Population = population;
            Activity = activity;
            TerritoryPressure = territoryPressure;
        }

        public int Population { get; }
        public double Activity { get; set; }
        public double TerritoryPressure { get; set; }
    }
}
