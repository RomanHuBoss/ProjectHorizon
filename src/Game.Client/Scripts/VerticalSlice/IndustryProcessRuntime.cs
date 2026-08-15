using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public enum IndustryProcessResult
{
    Ready = 0,
    Completed = 1,
    RecipeUnavailable = 2,
    WrongStation = 3,
    StationTierTooLow = 4,
    UnsupportedCategory = 5,
    TechnologyLocked = 6,
    InvalidBatchCount = 7,
    InsufficientEnergy = 8,
    EnvironmentRejected = 9,
    InsufficientInputs = 10,
    MissingCatalysts = 11
}

public sealed record IndustryProcessEnvironment(
    double TemperatureKelvin,
    double PressureKPa,
    bool IsVacuum);

public sealed record IndustryProcessExecutionReport(
    IndustryProcessResult Result,
    string ResultText,
    string RecipeId,
    int RequestedBatches,
    int NativeBatchSize,
    double EnergyConsumed,
    double EnergyRemaining,
    IReadOnlyList<CraftingStackDefinition> Outputs,
    IReadOnlyList<CraftingStackDefinition> Byproducts,
    IReadOnlyList<CraftingStackDefinition> ConsumedCatalysts,
    IReadOnlyList<CraftingStackDefinition> RetainedCatalysts,
    IReadOnlyList<string> Hazards,
    long ProcessSequence);

/// <summary>
/// Godot-independent runtime for one atomic industrial process execution.
/// Queueing, cancellation and parallel slots remain outside this atomic
/// executor and are handled by <see cref="ProductionQueueRuntime"/>.
/// </summary>
public sealed class IndustryProcessRuntime
{
    private readonly Dictionary<string, int> _inventory =
        new(StringComparer.Ordinal);
    private readonly Func<string, bool> _isTechnologyUnlocked;
    private long _nextProcessSequence;

    public IndustryProcessRuntime(
        double initialEnergy,
        Func<string, bool>? isTechnologyUnlocked = null,
        long initialProcessSequence = 0)
    {
        if (!double.IsFinite(initialEnergy) || initialEnergy < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialEnergy),
                "Initial process energy must be finite and non-negative.");
        }

        if (initialProcessSequence < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialProcessSequence),
                "Initial process sequence must be non-negative.");
        }

        EnergyRemaining = initialEnergy;
        _isTechnologyUnlocked = isTechnologyUnlocked ?? (static _ => true);
        _nextProcessSequence = initialProcessSequence;
    }

    public double EnergyRemaining { get; private set; }

    public long NextProcessSequence => _nextProcessSequence;

    public IReadOnlyList<CraftingStackDefinition> Inventory =>
        _inventory
            .Where(pair => pair.Value > 0)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new CraftingStackDefinition(pair.Key, pair.Value))
            .ToArray();

    public int GetQuantity(string definitionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        return _inventory.TryGetValue(definitionId, out int quantity)
            ? quantity
            : 0;
    }

    public void AddInventory(string definitionId, int quantity)
    {
        if (!GameContentCatalog.IsStableId(definitionId))
        {
            throw new ArgumentException(
                "Inventory definition ID must be a stable dotted ID.",
                nameof(definitionId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Inventory grant must be positive.");
        }

        _inventory.TryGetValue(definitionId, out int current);
        checked
        {
            _inventory[definitionId] = current + quantity;
        }
    }

    public IndustryProcessResult Validate(
        CraftingRecipeDefinition recipe,
        CraftingStationDefinition station,
        IndustryProcessEnvironment environment,
        int requestedBatches,
        out string result)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(station);
        ArgumentNullException.ThrowIfNull(environment);

        if (!string.Equals(
            recipe.Application.Type,
            "StoreOutputs",
            StringComparison.Ordinal))
        {
            result = GameLocalizationService.Format("ui.industry.not_inventory", ("recipe", recipe.RecipeId));
            return IndustryProcessResult.RecipeUnavailable;
        }

        if (!string.Equals(
            recipe.RequiredStation,
            station.StationId,
            StringComparison.Ordinal))
        {
            result = GameLocalizationService.Format("ui.industry.station_required", ("recipe", recipe.RecipeId), ("station", recipe.RequiredStation));
            return IndustryProcessResult.WrongStation;
        }

        if (station.Tier < recipe.StationTier)
        {
            result = GameLocalizationService.Format("ui.industry.tier_low", ("tier", station.Tier), ("required", recipe.StationTier));
            return IndustryProcessResult.StationTierTooLow;
        }

        if (!station.SupportedCategories.Contains(
            recipe.Category,
            StringComparer.Ordinal))
        {
            result = GameLocalizationService.Format("ui.industry.category", ("station", station.StationId), ("category", recipe.Category));
            return IndustryProcessResult.UnsupportedCategory;
        }

        if (!_isTechnologyUnlocked(recipe.RequiredTechnology))
        {
            result = GameLocalizationService.Format("ui.industry.technology", ("recipe", recipe.RecipeId), ("technology", recipe.RequiredTechnology));
            return IndustryProcessResult.TechnologyLocked;
        }

        if (requestedBatches <= 0)
        {
            result = GameLocalizationService.Text("ui.industry.batch_positive");
            return IndustryProcessResult.InvalidBatchCount;
        }

        double requiredEnergy = recipe.EnergyCost * requestedBatches;
        if (!double.IsFinite(requiredEnergy) ||
            requiredEnergy < 0.0 ||
            requiredEnergy > station.EnergyCapacity + 0.000001 ||
            requiredEnergy > EnergyRemaining + 0.000001)
        {
            result = GameLocalizationService.Format(
                "ui.industry.energy",
                ("required", requiredEnergy.ToString("0.###", CultureInfo.InvariantCulture)),
                ("available", EnergyRemaining.ToString("0.###", CultureInfo.InvariantCulture)));
            return IndustryProcessResult.InsufficientEnergy;
        }

        RecipeEnvironmentDefinition requiredEnvironment = recipe.Environment;
        bool temperatureValid =
            environment.TemperatureKelvin >=
                requiredEnvironment.MinimumTemperatureKelvin - 0.000001 &&
            environment.TemperatureKelvin <=
                requiredEnvironment.MaximumTemperatureKelvin + 0.000001;
        bool pressureValid =
            environment.PressureKPa >=
                requiredEnvironment.MinimumPressureKPa - 0.000001 &&
            environment.PressureKPa <=
                requiredEnvironment.MaximumPressureKPa + 0.000001;
        bool vacuumValid =
            !requiredEnvironment.RequiresVacuum || environment.IsVacuum;
        if (!temperatureValid || !pressureValid || !vacuumValid)
        {
            result = GameLocalizationService.Format(
                "ui.industry.environment",
                ("temperature", environment.TemperatureKelvin.ToString("0.###", CultureInfo.InvariantCulture) + "K"),
                ("pressure", environment.PressureKPa.ToString("0.###", CultureInfo.InvariantCulture) + "kPa"),
                ("vacuum", environment.IsVacuum ? 1 : 0));
            return IndustryProcessResult.EnvironmentRejected;
        }

        IReadOnlyList<CraftingStackDefinition> missingInputs = recipe.Inputs
            .Select(input => new CraftingStackDefinition(
                input.DefinitionId,
                checked(input.Quantity * requestedBatches)))
            .Where(input => GetQuantity(input.DefinitionId) < input.Quantity)
            .Select(input => input with
            {
                Quantity = input.Quantity - GetQuantity(input.DefinitionId)
            })
            .ToArray();
        if (missingInputs.Count > 0)
        {
            result = GameLocalizationService.Format(
                "ui.industry.missing_inputs",
                ("items", string.Join(", ", missingInputs.Select(input => $"{input.Quantity} × {input.DefinitionId}"))));
            return IndustryProcessResult.InsufficientInputs;
        }

        IReadOnlyList<CraftingStackDefinition> missingCatalysts =
            recipe.Catalysts
                .Where(catalyst =>
                    GetQuantity(catalyst.DefinitionId) < catalyst.Quantity)
                .Select(catalyst => new CraftingStackDefinition(
                    catalyst.DefinitionId,
                    catalyst.Quantity - GetQuantity(catalyst.DefinitionId)))
                .ToArray();
        if (missingCatalysts.Count > 0)
        {
            result = GameLocalizationService.Format(
                "ui.industry.missing_catalysts",
                ("items", string.Join(", ", missingCatalysts.Select(catalyst => $"{catalyst.Quantity} × {catalyst.DefinitionId}"))));
            return IndustryProcessResult.MissingCatalysts;
        }

        result = GameLocalizationService.Format(
            "ui.industry.ready", ("recipe", recipe.RecipeId), ("batches", requestedBatches));
        return IndustryProcessResult.Ready;
    }

    public IndustryProcessExecutionReport Execute(
        CraftingRecipeDefinition recipe,
        CraftingStationDefinition station,
        IndustryProcessEnvironment environment,
        int requestedBatches)
    {
        IndustryProcessResult validation = Validate(
            recipe,
            station,
            environment,
            requestedBatches,
            out string validationText);
        if (validation != IndustryProcessResult.Ready)
        {
            return EmptyReport(
                validation,
                validationText,
                recipe,
                requestedBatches,
                _nextProcessSequence);
        }

        long sequence = _nextProcessSequence++;
        foreach (CraftingStackDefinition input in recipe.Inputs)
        {
            Consume(
                input.DefinitionId,
                checked(input.Quantity * requestedBatches));
        }

        List<CraftingStackDefinition> consumedCatalysts = new();
        List<CraftingStackDefinition> retainedCatalysts = new();
        foreach (CatalystStackDefinition catalyst in recipe.Catalysts)
        {
            CraftingStackDefinition stack = new(
                catalyst.DefinitionId,
                catalyst.Quantity);
            if (ShouldConsumeCatalyst(
                recipe.RecipeId,
                catalyst.DefinitionId,
                catalyst.ConsumptionChance,
                sequence))
            {
                Consume(catalyst.DefinitionId, catalyst.Quantity);
                consumedCatalysts.Add(stack);
            }
            else
            {
                retainedCatalysts.Add(stack);
            }
        }

        List<CraftingStackDefinition> outputs = new();
        foreach (CraftingStackDefinition output in recipe.Outputs)
        {
            int quantity = checked(
                output.Quantity * recipe.BatchSize * requestedBatches);
            AddInventory(output.DefinitionId, quantity);
            outputs.Add(new CraftingStackDefinition(
                output.DefinitionId,
                quantity));
        }

        List<CraftingStackDefinition> byproducts = new();
        foreach (CraftingStackDefinition byproduct in recipe.Byproducts)
        {
            int quantity = checked(byproduct.Quantity * requestedBatches);
            AddInventory(byproduct.DefinitionId, quantity);
            byproducts.Add(new CraftingStackDefinition(
                byproduct.DefinitionId,
                quantity));
        }

        double energyConsumed = recipe.EnergyCost * requestedBatches;
        EnergyRemaining -= energyConsumed;
        string resultText = $"recipe {recipe.RecipeId} completed: " +
            $"batches={requestedBatches}; " +
            $"energy={energyConsumed.ToString("0.###", CultureInfo.InvariantCulture)}; " +
            $"outputs={outputs.Sum(output => output.Quantity)}; " +
            $"byproducts={byproducts.Sum(byproduct => byproduct.Quantity)}; " +
            $"catalystsConsumed={consumedCatalysts.Sum(catalyst => catalyst.Quantity)}";
        return new IndustryProcessExecutionReport(
            IndustryProcessResult.Completed,
            resultText,
            recipe.RecipeId,
            requestedBatches,
            recipe.BatchSize,
            energyConsumed,
            EnergyRemaining,
            outputs,
            byproducts,
            consumedCatalysts,
            retainedCatalysts,
            recipe.Hazards.ToArray(),
            sequence);
    }

    public static bool ShouldConsumeCatalyst(
        string recipeId,
        string catalystDefinitionId,
        double consumptionChance,
        long processSequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalystDefinitionId);
        if (!double.IsFinite(consumptionChance) ||
            consumptionChance < 0.0 ||
            consumptionChance > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(consumptionChance),
                "Catalyst consumption chance must be in [0, 1].");
        }

        if (processSequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processSequence));
        }

        if (consumptionChance <= 0.0)
        {
            return false;
        }

        if (consumptionChance >= 1.0)
        {
            return true;
        }

        string key = $"{recipeId}|{catalystDefinitionId}|{processSequence}";
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

        double roll = (hash >> 11) * (1.0 / 9007199254740992.0);
        return roll < consumptionChance;
    }

    private IndustryProcessExecutionReport EmptyReport(
        IndustryProcessResult result,
        string resultText,
        CraftingRecipeDefinition recipe,
        int requestedBatches,
        long sequence)
    {
        return new IndustryProcessExecutionReport(
            result,
            resultText,
            recipe.RecipeId,
            requestedBatches,
            recipe.BatchSize,
            0.0,
            EnergyRemaining,
            Array.Empty<CraftingStackDefinition>(),
            Array.Empty<CraftingStackDefinition>(),
            Array.Empty<CraftingStackDefinition>(),
            Array.Empty<CraftingStackDefinition>(),
            recipe.Hazards.ToArray(),
            sequence);
    }

    private void Consume(string definitionId, int quantity)
    {
        if (!_inventory.TryGetValue(definitionId, out int available) ||
            available < quantity)
        {
            throw new InvalidOperationException(
                $"Industry process inventory underflow for {definitionId}: " +
                $"required={quantity}, available={available}.");
        }

        int remaining = available - quantity;
        if (remaining == 0)
        {
            _inventory.Remove(definitionId);
        }
        else
        {
            _inventory[definitionId] = remaining;
        }
    }
}
