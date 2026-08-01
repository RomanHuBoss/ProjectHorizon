using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

public sealed record CraftTimeAcceptanceReport(
    bool Passed,
    string Result,
    double DurationSeconds,
    bool PositiveJsonDuration,
    bool Started,
    bool DuplicateStartRejected,
    bool InputsHeldUntilCompletion,
    bool PartialAdvanceStayedRunning,
    bool CompletedAtConfiguredDuration,
    bool SingleCompletion,
    int ProducedOutputQuantity,
    double ElapsedMilliseconds);

public static class CraftTimeAcceptanceRunner
{
    public static CraftTimeAcceptanceReport Run(
        CraftingRecipeDefinition repairRecipe,
        CraftingRecipeDefinition craftingRecipe,
        IReadOnlyList<ResourceNodeBinding> resourceBindings)
    {
        ArgumentNullException.ThrowIfNull(repairRecipe);
        ArgumentNullException.ThrowIfNull(craftingRecipe);
        ArgumentNullException.ThrowIfNull(resourceBindings);
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            bool positiveDuration =
                double.IsFinite(craftingRecipe.CraftTimeSeconds) &&
                craftingRecipe.CraftTimeSeconds >= 0.1;
            StarterRepairSession session = new(
                repairRecipe,
                craftingRecipe);

            CollectRecipeInputs(
                session,
                repairRecipe,
                resourceBindings);
            StarterRepairResult repairResult = session.TryRepair(out _);
            CollectRecipeInputs(
                session,
                craftingRecipe,
                resourceBindings);

            StationCraftResult validation = session.ValidateSecondaryCraft(
                craftingRecipe.RequiredStation,
                out _);
            int inputsBefore = craftingRecipe.Inputs.Sum(input =>
                session.GetAvailableQuantity(input.DefinitionId));
            DataDrivenCraftTimer timer = new();
            bool started = timer.TryStart(
                craftingRecipe,
                craftingRecipe.RequiredStation,
                out _);
            bool duplicateRejected = !timer.TryStart(
                craftingRecipe,
                craftingRecipe.RequiredStation,
                out _);

            double partialDelta = craftingRecipe.CraftTimeSeconds * 0.4;
            CraftTimerAdvanceResult partialResult = timer.Advance(
                partialDelta,
                out _);
            double partialProgress = timer.Progress01;
            int inputsAfterPartial = craftingRecipe.Inputs.Sum(input =>
                session.GetAvailableQuantity(input.DefinitionId));
            int outputsAfterPartial = craftingRecipe.Outputs.Sum(output =>
                session.GetCraftedQuantity(output.DefinitionId));
            bool heldUntilCompletion =
                inputsAfterPartial == inputsBefore &&
                outputsAfterPartial == 0;

            double finalDelta = Math.Max(
                0.0,
                craftingRecipe.CraftTimeSeconds - timer.ElapsedSeconds);
            CraftTimerAdvanceResult completionResult = timer.Advance(
                finalDelta,
                out _);
            double completedElapsed = timer.ElapsedSeconds;
            StationCraftResult craftedResult = completionResult ==
                CraftTimerAdvanceResult.Completed
                ? session.TryCraftSecondary(
                    craftingRecipe.RequiredStation,
                    out _)
                : StationCraftResult.RecipeUnavailable;
            int outputQuantity = craftingRecipe.Outputs.Sum(output =>
                session.GetCraftedQuantity(output.DefinitionId));
            CraftTimerAdvanceResult extraAdvance = timer.Advance(
                1.0,
                out _);
            int outputAfterExtraAdvance = craftingRecipe.Outputs.Sum(output =>
                session.GetCraftedQuantity(output.DefinitionId));

            bool completedAtDuration =
                completionResult == CraftTimerAdvanceResult.Completed &&
                Math.Abs(
                    completedElapsed - craftingRecipe.CraftTimeSeconds) <=
                    0.000001;
            bool singleCompletion =
                extraAdvance == CraftTimerAdvanceResult.NotRunning &&
                outputAfterExtraAdvance == outputQuantity;
            bool partialStayedRunning =
                partialResult == CraftTimerAdvanceResult.Running &&
                partialProgress > 0.0 &&
                partialProgress < 1.0;
            bool passed =
                positiveDuration &&
                repairResult == StarterRepairResult.Repaired &&
                validation == StationCraftResult.Ready &&
                started &&
                duplicateRejected &&
                heldUntilCompletion &&
                partialStayedRunning &&
                completedAtDuration &&
                craftedResult == StationCraftResult.Crafted &&
                outputQuantity == craftingRecipe.Outputs.Sum(
                    output => output.Quantity) &&
                singleCompletion;

            List<string> failures = new();
            if (!positiveDuration)
                failures.Add("positiveDuration=0");
            if (repairResult != StarterRepairResult.Repaired)
                failures.Add("repairSetup=0");
            if (validation != StationCraftResult.Ready)
                failures.Add("ready=0");
            if (!started)
                failures.Add("started=0");
            if (!duplicateRejected)
                failures.Add("duplicateRejected=0");
            if (!heldUntilCompletion)
                failures.Add("inputsHeld=0");
            if (!partialStayedRunning)
                failures.Add("partialRunning=0");
            if (!completedAtDuration)
                failures.Add("durationMatch=0");
            if (craftedResult != StationCraftResult.Crafted)
                failures.Add("crafted=0");
            if (!singleCompletion)
                failures.Add("singleCompletion=0");

            stopwatch.Stop();
            return new CraftTimeAcceptanceReport(
                passed,
                passed
                    ? "configured craft time delayed output and completed exactly once"
                    : $"craft-time criteria failed: {string.Join(", ", failures)}",
                craftingRecipe.CraftTimeSeconds,
                positiveDuration,
                started,
                duplicateRejected,
                heldUntilCompletion,
                partialStayedRunning,
                completedAtDuration,
                singleCompletion,
                outputQuantity,
                stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new CraftTimeAcceptanceReport(
                false,
                $"{exception.GetType().Name}: {exception.Message}",
                craftingRecipe.CraftTimeSeconds,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                0,
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private static void CollectRecipeInputs(
        StarterRepairSession session,
        CraftingRecipeDefinition recipe,
        IReadOnlyList<ResourceNodeBinding> resourceBindings)
    {
        HashSet<string> inputIds = recipe.Inputs
            .Select(input => input.DefinitionId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (ResourceNodeBinding binding in resourceBindings
            .Where(binding => inputIds.Contains(binding.ItemDefinitionId))
            .OrderBy(binding => binding.ResourceNodeId, StringComparer.Ordinal))
        {
            if (!session.TryCollect(
                binding.ResourceNodeId,
                binding.ItemDefinitionId,
                binding.Quantity,
                out string result))
            {
                throw new InvalidOperationException(result);
            }
        }
    }
}
