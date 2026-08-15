using System;

/// <summary>Normative system frequencies from PDF technical specification section 38.1.</summary>
public static class SystemFrequencyPolicy
{
    public const double PhysicsHz = 60.0;
    public const double PlayerControllerHz = 60.0;
    public const double NearbyAiHz = 10.0;
    public const double DistantAiHz = 2.0;
    public const double BackgroundEconomyMinimumHz = 0.2;
    public const double BackgroundEconomyMaximumHz = 1.0;
    public const double DefaultBackgroundEconomyHz = 0.5;
    public const double TelemetryFlushHz = 2.0;
}

/// <summary>
/// Deterministic fixed-rate gate for decision/maintenance work while movement/rendering may continue every frame.
/// </summary>
public sealed class SystemFrequencyGate
{
    private readonly double _intervalSeconds;
    private double _accumulator;
    private bool _firstTick = true;

    /// <summary>Creates a gate with the requested positive frequency.</summary>
    public SystemFrequencyGate(double frequencyHz)
    {
        if (!double.IsFinite(frequencyHz) || frequencyHz <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(frequencyHz));
        }
        FrequencyHz = frequencyHz;
        _intervalSeconds = 1.0 / frequencyHz;
    }

    /// <summary>Gets the configured frequency.</summary>
    public double FrequencyHz { get; }

    /// <summary>
    /// Accumulates elapsed time and returns true when one or more scheduled ticks are due.
    /// Long stalls are collapsed to one decision update to avoid a catch-up spiral.
    /// </summary>
    public bool Consume(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }
        if (_firstTick)
        {
            _firstTick = false;
            return true;
        }
        _accumulator += deltaSeconds;
        const double boundaryTolerance = 1e-9;
        if (_accumulator + boundaryTolerance < _intervalSeconds)
        {
            return false;
        }

        // Consume every elapsed interval at once but emit only one decision tick.
        // Subtracting the interval count instead of using '%' is important here:
        // when an exact boundary is represented just below the interval, the
        // tolerance may admit the tick while modulo would keep almost a full
        // interval and cause a second tick on the next frame.
        double elapsedIntervals = Math.Max(
            1.0,
            Math.Floor((_accumulator + boundaryTolerance) / _intervalSeconds));
        _accumulator -= elapsedIntervals * _intervalSeconds;
        if (_accumulator < 0.0 && _accumulator > -boundaryTolerance)
        {
            _accumulator = 0.0;
        }
        if (_accumulator >= _intervalSeconds)
        {
            _accumulator %= _intervalSeconds;
        }
        return true;
    }

    /// <summary>Forces the next call to run immediately and clears accumulated time.</summary>
    public void Reset()
    {
        _accumulator = 0.0;
        _firstTick = true;
    }
}
