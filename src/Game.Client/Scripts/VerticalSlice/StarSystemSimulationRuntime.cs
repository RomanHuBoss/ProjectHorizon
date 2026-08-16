using System;
using System.Collections.Generic;
using System.Linq;

public readonly record struct SystemDouble3(double X, double Y, double Z)
{
    public static SystemDouble3 Zero => new(0.0, 0.0, 0.0);

    public static SystemDouble3 operator +(SystemDouble3 left, SystemDouble3 right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    public static SystemDouble3 operator -(SystemDouble3 left, SystemDouble3 right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    public double Length() => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));
}

public enum StarSystemBodyKind
{
    Star = 0,
    Planet = 1,
    Moon = 2,
    Station = 3,
    ShipContact = 4
}

public enum StarSystemRepresentation
{
    Statistical = 0,
    Marker = 1,
    Proxy = 2,
    DetailedPlanet = 3
}

public sealed record StarSystemBodyDefinition(
    string BodyId,
    string ParentBodyId,
    StarSystemBodyKind Kind,
    string Archetype,
    double OrbitRadius,
    double OrbitPeriodSeconds,
    double PhaseRadians,
    double InclinationRadians,
    double VisualRadius,
    long Seed);

public sealed record StarSystemBodyState(
    StarSystemBodyDefinition Definition,
    SystemDouble3 Position,
    StarSystemRepresentation Representation,
    double DistanceToFocus);

public sealed record StarSystemSimulationSnapshot(
    string SystemId,
    double SimulationSeconds,
    string FocusBodyId,
    string? DetailedPlanetId,
    IReadOnlyList<StarSystemBodyState> Bodies)
{
    public int DetailedPlanetCount => Bodies.Count(body =>
        body.Representation == StarSystemRepresentation.DetailedPlanet);

    public int ProxyCount => Bodies.Count(body =>
        body.Representation == StarSystemRepresentation.Proxy);

    public int MarkerCount => Bodies.Count(body =>
        body.Representation == StarSystemRepresentation.Marker);

    public int StatisticalCount => Bodies.Count(body =>
        body.Representation == StarSystemRepresentation.Statistical);
}

public sealed class StarSystemSimulationRuntime
{
    // TASK-178.2: the previous 120x clock made a 110 s moon complete an
    // apparent orbit in under one real second. The runtime clock now advances
    // at real time; authored orbital periods provide the deliberate gameplay
    // compression without turning the system into a centrifuge.
    public const double OrbitTimeScale = 1.0;
    public const double ProxyDistance = 4200.0;
    public const double MarkerDistance = 8000.0;
    public const double MinimumPlanetOrbitRadius = 1800.0;
    public const double PlanetOrbitSpacing = 1200.0;
    public const double MinimumMoonOrbitRadius = 520.0;
    public const double MoonOrbitSpacing = 320.0;
    public const double MinimumMoonOrbitPeriodSeconds = 1800.0;
    public const int MaximumShipContacts = 16;

    private readonly GalaxySystemDefinition _system;
    private readonly Dictionary<string, StarSystemBodyDefinition> _definitions;
    private readonly StarSystemBodyDefinition[] _orderedDefinitions;
    private double _simulationSeconds;

    public StarSystemSimulationRuntime(
        GalaxySystemDefinition system,
        double initialSimulationSeconds = 0.0)
    {
        ArgumentNullException.ThrowIfNull(system);
        if (!double.IsFinite(initialSimulationSeconds) ||
            initialSimulationSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialSimulationSeconds));
        }

        _system = system;
        _simulationSeconds = initialSimulationSeconds;
        _orderedDefinitions = BuildDefinitions(system).ToArray();
        _definitions = _orderedDefinitions.ToDictionary(
            definition => definition.BodyId,
            StringComparer.Ordinal);
        ValidateDefinitions();
    }

    public string SystemId => _system.SystemId;

    public double SimulationSeconds => _simulationSeconds;

    public IReadOnlyList<StarSystemBodyDefinition> Definitions =>
        _orderedDefinitions;

    public int PlanetCount => _orderedDefinitions.Count(definition =>
        definition.Kind == StarSystemBodyKind.Planet);

    public int MoonCount => _orderedDefinitions.Count(definition =>
        definition.Kind == StarSystemBodyKind.Moon);

    public int StationCount => _orderedDefinitions.Count(definition =>
        definition.Kind == StarSystemBodyKind.Station);

    public int ShipContactCount => _orderedDefinitions.Count(definition =>
        definition.Kind == StarSystemBodyKind.ShipContact);

    public void Advance(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }

        _simulationSeconds += deltaSeconds * OrbitTimeScale;
    }

    public StarSystemSimulationSnapshot CreateSnapshot(
        string focusBodyId,
        string? detailedPlanetId,
        bool detailedPlanetRequested)
    {
        if (!_definitions.ContainsKey(focusBodyId))
        {
            throw new InvalidOperationException(
                $"Unknown focus body {focusBodyId} in system {SystemId}.");
        }

        if (!string.IsNullOrWhiteSpace(detailedPlanetId))
        {
            if (!_definitions.TryGetValue(
                    detailedPlanetId,
                    out StarSystemBodyDefinition? detailed) ||
                detailed.Kind != StarSystemBodyKind.Planet)
            {
                throw new InvalidOperationException(
                    $"Detailed body {detailedPlanetId} is not a planet in {SystemId}.");
            }
        }

        Dictionary<string, SystemDouble3> positions = EvaluatePositions(
            _simulationSeconds);
        SystemDouble3 focus = positions[focusBodyId];
        List<StarSystemBodyState> states = new(_orderedDefinitions.Length);
        foreach (StarSystemBodyDefinition definition in _orderedDefinitions)
        {
            SystemDouble3 position = positions[definition.BodyId];
            double distance = (position - focus).Length();
            StarSystemRepresentation representation = ResolveRepresentation(
                definition,
                distance,
                detailedPlanetId,
                detailedPlanetRequested);
            states.Add(new StarSystemBodyState(
                definition,
                position,
                representation,
                distance));
        }

        return new StarSystemSimulationSnapshot(
            SystemId,
            _simulationSeconds,
            focusBodyId,
            detailedPlanetRequested ? detailedPlanetId : null,
            states);
    }

    public SystemDouble3 EvaluateBodyPosition(
        string bodyId,
        double simulationSeconds)
    {
        if (!_definitions.ContainsKey(bodyId))
        {
            throw new InvalidOperationException(
                $"Unknown body {bodyId} in system {SystemId}.");
        }

        if (!double.IsFinite(simulationSeconds) || simulationSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(simulationSeconds));
        }

        return EvaluatePositions(simulationSeconds)[bodyId];
    }

    private Dictionary<string, SystemDouble3> EvaluatePositions(
        double simulationSeconds)
    {
        Dictionary<string, SystemDouble3> positions = new(
            StringComparer.Ordinal);
        foreach (StarSystemBodyDefinition definition in _orderedDefinitions)
        {
            if (definition.Kind == StarSystemBodyKind.Star)
            {
                positions[definition.BodyId] = SystemDouble3.Zero;
                continue;
            }

            SystemDouble3 parent = positions.TryGetValue(
                    definition.ParentBodyId,
                    out SystemDouble3 parentPosition)
                ? parentPosition
                : SystemDouble3.Zero;
            double angularSpeed = definition.OrbitPeriodSeconds <= 0.0
                ? 0.0
                : (Math.PI * 2.0) / definition.OrbitPeriodSeconds;
            double angle = definition.PhaseRadians +
                (simulationSeconds * angularSpeed);
            double horizontalRadius = definition.OrbitRadius;
            double x = Math.Cos(angle) * horizontalRadius;
            double orbitalSin = Math.Sin(angle) * horizontalRadius;
            double inclinationCos = Math.Cos(definition.InclinationRadians);
            double inclinationSin = Math.Sin(definition.InclinationRadians);
            double y = orbitalSin * inclinationSin;
            double z = orbitalSin * inclinationCos;
            positions[definition.BodyId] = parent + new SystemDouble3(x, y, z);
        }

        return positions;
    }

    private static StarSystemRepresentation ResolveRepresentation(
        StarSystemBodyDefinition definition,
        double distance,
        string? detailedPlanetId,
        bool detailedPlanetRequested)
    {
        if (detailedPlanetRequested &&
            definition.Kind == StarSystemBodyKind.Planet &&
            string.Equals(
                definition.BodyId,
                detailedPlanetId,
                StringComparison.Ordinal))
        {
            return StarSystemRepresentation.DetailedPlanet;
        }

        if (definition.Kind == StarSystemBodyKind.ShipContact)
        {
            return distance <= MarkerDistance
                ? StarSystemRepresentation.Marker
                : StarSystemRepresentation.Statistical;
        }

        if (distance <= ProxyDistance)
        {
            return StarSystemRepresentation.Proxy;
        }

        if (distance <= MarkerDistance)
        {
            return StarSystemRepresentation.Marker;
        }

        return StarSystemRepresentation.Statistical;
    }

    private static IEnumerable<StarSystemBodyDefinition> BuildDefinitions(
        GalaxySystemDefinition system)
    {
        string starId = $"{system.SystemId}.star";
        long starSeed = StableSeed(system.SystemId, 0x15A1UL);
        yield return new StarSystemBodyDefinition(
            starId,
            string.Empty,
            StarSystemBodyKind.Star,
            system.StarType.ToString(),
            0.0,
            0.0,
            0.0,
            0.0,
            420.0,
            starSeed);

        foreach (GalaxyPlanetDefinition planet in system.Planets
                     .OrderBy(item => item.OrbitIndex))
        {
            ulong hash = Mix((ulong)Math.Max(1L, planet.Seed));
            double orbitRadius = MinimumPlanetOrbitRadius +
                ((planet.OrbitIndex - 1) * PlanetOrbitSpacing) +
                ((hash & 0xFUL) * 12.0);
            double period = 7200.0 + (planet.OrbitIndex * 4200.0) +
                ((hash >> 8) % 1800UL);
            double phase = ToUnit(hash >> 16) * Math.PI * 2.0;
            double inclination = (ToUnit(hash >> 24) - 0.5) * 0.10;
            double visualRadius = string.Equals(
                    planet.Archetype,
                    "gas_giant",
                    StringComparison.Ordinal)
                ? 300.0
                : 150.0 + ((hash >> 32) % 61UL);
            yield return new StarSystemBodyDefinition(
                planet.PlanetId,
                starId,
                StarSystemBodyKind.Planet,
                planet.Archetype,
                orbitRadius,
                period,
                phase,
                inclination,
                visualRadius,
                planet.Seed);

            for (int moonIndex = 0; moonIndex < planet.MoonCount; moonIndex++)
            {
                ulong moonHash = Mix(hash +
                    ((ulong)(moonIndex + 1) * 0x9E3779B97F4A7C15UL));
                string moonId = $"{planet.PlanetId}.moon.{moonIndex + 1:00}";
                yield return new StarSystemBodyDefinition(
                    moonId,
                    planet.PlanetId,
                    StarSystemBodyKind.Moon,
                    "moon",
                    MinimumMoonOrbitRadius + (moonIndex * MoonOrbitSpacing) +
                        ((moonHash & 0x3UL) * 8.0),
                    MinimumMoonOrbitPeriodSeconds + (moonIndex * 900.0) +
                        ((moonHash >> 9) % 420UL),
                    ToUnit(moonHash >> 17) * Math.PI * 2.0,
                    (ToUnit(moonHash >> 25) - 0.5) * 0.20,
                    28.0 + ((moonHash >> 31) % 15UL),
                    (long)(moonHash & 0x7FFF_FFFF_FFFF_FFFFUL));
            }
        }

        GalaxyPlanetDefinition stationPlanet = system.Planets[0];
        int stationCount = 1 + Math.Abs(system.DangerLevel +
            system.EconomyType.Length) % 3;
        string[] stationArchetypes =
        {
            "ring",
            "spindle",
            "habitat"
        };
        for (int index = 0; index < stationCount; index++)
        {
            ulong stationHash = Mix((ulong)Math.Max(1L, stationPlanet.Seed) +
                ((ulong)(index + 1) * 0xD6E8FEB86659FD93UL));
            string stationId = $"{system.SystemId}.station.{index + 1:00}";
            yield return new StarSystemBodyDefinition(
                stationId,
                stationPlanet.PlanetId,
                StarSystemBodyKind.Station,
                stationArchetypes[index % stationArchetypes.Length],
                82.0 + (index * 30.0),
                480.0 + (index * 180.0),
                ToUnit(stationHash) * Math.PI * 2.0,
                0.04 + (index * 0.02),
                7.5 + (index * 1.5),
                (long)(stationHash & 0x7FFF_FFFF_FFFF_FFFFUL));
        }

        int shipContacts = Math.Clamp(
            4 + system.DangerLevel + stationCount,
            4,
            MaximumShipContacts);
        for (int index = 0; index < shipContacts; index++)
        {
            ulong shipHash = Mix((ulong)Math.Max(1L, stationPlanet.Seed) +
                ((ulong)(index + 1) * 0xA24BAED4963EE407UL));
            string shipId = $"{system.SystemId}.traffic.{index + 1:00}";
            yield return new StarSystemBodyDefinition(
                shipId,
                $"{system.SystemId}.station.{(index % stationCount) + 1:00}",
                StarSystemBodyKind.ShipContact,
                index % 4 == 0 ? "security" :
                index % 3 == 0 ? "trader" : "civilian",
                24.0 + ((shipHash >> 8) % 22UL),
                120.0 + ((shipHash >> 15) % 120UL),
                ToUnit(shipHash >> 23) * Math.PI * 2.0,
                (ToUnit(shipHash >> 31) - 0.5) * 0.5,
                0.85,
                (long)(shipHash & 0x7FFF_FFFF_FFFF_FFFFUL));
        }
    }

    private void ValidateDefinitions()
    {
        if (PlanetCount is < 1 or > 8)
        {
            throw new InvalidOperationException(
                $"System {SystemId} must contain 1..8 planets, got {PlanetCount}.");
        }

        foreach (StarSystemBodyDefinition definition in _orderedDefinitions)
        {
            if (definition.Kind == StarSystemBodyKind.Star)
            {
                continue;
            }

            if (!_definitions.ContainsKey(definition.ParentBodyId))
            {
                throw new InvalidOperationException(
                    $"Body {definition.BodyId} has missing parent {definition.ParentBodyId}.");
            }
        }

        foreach (GalaxyPlanetDefinition planet in _system.Planets)
        {
            int moons = _orderedDefinitions.Count(definition =>
                definition.Kind == StarSystemBodyKind.Moon &&
                string.Equals(
                    definition.ParentBodyId,
                    planet.PlanetId,
                    StringComparison.Ordinal));
            if (moons != planet.MoonCount || moons is < 0 or > 4)
            {
                throw new InvalidOperationException(
                    $"Planet {planet.PlanetId} moon coverage mismatch: {moons}/{planet.MoonCount}.");
            }
        }
    }

    private static long StableSeed(string value, ulong salt)
    {
        ulong hash = 1469598103934665603UL ^ salt;
        foreach (char character in value)
        {
            hash ^= character;
            hash *= 1099511628211UL;
        }
        return (long)(Mix(hash) & 0x7FFF_FFFF_FFFF_FFFFUL);
    }

    private static ulong Mix(ulong value)
    {
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    private static double ToUnit(ulong value) =>
        (value & 0x00FF_FFFFUL) / (double)0x0100_0000UL;
}
