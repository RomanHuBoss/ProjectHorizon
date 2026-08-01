using System;

public enum CraftTimerAdvanceResult
{
    NotRunning = 0,
    Running = 1,
    Completed = 2
}

public sealed class DataDrivenCraftTimer
{
    private const double CompletionToleranceSeconds = 0.000001;

    public bool IsRunning { get; private set; }

    public string RecipeId { get; private set; } = string.Empty;

    public string StationId { get; private set; } = string.Empty;

    public double DurationSeconds { get; private set; }

    public double ElapsedSeconds { get; private set; }

    public double RemainingSeconds => Math.Max(
        0.0,
        DurationSeconds - ElapsedSeconds);

    public double Progress01 => DurationSeconds <= 0.0
        ? 0.0
        : Math.Clamp(ElapsedSeconds / DurationSeconds, 0.0, 1.0);

    public bool TryStart(
        CraftingRecipeDefinition recipe,
        string stationId,
        out string result)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (IsRunning)
        {
            result = $"recipe {RecipeId} is already processing at {StationId}";
            return false;
        }

        if (!GameContentCatalog.IsStableId(stationId))
        {
            throw new ArgumentException(
                "Crafting station ID must be a stable dotted string ID.",
                nameof(stationId));
        }

        if (!string.Equals(
            stationId,
            recipe.RequiredStation,
            StringComparison.Ordinal))
        {
            result = $"recipe {recipe.RecipeId} requires station " +
                recipe.RequiredStation;
            return false;
        }

        if (!double.IsFinite(recipe.CraftTimeSeconds) ||
            recipe.CraftTimeSeconds <= 0.0)
        {
            result = $"recipe {recipe.RecipeId} has no positive craft time";
            return false;
        }

        RecipeId = recipe.RecipeId;
        StationId = stationId;
        DurationSeconds = recipe.CraftTimeSeconds;
        ElapsedSeconds = 0.0;
        IsRunning = true;
        result = $"recipe {RecipeId} started for {DurationSeconds:0.###} s";
        return true;
    }

    public CraftTimerAdvanceResult Advance(
        double deltaSeconds,
        out string result)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deltaSeconds),
                "Craft timer delta must be finite and non-negative.");
        }

        if (!IsRunning)
        {
            result = "no timed craft is running";
            return CraftTimerAdvanceResult.NotRunning;
        }

        ElapsedSeconds = Math.Min(
            DurationSeconds,
            ElapsedSeconds + deltaSeconds);
        if (ElapsedSeconds + CompletionToleranceSeconds < DurationSeconds)
        {
            result = $"recipe {RecipeId} processing; " +
                $"remaining={RemainingSeconds:0.###} s";
            return CraftTimerAdvanceResult.Running;
        }

        ElapsedSeconds = DurationSeconds;
        IsRunning = false;
        result = $"recipe {RecipeId} timer completed";
        return CraftTimerAdvanceResult.Completed;
    }

    public void Reset()
    {
        IsRunning = false;
        RecipeId = string.Empty;
        StationId = string.Empty;
        DurationSeconds = 0.0;
        ElapsedSeconds = 0.0;
    }
}
