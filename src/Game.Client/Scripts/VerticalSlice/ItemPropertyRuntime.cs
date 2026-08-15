using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public sealed record IndustryItemProperties(
    int Quality,
    int Purity,
    int Stability)
{
    public static IndustryItemProperties Legacy { get; } = new(100, 100, 100);

    public double RecoveryEfficiency =>
        (Quality * 0.5 + Purity * 0.3 + Stability * 0.2) / 100.0;

    public static IndustryItemProperties Create(
        int quality,
        int purity,
        int stability)
    {
        if (quality is < 0 or > 100 ||
            purity is < 0 or > 100 ||
            stability is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quality),
                "Item quality, purity and stability must be in 0..100.");
        }

        return new IndustryItemProperties(quality, purity, stability);
    }

    public static IndustryItemProperties FromSaveData(
        InventoryItemSaveData item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Create(item.Quality, item.Purity, item.Stability);
    }
}

public sealed record DismantleExecutionReport(
    bool Succeeded,
    string Result,
    string RecipeId,
    string SourceDefinitionId,
    int SourceQuantity,
    IndustryItemProperties SourceProperties,
    double RecoveryEfficiency,
    IReadOnlyList<CraftingStackDefinition> Returns);

/// <summary>
/// Deterministic item-property and dismantling rules. The service is independent
/// from Godot and uses recipe data only, so the same process sequence produces
/// the same properties in acceptance tests, saves and future server simulation.
/// </summary>
public static class ItemPropertyRuntime
{
    public static IndustryProcessEnvironment CreateNominalEnvironment(
        CraftingRecipeDefinition recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        RecipeEnvironmentDefinition environment = recipe.Environment;
        return new IndustryProcessEnvironment(
            (environment.MinimumTemperatureKelvin +
             environment.MaximumTemperatureKelvin) / 2.0,
            environment.RequiresVacuum
                ? environment.MinimumPressureKPa
                : (environment.MinimumPressureKPa +
                   environment.MaximumPressureKPa) / 2.0,
            environment.RequiresVacuum);
    }

    public static IndustryItemProperties CreateOutputProperties(
        CraftingRecipeDefinition recipe,
        long processSequence,
        IndustryProcessEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(environment);
        if (processSequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processSequence));
        }

        int minimum = recipe.Quality.Minimum;
        int maximum = recipe.Quality.Maximum;
        ulong hash = StableHash($"{recipe.RecipeId}|{processSequence}|quality");
        int span = checked(maximum - minimum + 1);
        int quality = minimum + (int)(hash % (ulong)span);
        int environmentScore = CalculateEnvironmentScore(
            recipe.Environment,
            environment);
        int hazardPenalty = recipe.Hazards.Count * 3;
        int purity = ClampScore((int)Math.Round(
            quality * 0.60 + environmentScore * 0.35 +
            recipe.TechnologyTier * 2.0 - hazardPenalty));
        int stability = ClampScore((int)Math.Round(
            quality * 0.45 + purity * 0.35 + environmentScore * 0.20 -
            hazardPenalty));
        return IndustryItemProperties.Create(quality, purity, stability);
    }

    public static IndustryItemProperties CreateRecoveredProperties(
        IndustryItemProperties source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return IndustryItemProperties.Create(
            Math.Max(0, source.Quality - 12),
            Math.Max(0, source.Purity - 8),
            Math.Max(0, source.Stability - 15));
    }

    public static DismantleExecutionReport Dismantle(
        CraftingRecipeDefinition recipe,
        IndustryItemProperties sourceProperties,
        int sourceQuantity = 1)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(sourceProperties);
        if (sourceQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceQuantity));
        }

        if (recipe.Outputs.Count != 1)
        {
            throw new InvalidOperationException(
                $"Recipe {recipe.RecipeId} must have exactly one output for dismantling.");
        }

        CraftingStackDefinition source = recipe.Outputs[0];

        if (recipe.DismantleReturns.Count == 0)
        {
            return new DismantleExecutionReport(
                false,
                GameLocalizationService.Format("ui.item.no_dismantle_returns", ("recipe", recipe.RecipeId)),
                recipe.RecipeId,
                source.DefinitionId,
                sourceQuantity,
                sourceProperties,
                sourceProperties.RecoveryEfficiency,
                Array.Empty<CraftingStackDefinition>());
        }

        double efficiency = Math.Clamp(
            sourceProperties.RecoveryEfficiency,
            0.0,
            1.0);
        CraftingStackDefinition[] returns = recipe.DismantleReturns
            .Select(stack => new CraftingStackDefinition(
                stack.DefinitionId,
                checked((int)Math.Floor(
                    stack.Quantity * sourceQuantity * efficiency + 0.000001))))
            .Where(stack => stack.Quantity > 0)
            .ToArray();
        string result = GameLocalizationService.Format(
            "ui.industry.dismantled",
            ("quantity", sourceQuantity), ("item", source.DefinitionId),
            ("efficiency", efficiency.ToString("0.###", CultureInfo.InvariantCulture)),
            ("returns", returns.Sum(stack => stack.Quantity)));
        return new DismantleExecutionReport(
            true,
            result,
            recipe.RecipeId,
            source.DefinitionId,
            sourceQuantity,
            sourceProperties,
            efficiency,
            returns);
    }

    private static int CalculateEnvironmentScore(
        RecipeEnvironmentDefinition required,
        IndustryProcessEnvironment actual)
    {
        double temperatureMidpoint =
            (required.MinimumTemperatureKelvin +
             required.MaximumTemperatureKelvin) / 2.0;
        double temperatureHalfRange = Math.Max(
            1.0,
            (required.MaximumTemperatureKelvin -
             required.MinimumTemperatureKelvin) / 2.0);
        double pressureMidpoint =
            (required.MinimumPressureKPa + required.MaximumPressureKPa) / 2.0;
        double pressureHalfRange = Math.Max(
            1.0,
            (required.MaximumPressureKPa - required.MinimumPressureKPa) / 2.0);
        double temperatureFit = 1.0 - Math.Min(
            1.0,
            Math.Abs(actual.TemperatureKelvin - temperatureMidpoint) /
            temperatureHalfRange);
        double pressureFit = 1.0 - Math.Min(
            1.0,
            Math.Abs(actual.PressureKPa - pressureMidpoint) /
            pressureHalfRange);
        double vacuumFit = !required.RequiresVacuum || actual.IsVacuum
            ? 1.0
            : 0.0;
        return ClampScore((int)Math.Round(
            (temperatureFit * 0.45 + pressureFit * 0.35 + vacuumFit * 0.20) *
            100.0));
    }

    private static int ClampScore(int value) => Math.Clamp(value, 0, 100);

    private static ulong StableHash(string key)
    {
        ulong hash = 14695981039346656037UL;
        unchecked
        {
            foreach (char character in key)
            {
                hash ^= (byte)(character & 0xFF);
                hash *= 1099511628211UL;
                hash ^= (byte)(character >> 8);
                hash *= 1099511628211UL;
            }
        }

        return hash;
    }
}
