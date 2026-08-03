using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public sealed record ProductionQueueTerminalJobRow(
    string JobId,
    string RecipeId,
    ProductionQueueJobStatus Status,
    double Progress01,
    string ProgressBar,
    string TimingText,
    string SlotText,
    string ReservationText,
    double ReservedEnergy,
    bool CanPause,
    bool CanResume,
    bool CanCancel);

public sealed record ProductionQueueTerminalSnapshot(
    string StationId,
    double EnergyRemaining,
    double EnergyCapacity,
    int ParallelSlots,
    int RunningJobs,
    int QueuedJobs,
    int PausedJobs,
    IReadOnlyList<ProductionQueueTerminalJobRow> Jobs);

/// <summary>
/// Godot-independent projection used by the station terminal and F1 acceptance.
/// It exposes progress, slot assignment, energy and exact reservations without
/// mutating the queue runtime.
/// </summary>
public static class ProductionQueueTerminalModel
{
    private const int ProgressBarWidth = 16;

    public static ProductionQueueTerminalSnapshot Build(
        ProductionQueueRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ProductionQueueTerminalJobRow[] jobs = runtime.Jobs
            .Select(BuildRow)
            .ToArray();
        return new ProductionQueueTerminalSnapshot(
            runtime.StationId,
            runtime.EnergyRemaining,
            runtime.EnergyCapacity,
            runtime.ParallelSlots,
            runtime.RunningCount,
            runtime.QueuedCount,
            runtime.PausedCount,
            jobs);
    }

    private static ProductionQueueTerminalJobRow BuildRow(
        ProductionQueueJobView job)
    {
        string inputText = FormatStacks(job.ReservedInputs);
        string catalystText = FormatStacks(job.ReservedCatalysts);
        string reservations = job.ReservedCatalysts.Count == 0
            ? $"inputs: {inputText}"
            : $"inputs: {inputText}; catalysts: {catalystText}";
        string slot = job.Status == ProductionQueueJobStatus.Running
            ? $"slot {job.SlotIndex + 1}"
            : job.Status == ProductionQueueJobStatus.Queued
                ? "waiting"
                : "paused";
        return new ProductionQueueTerminalJobRow(
            job.JobId,
            job.RecipeId,
            job.Status,
            job.Progress01,
            FormatProgressBar(job.Progress01),
            $"{job.ElapsedSeconds.ToString("0.0", CultureInfo.InvariantCulture)}/" +
            $"{job.DurationSeconds.ToString("0.0", CultureInfo.InvariantCulture)}s",
            slot,
            reservations,
            job.ReservedEnergy,
            job.Status == ProductionQueueJobStatus.Running,
            job.Status == ProductionQueueJobStatus.Paused,
            true);
    }

    private static string FormatProgressBar(double progress01)
    {
        int filled = (int)Math.Round(
            Math.Clamp(progress01, 0.0, 1.0) * ProgressBarWidth,
            MidpointRounding.AwayFromZero);
        return "[" + new string('#', filled) +
            new string('-', ProgressBarWidth - filled) + "]";
    }

    private static string FormatStacks(
        IReadOnlyList<CraftingStackDefinition> stacks)
    {
        return stacks.Count == 0
            ? "none"
            : string.Join(
                " + ",
                stacks.Select(stack =>
                    $"{stack.Quantity}x{stack.DefinitionId}"));
    }
}
