using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public sealed record ProductionNetworkHudStationRow(
    string StationId,
    string DisplayName,
    int Jobs,
    int RunningJobs,
    int QueuedJobs,
    int PausedJobs,
    double EnergyRemaining,
    double EnergyCapacity)
{
    public bool IsActive => Jobs > 0;
}

public sealed record ProductionNetworkHudSnapshot(
    bool IsAvailable,
    string Error,
    int Stations,
    int Jobs,
    int RunningJobs,
    int QueuedJobs,
    int PausedJobs,
    double EnergyRemaining,
    double EnergyCapacity,
    IReadOnlyList<ProductionNetworkHudStationRow> StationRows)
{
    public static ProductionNetworkHudSnapshot Unavailable(string error)
    {
        return new ProductionNetworkHudSnapshot(
            false,
            string.IsNullOrWhiteSpace(error)
                ? "production network is not initialized"
                : error,
            0,
            0,
            0,
            0,
            0,
            0.0,
            0.0,
            Array.Empty<ProductionNetworkHudStationRow>());
    }
}

/// <summary>
/// Read-only projection of the complete gameplay production network. The HUD
/// consumes this model instead of selecting a single PortableFabricator queue,
/// so an idle but initialized network is never reported as unavailable.
/// </summary>
public static class ProductionNetworkHudModel
{
    private static readonly string[] PreferredStationOrder =
    {
        "station.portable_fabricator",
        "station.smelter",
        "station.refinery",
        "station.distillation_column",
        "station.chemical_processor"
    };

    public static ProductionNetworkHudSnapshot Build(
        ProductionNetworkRuntime network,
        IReadOnlyDictionary<string, string>? displayNames = null)
    {
        ArgumentNullException.ThrowIfNull(network);

        ProductionNetworkHudStationRow[] rows = network.Queues
            .Select(queue => new ProductionNetworkHudStationRow(
                queue.StationId,
                ResolveDisplayName(queue.StationId, displayNames),
                queue.Jobs.Count,
                queue.RunningCount,
                queue.QueuedCount,
                queue.PausedCount,
                queue.EnergyRemaining,
                queue.EnergyCapacity))
            .OrderBy(row => GetPreferredOrder(row.StationId))
            .ThenBy(row => row.StationId, StringComparer.Ordinal)
            .ToArray();

        return new ProductionNetworkHudSnapshot(
            true,
            string.Empty,
            rows.Length,
            rows.Sum(row => row.Jobs),
            rows.Sum(row => row.RunningJobs),
            rows.Sum(row => row.QueuedJobs),
            rows.Sum(row => row.PausedJobs),
            rows.Sum(row => row.EnergyRemaining),
            rows.Sum(row => row.EnergyCapacity),
            rows);
    }

    public static string FormatAggregate(ProductionNetworkHudSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.IsAvailable)
        {
            return $"Production network: unavailable ({snapshot.Error})";
        }

        return "Production network: " +
            $"stations={snapshot.Stations} • jobs={snapshot.Jobs} • " +
            $"running={snapshot.RunningJobs} • queued={snapshot.QueuedJobs} • " +
            $"paused={snapshot.PausedJobs} • " +
            $"energy={FormatEnergy(snapshot.EnergyRemaining)}/" +
            FormatEnergy(snapshot.EnergyCapacity);
    }

    public static string FormatStations(
        ProductionNetworkHudSnapshot snapshot,
        bool compact)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.IsAvailable)
        {
            return "Stations: unavailable";
        }

        IReadOnlyList<ProductionNetworkHudStationRow> visibleRows = compact
            ? snapshot.StationRows.Where(row => row.IsActive).ToArray()
            : snapshot.StationRows;
        int hiddenIdle = compact
            ? snapshot.StationRows.Count - visibleRows.Count
            : 0;
        List<string> parts = visibleRows
            .Select(FormatStation)
            .ToList();
        if (hiddenIdle > 0)
        {
            parts.Add($"+{hiddenIdle} idle stations");
        }

        return parts.Count == 0
            ? "Stations: none"
            : "Stations: " + string.Join(" • ", parts);
    }

    private static string FormatStation(ProductionNetworkHudStationRow row)
    {
        return $"{row.DisplayName} " +
            $"{FormatEnergy(row.EnergyRemaining)}/" +
            $"{FormatEnergy(row.EnergyCapacity)} " +
            $"[{row.RunningJobs}R/{row.QueuedJobs}Q/{row.PausedJobs}P]";
    }

    private static string FormatEnergy(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string ResolveDisplayName(
        string stationId,
        IReadOnlyDictionary<string, string>? displayNames)
    {
        if (displayNames is not null &&
            displayNames.TryGetValue(stationId, out string? displayName) &&
            !string.IsNullOrWhiteSpace(displayName))
        {
            return displayName;
        }

        string suffix = stationId.StartsWith("station.", StringComparison.Ordinal)
            ? stationId["station.".Length..]
            : stationId;
        return string.Concat(
            suffix.Split(
                    '_',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static int GetPreferredOrder(string stationId)
    {
        int index = Array.IndexOf(PreferredStationOrder, stationId);
        return index < 0 ? int.MaxValue : index;
    }
}
