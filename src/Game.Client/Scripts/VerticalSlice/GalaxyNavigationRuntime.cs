using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public enum GalaxyStarType
{
    RedDwarf = 0,
    OrangeDwarf = 1,
    YellowStar = 2,
    WhiteStar = 3,
    BlueStar = 4,
    BinaryDecorative = 5
}

public enum GalaxyTravelActionResult
{
    Applied = 0,
    NotCommissioned = 1,
    FlightNotReady = 2,
    HyperspaceNotReady = 3,
    InvalidLocation = 4,
    SameSystem = 5,
    OutsideRange = 6,
    InsufficientFuel = 7,
    NoDestination = 8,
    RouteUnavailable = 9
}

public sealed record GalaxyPlanetDefinition(
    string PlanetId,
    string Archetype,
    int OrbitIndex,
    int MoonCount,
    bool HasAtmosphere,
    bool HasWater,
    long Seed);

public sealed record GalaxySystemDefinition(
    string SystemId,
    string DisplayName,
    string GalaxyId,
    int SectorX,
    int SectorY,
    int SectorZ,
    double PositionX,
    double PositionY,
    double PositionZ,
    GalaxyStarType StarType,
    string EconomyType,
    int DangerLevel,
    IReadOnlyList<GalaxyPlanetDefinition> Planets);

public sealed record GalaxyRoutePlan(
    bool Reachable,
    double TotalDistanceLightYears,
    IReadOnlyList<GalaxySystemDefinition> Systems);

public sealed class GalaxyNavigationRuntime
{
    public const long DefaultUniverseSeed = 2_026_080_5L;
    public const int GeneratorVersion = ProjectHorizonGenerator.Version;
    public const string PrimaryGalaxyId = "galaxy.g1";
    public const string StarterSystemId = "system.vertical_slice";
    public const double SectorScaleLightYears = 180.0;
    public const int MaximumVisitedSystems = 10_000;

    private static readonly (int X, int Y, int Z)[] NeighborOffsets =
        (from x in Enumerable.Range(-1, 3)
         from y in Enumerable.Range(-1, 3)
         from z in Enumerable.Range(-1, 3)
         where x != 0 || y != 0 || z != 0
         select (x, y, z)).ToArray();

    private static readonly string[] PlanetArchetypes =
    {
        "temperate",
        "desert",
        "frozen",
        "volcanic",
        "toxic",
        "radioactive",
        "barren",
        "oceanic",
        "gas_giant"
    };

    private static readonly string[] StarterPlanetArchetypes =
    {
        "temperate",
        "desert",
        "frozen",
        "volcanic"
    };

    private static readonly string[] EconomyTypes =
    {
        "extractive",
        "industrial",
        "scientific",
        "commercial",
        "agricultural",
        "frontier"
    };

    private readonly HashSet<string> _visitedSystemIds = new(
        StringComparer.Ordinal);
    private string _currentPlanetId = string.Empty;

    public GalaxyNavigationRuntime(long universeSeed)
    {
        if (universeSeed <= 0)
        {
            throw new InvalidOperationException(
                "Universe seed must be positive.");
        }

        UniverseSeed = universeSeed;
        CurrentSystem = GenerateSystem(0, 0, 0);
        _currentPlanetId = SelectDefaultPlanetId(CurrentSystem);
        _visitedSystemIds.Add(CurrentSystem.SystemId);
    }

    public GalaxyNavigationRuntime(GalaxyNavigationSaveData? saveData = null)
    {
        UniverseSeed = saveData?.UniverseSeed ?? DefaultUniverseSeed;
        if (UniverseSeed <= 0)
        {
            throw new InvalidOperationException(
                "Universe seed must be positive.");
        }

        if (saveData is null)
        {
            CurrentSystem = GenerateSystem(0, 0, 0);
            _currentPlanetId = SelectDefaultPlanetId(CurrentSystem);
            _visitedSystemIds.Add(CurrentSystem.SystemId);
            return;
        }

        ValidateSaveData(saveData);
        CurrentSystem = GenerateSystem(
            saveData.CurrentSectorX,
            saveData.CurrentSectorY,
            saveData.CurrentSectorZ);
        if (!string.Equals(
            CurrentSystem.SystemId,
            saveData.CurrentSystemId,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Saved current system does not match deterministic generation.");
        }

        _currentPlanetId = ResolveSavedPlanetId(
            CurrentSystem,
            saveData.CurrentPlanetId);

        SelectedDestination = string.IsNullOrWhiteSpace(
            saveData.SelectedDestinationSystemId)
            ? null
            : GenerateSystem(
                saveData.SelectedSectorX,
                saveData.SelectedSectorY,
                saveData.SelectedSectorZ);
        if (SelectedDestination is not null && !string.Equals(
            SelectedDestination.SystemId,
            saveData.SelectedDestinationSystemId,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Saved destination does not match deterministic generation.");
        }

        JumpCount = saveData.JumpCount;
        TotalDistanceLightYears = saveData.TotalDistanceLightYears;
        foreach (string systemId in saveData.VisitedSystemIds)
        {
            _visitedSystemIds.Add(systemId);
        }

        _visitedSystemIds.Add(CurrentSystem.SystemId);
    }

    public long UniverseSeed { get; }

    public GalaxySystemDefinition CurrentSystem { get; private set; }

    public GalaxySystemDefinition? SelectedDestination { get; private set; }

    public int JumpCount { get; private set; }

    public double TotalDistanceLightYears { get; private set; }

    public IReadOnlyCollection<string> VisitedSystemIds =>
        _visitedSystemIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();

    public string CurrentPlanetId => _currentPlanetId;

    public GalaxyPlanetDefinition CurrentPlanet => CurrentSystem.Planets
        .First(planet => string.Equals(
            planet.PlanetId,
            _currentPlanetId,
            StringComparison.Ordinal));

    public GalaxySystemDefinition LoadSystemForDeveloper(
        int sectorX,
        int sectorY,
        int sectorZ)
    {
        CurrentSystem = GenerateSystem(sectorX, sectorY, sectorZ);
        _currentPlanetId = SelectDefaultPlanetId(CurrentSystem);
        SelectedDestination = null;
        _visitedSystemIds.Add(CurrentSystem.SystemId);
        return CurrentSystem;
    }

    public GalaxySystemDefinition GenerateSystem(
        int sectorX,
        int sectorY,
        int sectorZ)
    {
        ulong hash = HashCoordinates(sectorX, sectorY, sectorZ, 0x51UL);
        string systemId = sectorX == 0 && sectorY == 0 && sectorZ == 0
            ? StarterSystemId
            : $"system.g1.x{EncodeCoordinate(sectorX)}_" +
              $"y{EncodeCoordinate(sectorY)}_z{EncodeCoordinate(sectorZ)}";
        double jitterX = ToSignedUnit(hash) * 32.0;
        double jitterY = ToSignedUnit(Mix(hash + 0x91UL)) * 32.0;
        double jitterZ = ToSignedUnit(Mix(hash + 0xD3UL)) * 32.0;
        GalaxyStarType starType = SelectStarType(hash);
        bool starterSystem = string.Equals(
            systemId,
            StarterSystemId,
            StringComparison.Ordinal);
        int planetCount = starterSystem
            ? StarterPlanetArchetypes.Length
            : 1 + (int)((hash >> 9) % 8UL);
        List<GalaxyPlanetDefinition> planets = new(planetCount);
        for (int index = 0; index < planetCount; index++)
        {
            ulong planetHash = Mix(hash + (ulong)(index + 1) * 0x9E3779B9UL);
            string archetype = starterSystem
                ? StarterPlanetArchetypes[index]
                : PlanetArchetypes[
                    (int)(planetHash % (ulong)PlanetArchetypes.Length)];
            bool gasGiant = string.Equals(
                archetype,
                "gas_giant",
                StringComparison.Ordinal);
            bool oceanic = string.Equals(
                archetype,
                "oceanic",
                StringComparison.Ordinal);
            bool frozen = string.Equals(
                archetype,
                "frozen",
                StringComparison.Ordinal);
            bool barren = string.Equals(
                archetype,
                "barren",
                StringComparison.Ordinal);
            bool volcanic = string.Equals(
                archetype,
                "volcanic",
                StringComparison.Ordinal);
            bool hasAtmosphere = starterSystem
                ? !barren
                : gasGiant || !barren ||
                    ((planetHash >> 13) & 1UL) == 1UL;
            bool hasWater = starterSystem
                ? oceanic || frozen || string.Equals(
                    archetype,
                    "temperate",
                    StringComparison.Ordinal)
                : oceanic || frozen ||
                    (!gasGiant && !volcanic &&
                     ((planetHash >> 17) % 5UL) == 0UL);
            string planetId = starterSystem && index == 0
                ? StarterRepairSnapshotFactory.PlanetId
                : $"planet.{GetSystemSuffix(systemId)}.{index + 1:00}";
            planets.Add(new GalaxyPlanetDefinition(
                planetId,
                archetype,
                index + 1,
                (int)((planetHash >> 21) % 5UL),
                hasAtmosphere,
                hasWater,
                (long)(planetHash & 0x7FFF_FFFF_FFFF_FFFFUL)));
        }

        return new GalaxySystemDefinition(
            systemId,
            BuildSystemName(hash, sectorX, sectorY, sectorZ),
            PrimaryGalaxyId,
            sectorX,
            sectorY,
            sectorZ,
            sectorX * SectorScaleLightYears + jitterX,
            sectorY * SectorScaleLightYears + jitterY,
            sectorZ * SectorScaleLightYears + jitterZ,
            starType,
            EconomyTypes[(int)((hash >> 31) % (ulong)EconomyTypes.Length)],
            1 + (int)((hash >> 37) % 5UL),
            planets);
    }

    public bool TrySelectCurrentPlanet(
        string planetId,
        out string result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planetId);
        GalaxyPlanetDefinition? planet = CurrentSystem.Planets.FirstOrDefault(
            candidate => string.Equals(
                candidate.PlanetId,
                planetId,
                StringComparison.Ordinal));
        if (planet is null)
        {
            result = $"planet {planetId} does not belong to the current system";
            return false;
        }

        if (string.Equals(
            planet.Archetype,
            "gas_giant",
            StringComparison.Ordinal))
        {
            result = $"planet {planetId} is a non-landable gas giant";
            return false;
        }

        _currentPlanetId = planet.PlanetId;
        result = $"current planet selected: {planet.PlanetId}";
        return true;
    }

    public IReadOnlyList<GalaxySystemDefinition> GetNearbySystems(
        int radius,
        int maximumCount = 25)
    {
        if (radius is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(radius));
        }

        return (from x in Enumerable.Range(
                    CurrentSystem.SectorX - radius,
                    radius * 2 + 1)
                from y in Enumerable.Range(
                    CurrentSystem.SectorY - radius,
                    radius * 2 + 1)
                from z in Enumerable.Range(
                    CurrentSystem.SectorZ - radius,
                    radius * 2 + 1)
                let system = GenerateSystem(x, y, z)
                where !string.Equals(
                    system.SystemId,
                    CurrentSystem.SystemId,
                    StringComparison.Ordinal)
                orderby Distance(CurrentSystem, system), system.SystemId
                select system)
            .Take(Math.Max(1, maximumCount))
            .ToArray();
    }

    public void SelectDestination(GalaxySystemDefinition destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        SelectedDestination = destination;
    }

    public GalaxyRoutePlan PlanRoute(
        GalaxySystemDefinition destination,
        double maximumJumpRangeLightYears)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!double.IsFinite(maximumJumpRangeLightYears) ||
            maximumJumpRangeLightYears <= 0.0)
        {
            return new GalaxyRoutePlan(false, 0.0, Array.Empty<GalaxySystemDefinition>());
        }

        if (string.Equals(
            destination.SystemId,
            CurrentSystem.SystemId,
            StringComparison.Ordinal))
        {
            return new GalaxyRoutePlan(
                true,
                0.0,
                new[] { CurrentSystem });
        }

        (int X, int Y, int Z) start = (
            CurrentSystem.SectorX,
            CurrentSystem.SectorY,
            CurrentSystem.SectorZ);
        (int X, int Y, int Z) goal = (
            destination.SectorX,
            destination.SectorY,
            destination.SectorZ);
        PriorityQueue<(int X, int Y, int Z), double> frontier = new();
        Dictionary<(int X, int Y, int Z), double> cost = new();
        Dictionary<(int X, int Y, int Z), (int X, int Y, int Z)> previous =
            new();
        frontier.Enqueue(start, 0.0);
        cost[start] = 0.0;
        int expansions = 0;
        while (frontier.Count > 0 && expansions < 30_000)
        {
            (int X, int Y, int Z) current = frontier.Dequeue();
            if (current == goal)
            {
                break;
            }

            expansions++;
            GalaxySystemDefinition currentSystem = GenerateSystem(
                current.X,
                current.Y,
                current.Z);
            foreach ((int dx, int dy, int dz) in NeighborOffsets)
            {
                (int X, int Y, int Z) next = (
                    current.X + dx,
                    current.Y + dy,
                    current.Z + dz);
                if (Math.Abs(next.X - start.X) > 12 ||
                    Math.Abs(next.Y - start.Y) > 12 ||
                    Math.Abs(next.Z - start.Z) > 12)
                {
                    continue;
                }

                GalaxySystemDefinition nextSystem = GenerateSystem(
                    next.X,
                    next.Y,
                    next.Z);
                double edge = Distance(currentSystem, nextSystem);
                if (edge > maximumJumpRangeLightYears + 0.0001)
                {
                    continue;
                }

                double nextCost = cost[current] + edge;
                if (cost.TryGetValue(next, out double known) &&
                    known <= nextCost)
                {
                    continue;
                }

                cost[next] = nextCost;
                previous[next] = current;
                double heuristic = Distance(nextSystem, destination);
                frontier.Enqueue(next, nextCost + heuristic);
            }
        }

        if (!cost.ContainsKey(goal))
        {
            return new GalaxyRoutePlan(false, 0.0, Array.Empty<GalaxySystemDefinition>());
        }

        List<(int X, int Y, int Z)> coordinates = new() { goal };
        (int X, int Y, int Z) cursor = goal;
        while (cursor != start)
        {
            cursor = previous[cursor];
            coordinates.Add(cursor);
        }

        coordinates.Reverse();
        GalaxySystemDefinition[] systems = coordinates
            .Select(value => GenerateSystem(value.X, value.Y, value.Z))
            .ToArray();
        return new GalaxyRoutePlan(true, cost[goal], systems);
    }

    public GalaxyTravelActionResult TryJumpToSelected(
        ShipSystemsRuntime shipSystems,
        StageOneVoyageLocation voyageLocation,
        out string result)
    {
        ArgumentNullException.ThrowIfNull(shipSystems);
        if (SelectedDestination is null)
        {
            result = GameLocalizationService.Text("ui.galaxy.no_selected");
            return GalaxyTravelActionResult.NoDestination;
        }

        if (!shipSystems.Commissioned)
        {
            result = GameLocalizationService.Text("ui.galaxy.ship_not_commissioned");
            return GalaxyTravelActionResult.NotCommissioned;
        }

        if (!shipSystems.FlightReady)
        {
            result = GameLocalizationService.Text("ui.galaxy.ship_not_ready");
            return GalaxyTravelActionResult.FlightNotReady;
        }

        if (!shipSystems.HyperspaceReady)
        {
            result = GameLocalizationService.Text("ui.galaxy.hyperdrive_required");
            return GalaxyTravelActionResult.HyperspaceNotReady;
        }

        if (voyageLocation != StageOneVoyageLocation.OrbitalStation)
        {
            result = GameLocalizationService.Text("ui.galaxy.orbital_only");
            return GalaxyTravelActionResult.InvalidLocation;
        }

        if (string.Equals(
            SelectedDestination.SystemId,
            CurrentSystem.SystemId,
            StringComparison.Ordinal))
        {
            result = GameLocalizationService.Text("ui.galaxy.current_destination");
            return GalaxyTravelActionResult.SameSystem;
        }

        ShipEffectiveStats stats = shipSystems.GetEffectiveStats();
        GalaxyRoutePlan route = PlanRoute(
            SelectedDestination,
            stats.HyperdriveRange);
        if (!route.Reachable || route.Systems.Count < 2)
        {
            result = GameLocalizationService.Format("ui.galaxy.no_route", ("range", stats.HyperdriveRange.ToString("0.#", CultureInfo.InvariantCulture)));
            return GalaxyTravelActionResult.RouteUnavailable;
        }

        GalaxySystemDefinition next = route.Systems[1];
        double distance = Distance(CurrentSystem, next);
        if (distance > stats.HyperdriveRange + 0.0001)
        {
            result = GameLocalizationService.Format("ui.galaxy.waypoint_out_range", ("distance", distance.ToString("0.#", CultureInfo.InvariantCulture)), ("range", stats.HyperdriveRange.ToString("0.#", CultureInfo.InvariantCulture)));
            return GalaxyTravelActionResult.OutsideRange;
        }

        double fuelCost = CalculateFuelCost(distance);
        if (!shipSystems.TryConsumeFuel(fuelCost, out string fuelResult))
        {
            result = fuelResult;
            return GalaxyTravelActionResult.InsufficientFuel;
        }

        CurrentSystem = next;
        _currentPlanetId = SelectDefaultPlanetId(CurrentSystem);
        JumpCount++;
        TotalDistanceLightYears += distance;
        _visitedSystemIds.Add(CurrentSystem.SystemId);
        result = GameLocalizationService.Format(
            "ui.galaxy.jump_result",
            ("jump", JumpCount),
            ("system", CurrentSystem.DisplayName),
            ("fuel", shipSystems.Fuel.ToString("0.#", CultureInfo.InvariantCulture)),
            ("visited", _visitedSystemIds.Count));
        return GalaxyTravelActionResult.Applied;
    }

    public GalaxyNavigationSaveData CreateSaveData()
    {
        return new GalaxyNavigationSaveData(
            UniverseSeed,
            PrimaryGalaxyId,
            CurrentSystem.SystemId,
            CurrentSystem.SectorX,
            CurrentSystem.SectorY,
            CurrentSystem.SectorZ,
            SelectedDestination?.SystemId ?? string.Empty,
            SelectedDestination?.SectorX ?? 0,
            SelectedDestination?.SectorY ?? 0,
            SelectedDestination?.SectorZ ?? 0,
            JumpCount,
            TotalDistanceLightYears,
            _visitedSystemIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            CurrentPlanetId);
    }

    public string BuildSummary()
    {
        return $"galaxy={CurrentSystem.GalaxyId}; system={CurrentSystem.SystemId}; " +
            $"sector={CurrentSystem.SectorX},{CurrentSystem.SectorY},{CurrentSystem.SectorZ}; " +
            $"star={CurrentSystem.StarType}; planets={CurrentSystem.Planets.Count}; " +
            $"planet={CurrentPlanetId}; visited={_visitedSystemIds.Count}; " +
            $"jumps={JumpCount}; " +
            $"distance={TotalDistanceLightYears.ToString("0.0", CultureInfo.InvariantCulture)}ly";
    }

    public static double Distance(
        GalaxySystemDefinition left,
        GalaxySystemDefinition right)
    {
        double x = right.PositionX - left.PositionX;
        double y = right.PositionY - left.PositionY;
        double z = right.PositionZ - left.PositionZ;
        return Math.Sqrt(x * x + y * y + z * z);
    }

    public static double CalculateFuelCost(double distanceLightYears)
    {
        if (!double.IsFinite(distanceLightYears) || distanceLightYears <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(distanceLightYears));
        }

        return Math.Max(4.0, Math.Ceiling(distanceLightYears / 120.0) * 2.0);
    }

    private static string SelectDefaultPlanetId(
        GalaxySystemDefinition system)
    {
        GalaxyPlanetDefinition? landable = system.Planets.FirstOrDefault(planet =>
            !string.Equals(
                planet.Archetype,
                "gas_giant",
                StringComparison.Ordinal));
        return (landable ?? system.Planets.First()).PlanetId;
    }

    private static string ResolveSavedPlanetId(
        GalaxySystemDefinition system,
        string savedPlanetId)
    {
        if (string.IsNullOrWhiteSpace(savedPlanetId))
        {
            return SelectDefaultPlanetId(system);
        }

        GalaxyPlanetDefinition? savedPlanet = system.Planets.FirstOrDefault(
            planet => string.Equals(
                planet.PlanetId,
                savedPlanetId,
                StringComparison.Ordinal));
        if (savedPlanet is null || string.Equals(
                savedPlanet.Archetype,
                "gas_giant",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Saved current planet does not match a landable deterministic planet.");
        }

        return savedPlanet.PlanetId;
    }

    private void ValidateSaveData(GalaxyNavigationSaveData saveData)
    {
        if (saveData.UniverseSeed <= 0 ||
            !string.Equals(
                saveData.GalaxyId,
                PrimaryGalaxyId,
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(saveData.CurrentSystemId) ||
            (string.IsNullOrWhiteSpace(saveData.SelectedDestinationSystemId) &&
             (saveData.SelectedSectorX != 0 ||
              saveData.SelectedSectorY != 0 ||
              saveData.SelectedSectorZ != 0)) ||
            saveData.JumpCount < 0 ||
            !double.IsFinite(saveData.TotalDistanceLightYears) ||
            saveData.TotalDistanceLightYears < 0.0 ||
            saveData.VisitedSystemIds is null ||
            saveData.VisitedSystemIds.Count is < 1 or > MaximumVisitedSystems ||
            saveData.VisitedSystemIds.Any(string.IsNullOrWhiteSpace) ||
            saveData.VisitedSystemIds.Distinct(StringComparer.Ordinal).Count() !=
                saveData.VisitedSystemIds.Count ||
            !saveData.VisitedSystemIds.Contains(
                saveData.CurrentSystemId,
                StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "Galaxy navigation save data is invalid.");
        }
    }

    private ulong HashCoordinates(
        int sectorX,
        int sectorY,
        int sectorZ,
        ulong salt)
    {
        ulong value = unchecked((ulong)UniverseSeed) ^ salt;
        value = Mix(value ^ unchecked((ulong)(long)sectorX * 0x9E3779B185EBCA87UL));
        value = Mix(value ^ unchecked((ulong)(long)sectorY * 0xC2B2AE3D27D4EB4FUL));
        value = Mix(value ^ unchecked((ulong)(long)sectorZ * 0x165667B19E3779F9UL));
        return value;
    }

    private static ulong Mix(ulong value)
    {
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        value ^= value >> 31;
        return value;
    }

    private static double ToSignedUnit(ulong value)
    {
        return ((value & 0xFFFFUL) / 32767.5) - 1.0;
    }

    private static GalaxyStarType SelectStarType(ulong hash)
    {
        int value = (int)(hash % 100UL);
        return value switch
        {
            < 25 => GalaxyStarType.RedDwarf,
            < 45 => GalaxyStarType.OrangeDwarf,
            < 65 => GalaxyStarType.YellowStar,
            < 80 => GalaxyStarType.WhiteStar,
            < 92 => GalaxyStarType.BlueStar,
            _ => GalaxyStarType.BinaryDecorative
        };
    }

    private static string BuildSystemName(
        ulong hash,
        int sectorX,
        int sectorY,
        int sectorZ)
    {
        string[] first =
        {
            "Aster", "Boreal", "Cygnus", "Dawn", "Eidolon", "Frontier",
            "Galen", "Helix", "Icarus", "Juno", "Kepler", "Lumen"
        };
        string[] second =
        {
            "Reach", "Gate", "Drift", "Crown", "Haven", "March",
            "Span", "Rise", "Belt", "Cross", "Point", "Vale"
        };
        return $"{first[(int)(hash % (ulong)first.Length)]} " +
            $"{second[(int)((hash >> 8) % (ulong)second.Length)]} " +
            $"{Math.Abs(sectorX * 31 + sectorY * 17 + sectorZ * 13):000}";
    }

    private static string EncodeCoordinate(int value)
    {
        return value < 0
            ? "n" + Math.Abs(value).ToString(CultureInfo.InvariantCulture)
            : "p" + value.ToString(CultureInfo.InvariantCulture);
    }

    private static string GetSystemSuffix(string systemId)
    {
        return systemId.StartsWith("system.", StringComparison.Ordinal)
            ? systemId[7..].Replace('.', '_')
            : systemId.Replace('.', '_');
    }
}
