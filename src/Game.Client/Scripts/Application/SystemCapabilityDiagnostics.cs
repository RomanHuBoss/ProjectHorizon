using System;
using System.IO;
using System.Globalization;
using Godot;

public sealed record SystemCapabilitySnapshot(
    string OperatingSystem,
    bool Is64Bit,
    int LogicalProcessorCount,
    long PhysicalMemoryBytes,
    bool PhysicalMemoryKnown,
    string RenderingMethod,
    string RenderingDriver,
    string AdapterName,
    string AdapterVendor,
    string AdapterType,
    string AdapterApiVersion,
    long VideoMemoryUsedBytes,
    long FreeStorageBytes,
    bool FreeStorageKnown,
    SystemCapabilityEvaluation Evaluation)
{
}

/// <summary>
/// Captures only portable evidence exposed by Godot/.NET. It never guesses SSD
/// type or total VRAM when the backend does not expose those values reliably.
/// </summary>
public static class SystemCapabilityDiagnostics
{
    public static SystemCapabilitySnapshot Capture()
    {
        string osName = OS.GetName();
        bool windows = string.Equals(osName, "Windows", StringComparison.OrdinalIgnoreCase);
        bool linux = string.Equals(osName, "Linux", StringComparison.OrdinalIgnoreCase);
        bool supportedOs = linux || (windows && System.Environment.OSVersion.Version.Major >= 10);
        bool is64Bit = System.Environment.Is64BitOperatingSystem && System.Environment.Is64BitProcess;
        int processors = Math.Max(1, System.Environment.ProcessorCount);
        long physicalMemory = ReadPhysicalMemoryBytes();
        bool physicalKnown = physicalMemory > 0;

        RendererProfileSnapshot renderer = RendererProfileDiagnostics.Capture();
        string adapterName = RenderingServer.GetVideoAdapterName();
        string adapterVendor = RenderingServer.GetVideoAdapterVendor();
        string adapterType = RenderingServer.GetVideoAdapterType().ToString();
        string adapterApiVersion = RenderingServer.GetVideoAdapterApiVersion();
        bool dedicated = adapterType.Contains("Discrete", StringComparison.OrdinalIgnoreCase);
        long videoMemoryUsed = ReadMonitorBytes(Performance.Monitor.RenderVideoMemUsed);

        (long freeStorage, bool storageKnown) = ReadFreeStorageBytes();
        SystemCapabilityInput input = new(
            supportedOs,
            is64Bit,
            processors,
            physicalMemory,
            physicalKnown,
            renderer.IsValidForProfile,
            renderer.IsPrimaryRenderer,
            renderer.IsCompatibilityRenderer,
            dedicated,
            VideoMemoryCapacityBytes: 0,
            VideoMemoryCapacityKnown: false,
            freeStorage,
            storageKnown,
            SsdDetected: false,
            StorageMediumKnown: false);
        SystemCapabilityEvaluation evaluation = SystemCapabilityPolicy.Evaluate(input);

        return new SystemCapabilitySnapshot(
            osName,
            is64Bit,
            processors,
            physicalMemory,
            physicalKnown,
            renderer.RenderingMethod,
            renderer.RenderingDriver,
            adapterName,
            adapterVendor,
            adapterType,
            adapterApiVersion,
            videoMemoryUsed,
            freeStorage,
            storageKnown,
            evaluation);
    }

    public static string BuildEvidence(SystemCapabilitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        SystemCapabilityEvaluation evaluation = snapshot.Evaluation;
        string status = evaluation.MinimumSatisfied ? "PASS" : "WARN";
        return
            $"TASK-206 system capability {status}: tier={evaluation.Tier}; " +
            $"os={snapshot.OperatingSystem}; x64={(snapshot.Is64Bit ? 1 : 0)}; " +
            $"cpuLogical={snapshot.LogicalProcessorCount}; ram={FormatGiB(snapshot.PhysicalMemoryBytes, snapshot.PhysicalMemoryKnown)}; " +
            $"renderer={snapshot.RenderingMethod}/{snapshot.RenderingDriver}; " +
            $"gpu={Sanitize(snapshot.AdapterName)}; vendor={Sanitize(snapshot.AdapterVendor)}; type={snapshot.AdapterType}; " +
            $"vramUsed={FormatGiB(snapshot.VideoMemoryUsedBytes, snapshot.VideoMemoryUsedBytes > 0)}; " +
            $"storageFree={FormatGiB(snapshot.FreeStorageBytes, snapshot.FreeStorageKnown)}; " +
            $"minimum={(evaluation.MinimumSatisfied ? 1 : 0)}; recommended={(evaluation.RecommendedSatisfied ? 1 : 0)}; " +
            $"recommendedProfile={evaluation.RecommendedGraphicsProfile}; " +
            "ssd=unknown; vramCapacity=unknown; action=recommend-only.";
    }

    private static long ReadPhysicalMemoryBytes()
    {
        try
        {
            Godot.Collections.Dictionary info = OS.GetMemoryInfo();
            string physical = info["physical"].ToString();
            return long.TryParse(physical, NumberStyles.Integer, CultureInfo.InvariantCulture, out long bytes) && bytes > 0
                ? bytes
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static (long Bytes, bool Known) ReadFreeStorageBytes()
    {
        try
        {
            string userPath = ProjectSettings.GlobalizePath("user://");
            string fullPath = Path.GetFullPath(userPath);
            string? root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root))
            {
                return (0, false);
            }

            DriveInfo drive = new(root);
            return (Math.Max(0L, drive.AvailableFreeSpace), true);
        }
        catch
        {
            return (0, false);
        }
    }

    private static long ReadMonitorBytes(Performance.Monitor monitor)
    {
        double value = Performance.GetMonitor(monitor);
        if (!double.IsFinite(value) || value <= 0.0)
        {
            return 0;
        }
        return (long)Math.Min(long.MaxValue, Math.Round(value));
    }

    private static string FormatGiB(long bytes, bool known) =>
        known ? $"{bytes / (1024.0 * 1024.0 * 1024.0):0.0}GiB" : "unknown";

    private static string Sanitize(string value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Replace(';', ',');
}
