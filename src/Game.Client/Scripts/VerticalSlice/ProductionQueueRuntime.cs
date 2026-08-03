using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public enum ProductionQueueCommandResult
{
    Enqueued = 0,
    Paused = 1,
    Resumed = 2,
    Cancelled = 3,
    NotFound = 4,
    InvalidState = 5,
    ValidationFailed = 6
}

public sealed record ProductionQueueJobView(
    string JobId,
    string RecipeId,
    int RequestedBatches,
    double DurationSeconds,
    double ElapsedSeconds,
    ProductionQueueJobStatus Status,
    int SlotIndex,
    long JobSequence,
    long ProcessSequence,
    double ReservedEnergy,
    IReadOnlyList<CraftingStackDefinition> ReservedInputs,
    IReadOnlyList<CraftingStackDefinition> ReservedCatalysts,
    double Progress01);

public sealed record ProductionQueueCommandReport(
    ProductionQueueCommandResult Result,
    string ResultText,
    string JobId,
    IndustryProcessResult ValidationResult,
    IReadOnlyList<CraftingStackDefinition> RefundedInputs,
    IReadOnlyList<CraftingStackDefinition> RefundedCatalysts,
    double RefundedEnergy);

public sealed record ProductionQueueAdvanceReport(
    double AdvancedSeconds,
    IReadOnlyList<IndustryProcessExecutionReport> CompletedProcesses,
    int RunningJobs,
    int QueuedJobs,
    int PausedJobs,
    int MaximumObservedRunning);

/// <summary>
/// Godot-independent production queue. Inputs, catalysts and energy are
/// reserved atomically when a job is enqueued. A graceful-exit snapshot freezes
/// progress; restoring the snapshot resumes from exactly the persisted elapsed
/// time without offline progress. Cancellation returns all reservations because
/// no output exists before completion.
/// </summary>
public sealed class ProductionQueueRuntime
{
    private const double Epsilon = 0.000001;

    private sealed class JobState
    {
        public required string JobId { get; init; }
        public required CraftingRecipeDefinition Recipe { get; init; }
        public required IndustryProcessEnvironment Environment { get; init; }
        public required int RequestedBatches { get; init; }
        public required double DurationSeconds { get; init; }
        public required long JobSequence { get; init; }
        public required long ProcessSequence { get; init; }
        public required double ReservedEnergy { get; init; }
        public required IReadOnlyList<CraftingStackDefinition> ReservedInputs
        {
            get;
            init;
        }
        public required IReadOnlyList<CraftingStackDefinition>
            ReservedCatalysts { get; init; }
        public double ElapsedSeconds { get; set; }
        public ProductionQueueJobStatus Status { get; set; }
        public int SlotIndex { get; set; } = -1;
    }

    private readonly CraftingStationDefinition _station;
    private readonly IReadOnlyDictionary<string, CraftingRecipeDefinition>
        _recipes;
    private readonly Func<string, bool> _isTechnologyUnlocked;
    private readonly Dictionary<string, int> _inventory =
        new(StringComparer.Ordinal);
    private readonly List<JobState> _jobs = new();
    private readonly List<IndustryProcessExecutionReport> _completed = new();
    private long _nextJobSequence;
    private long _nextProcessSequence;
    private int _maximumObservedRunning;

    public ProductionQueueRuntime(
        CraftingStationDefinition station,
        IReadOnlyDictionary<string, CraftingRecipeDefinition> recipes,
        double initialEnergy,
        Func<string, bool>? isTechnologyUnlocked = null,
        long initialJobSequence = 0,
        long initialProcessSequence = 0)
    {
        ArgumentNullException.ThrowIfNull(station);
        ArgumentNullException.ThrowIfNull(recipes);
        if (station.ParallelSlots <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(station),
                "Production station must expose at least one parallel slot.");
        }

        if (!double.IsFinite(initialEnergy) ||
            initialEnergy < 0.0 ||
            initialEnergy > station.EnergyCapacity + Epsilon)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialEnergy),
                "Initial queue energy must be finite, non-negative and not " +
                "exceed the station energy capacity.");
        }

        if (initialJobSequence < 0 || initialProcessSequence < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialJobSequence),
                "Queue sequences must be non-negative.");
        }

        _station = station;
        _recipes = recipes;
        _isTechnologyUnlocked = isTechnologyUnlocked ?? (static _ => true);
        EnergyRemaining = initialEnergy;
        _nextJobSequence = initialJobSequence;
        _nextProcessSequence = initialProcessSequence;
    }

    public string StationId => _station.StationId;

    public int ParallelSlots => _station.ParallelSlots;

    public double EnergyCapacity => _station.EnergyCapacity;

    public double EnergyRemaining { get; private set; }

    public long NextJobSequence => _nextJobSequence;

    public long NextProcessSequence => _nextProcessSequence;

    public int MaximumObservedRunning => _maximumObservedRunning;

    public IReadOnlyList<CraftingStackDefinition> Inventory =>
        _inventory
            .Where(pair => pair.Value > 0)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new CraftingStackDefinition(pair.Key, pair.Value))
            .ToArray();

    public IReadOnlyList<ProductionQueueJobView> Jobs => _jobs
        .OrderBy(job => job.JobSequence)
        .Select(ToView)
        .ToArray();

    public IReadOnlyList<IndustryProcessExecutionReport> CompletedProcesses =>
        _completed.ToArray();

    public int RunningCount => _jobs.Count(job =>
        job.Status == ProductionQueueJobStatus.Running);

    public int QueuedCount => _jobs.Count(job =>
        job.Status == ProductionQueueJobStatus.Queued);

    public int PausedCount => _jobs.Count(job =>
        job.Status == ProductionQueueJobStatus.Paused);

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

    public double RechargeEnergy(double amount)
    {
        if (!double.IsFinite(amount) || amount < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "Recharge amount must be finite and non-negative.");
        }

        double before = EnergyRemaining;
        EnergyRemaining = Math.Min(EnergyCapacity, EnergyRemaining + amount);
        return EnergyRemaining - before;
    }

    public bool TryConsumeInventory(
        string definitionId,
        int quantity,
        out string result)
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
                "Inventory consumption must be positive.");
        }

        int available = GetQuantity(definitionId);
        if (available < quantity)
        {
            result = $"missing {quantity - available} x {definitionId}";
            return false;
        }

        Consume(definitionId, quantity);
        result = $"consumed {quantity} x {definitionId}";
        return true;
    }

    public ProductionQueueCommandReport Enqueue(
        string recipeId,
        IndustryProcessEnvironment environment,
        int requestedBatches)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipeId);
        ArgumentNullException.ThrowIfNull(environment);
        if (!_recipes.TryGetValue(
            recipeId,
            out CraftingRecipeDefinition? recipe) ||
            recipe is null)
        {
            return CommandFailure(
                ProductionQueueCommandResult.ValidationFailed,
                $"recipe {recipeId} is unavailable",
                IndustryProcessResult.RecipeUnavailable);
        }

        IndustryProcessRuntime validator = new(
            EnergyRemaining,
            _isTechnologyUnlocked,
            _nextProcessSequence);
        foreach (CraftingStackDefinition stack in Inventory)
        {
            validator.AddInventory(stack.DefinitionId, stack.Quantity);
        }

        IndustryProcessResult validation = validator.Validate(
            recipe,
            _station,
            environment,
            requestedBatches,
            out string validationText);
        if (validation != IndustryProcessResult.Ready)
        {
            return CommandFailure(
                ProductionQueueCommandResult.ValidationFailed,
                validationText,
                validation);
        }

        IReadOnlyList<CraftingStackDefinition> reservedInputs = recipe.Inputs
            .Select(input => new CraftingStackDefinition(
                input.DefinitionId,
                checked(input.Quantity * requestedBatches)))
            .ToArray();
        IReadOnlyList<CraftingStackDefinition> reservedCatalysts =
            recipe.Catalysts
                .Select(catalyst => new CraftingStackDefinition(
                    catalyst.DefinitionId,
                    catalyst.Quantity))
                .ToArray();
        double reservedEnergy = recipe.EnergyCost * requestedBatches;

        foreach (CraftingStackDefinition input in reservedInputs)
        {
            Consume(input.DefinitionId, input.Quantity);
        }

        foreach (CraftingStackDefinition catalyst in reservedCatalysts)
        {
            Consume(catalyst.DefinitionId, catalyst.Quantity);
        }

        EnergyRemaining -= reservedEnergy;
        long jobSequence = _nextJobSequence++;
        long processSequence = _nextProcessSequence++;
        string jobId = $"job.production.{jobSequence:000000}";
        JobState job = new()
        {
            JobId = jobId,
            Recipe = recipe,
            Environment = environment,
            RequestedBatches = requestedBatches,
            DurationSeconds = recipe.CraftTimeSeconds * requestedBatches,
            JobSequence = jobSequence,
            ProcessSequence = processSequence,
            ReservedEnergy = reservedEnergy,
            ReservedInputs = reservedInputs,
            ReservedCatalysts = reservedCatalysts,
            ElapsedSeconds = 0.0,
            Status = ProductionQueueJobStatus.Queued,
            SlotIndex = -1
        };
        _jobs.Add(job);
        ScheduleQueuedJobs();
        return new ProductionQueueCommandReport(
            ProductionQueueCommandResult.Enqueued,
            $"job {jobId} enqueued for recipe {recipe.RecipeId}",
            jobId,
            IndustryProcessResult.Ready,
            Array.Empty<CraftingStackDefinition>(),
            Array.Empty<CraftingStackDefinition>(),
            0.0);
    }

    public ProductionQueueCommandReport Pause(string jobId)
    {
        JobState? job = FindJob(jobId);
        if (job is null)
        {
            return CommandFailure(
                ProductionQueueCommandResult.NotFound,
                $"job {jobId} was not found");
        }

        if (job.Status != ProductionQueueJobStatus.Running)
        {
            return CommandFailure(
                ProductionQueueCommandResult.InvalidState,
                $"job {jobId} is not running",
                jobId: jobId);
        }

        job.Status = ProductionQueueJobStatus.Paused;
        job.SlotIndex = -1;
        ScheduleQueuedJobs();
        return new ProductionQueueCommandReport(
            ProductionQueueCommandResult.Paused,
            $"job {jobId} paused at {job.ElapsedSeconds:0.###}s",
            jobId,
            IndustryProcessResult.Ready,
            Array.Empty<CraftingStackDefinition>(),
            Array.Empty<CraftingStackDefinition>(),
            0.0);
    }

    public ProductionQueueCommandReport Resume(string jobId)
    {
        JobState? job = FindJob(jobId);
        if (job is null)
        {
            return CommandFailure(
                ProductionQueueCommandResult.NotFound,
                $"job {jobId} was not found");
        }

        if (job.Status != ProductionQueueJobStatus.Paused)
        {
            return CommandFailure(
                ProductionQueueCommandResult.InvalidState,
                $"job {jobId} is not paused",
                jobId: jobId);
        }

        job.Status = ProductionQueueJobStatus.Queued;
        job.SlotIndex = -1;
        ScheduleQueuedJobs();
        return new ProductionQueueCommandReport(
            ProductionQueueCommandResult.Resumed,
            $"job {jobId} resumed",
            jobId,
            IndustryProcessResult.Ready,
            Array.Empty<CraftingStackDefinition>(),
            Array.Empty<CraftingStackDefinition>(),
            0.0);
    }

    public ProductionQueueCommandReport Cancel(string jobId)
    {
        JobState? job = FindJob(jobId);
        if (job is null)
        {
            return CommandFailure(
                ProductionQueueCommandResult.NotFound,
                $"job {jobId} was not found");
        }

        foreach (CraftingStackDefinition input in job.ReservedInputs)
        {
            AddInventory(input.DefinitionId, input.Quantity);
        }

        foreach (CraftingStackDefinition catalyst in job.ReservedCatalysts)
        {
            AddInventory(catalyst.DefinitionId, catalyst.Quantity);
        }

        EnergyRemaining += job.ReservedEnergy;
        _jobs.Remove(job);
        ScheduleQueuedJobs();
        return new ProductionQueueCommandReport(
            ProductionQueueCommandResult.Cancelled,
            $"job {jobId} cancelled with full reservation refund",
            jobId,
            IndustryProcessResult.Ready,
            job.ReservedInputs.ToArray(),
            job.ReservedCatalysts.ToArray(),
            job.ReservedEnergy);
    }

    public ProductionQueueAdvanceReport Advance(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsedSeconds),
                "Queue advance must be finite and non-negative.");
        }

        double remaining = elapsedSeconds;
        List<IndustryProcessExecutionReport> completed = new();
        ScheduleQueuedJobs();
        while (remaining > Epsilon)
        {
            JobState[] running = _jobs
                .Where(job => job.Status == ProductionQueueJobStatus.Running)
                .OrderBy(job => job.SlotIndex)
                .ToArray();
            if (running.Length == 0)
            {
                break;
            }

            double untilNextCompletion = running.Min(job =>
                Math.Max(0.0, job.DurationSeconds - job.ElapsedSeconds));
            double step = Math.Min(remaining, untilNextCompletion);
            if (step > Epsilon)
            {
                foreach (JobState job in running)
                {
                    job.ElapsedSeconds = Math.Min(
                        job.DurationSeconds,
                        job.ElapsedSeconds + step);
                }

                remaining -= step;
            }

            JobState[] finished = running
                .Where(job =>
                    job.DurationSeconds - job.ElapsedSeconds <= Epsilon)
                .ToArray();
            if (finished.Length == 0)
            {
                break;
            }

            foreach (JobState job in finished)
            {
                completed.Add(Complete(job));
            }

            ScheduleQueuedJobs();
        }

        return new ProductionQueueAdvanceReport(
            elapsedSeconds - remaining,
            completed,
            RunningCount,
            QueuedCount,
            PausedCount,
            _maximumObservedRunning);
    }

    public ProductionQueueSaveData CreateSaveData()
    {
        return new ProductionQueueSaveData(
            _station.StationId,
            EnergyRemaining,
            _nextJobSequence,
            _nextProcessSequence,
            _jobs
                .OrderBy(job => job.JobSequence)
                .Select(job => new ProductionQueueJobSaveData(
                    job.JobId,
                    job.Recipe.RecipeId,
                    job.RequestedBatches,
                    job.DurationSeconds,
                    job.ElapsedSeconds,
                    job.Status,
                    job.SlotIndex,
                    job.JobSequence,
                    job.ProcessSequence,
                    job.ReservedEnergy,
                    job.Environment.TemperatureKelvin,
                    job.Environment.PressureKPa,
                    job.Environment.IsVacuum,
                    job.ReservedInputs
                        .Select(stack => new ProductionQueueStackSaveData(
                            stack.DefinitionId,
                            stack.Quantity))
                        .ToArray(),
                    job.ReservedCatalysts
                        .Select(stack => new ProductionQueueStackSaveData(
                            stack.DefinitionId,
                            stack.Quantity))
                        .ToArray()))
                .ToArray());
    }

    public static ProductionQueueRuntime Restore(
        CraftingStationDefinition station,
        IReadOnlyDictionary<string, CraftingRecipeDefinition> recipes,
        ProductionQueueSaveData saveData,
        IReadOnlyList<CraftingStackDefinition> freeInventory,
        Func<string, bool>? isTechnologyUnlocked = null)
    {
        ArgumentNullException.ThrowIfNull(station);
        ArgumentNullException.ThrowIfNull(recipes);
        ArgumentNullException.ThrowIfNull(saveData);
        ArgumentNullException.ThrowIfNull(freeInventory);
        if (!string.Equals(
            station.StationId,
            saveData.StationId,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Queue station mismatch: expected {station.StationId}, " +
                $"saved {saveData.StationId}.");
        }

        if (!double.IsFinite(saveData.EnergyRemaining) ||
            saveData.EnergyRemaining < 0.0 ||
            saveData.EnergyRemaining > station.EnergyCapacity + Epsilon ||
            saveData.NextJobSequence < 0 ||
            saveData.NextProcessSequence < 0)
        {
            throw new InvalidOperationException(
                "Saved production queue has invalid energy or sequence state.");
        }

        ProductionQueueRuntime runtime = new(
            station,
            recipes,
            saveData.EnergyRemaining,
            isTechnologyUnlocked,
            saveData.NextJobSequence,
            saveData.NextProcessSequence);
        foreach (CraftingStackDefinition stack in freeInventory)
        {
            runtime.AddInventory(stack.DefinitionId, stack.Quantity);
        }

        HashSet<int> occupiedSlots = new();
        HashSet<string> jobIds = new(StringComparer.Ordinal);
        HashSet<long> jobSequences = new();
        HashSet<long> processSequences = new();
        foreach (ProductionQueueJobSaveData savedJob in saveData.Jobs
            .OrderBy(job => job.JobSequence))
        {
            if (!jobIds.Add(savedJob.JobId) ||
                !jobSequences.Add(savedJob.JobSequence) ||
                !processSequences.Add(savedJob.ProcessSequence))
            {
                throw new InvalidOperationException(
                    "Saved production queue contains duplicate job identity " +
                    "or sequence values.");
            }

            if (!recipes.TryGetValue(
                savedJob.RecipeId,
                out CraftingRecipeDefinition? recipe) ||
                recipe is null)
            {
                throw new InvalidOperationException(
                    $"Saved queue recipe {savedJob.RecipeId} is unavailable.");
            }

            ValidateSavedJob(station, recipe, savedJob, occupiedSlots);
            runtime._jobs.Add(new JobState
            {
                JobId = savedJob.JobId,
                Recipe = recipe,
                Environment = new IndustryProcessEnvironment(
                    savedJob.TemperatureKelvin,
                    savedJob.PressureKPa,
                    savedJob.IsVacuum),
                RequestedBatches = savedJob.RequestedBatches,
                DurationSeconds = savedJob.DurationSeconds,
                JobSequence = savedJob.JobSequence,
                ProcessSequence = savedJob.ProcessSequence,
                ReservedEnergy = savedJob.ReservedEnergy,
                ReservedInputs = savedJob.ReservedInputs
                    .Select(stack => new CraftingStackDefinition(
                        stack.DefinitionId,
                        stack.Quantity))
                    .ToArray(),
                ReservedCatalysts = savedJob.ReservedCatalysts
                    .Select(stack => new CraftingStackDefinition(
                        stack.DefinitionId,
                        stack.Quantity))
                    .ToArray(),
                ElapsedSeconds = savedJob.ElapsedSeconds,
                Status = savedJob.Status,
                SlotIndex = savedJob.SlotIndex
            });
        }

        if (jobSequences.Count > 0 &&
            saveData.NextJobSequence <= jobSequences.Max())
        {
            throw new InvalidOperationException(
                "Saved next job sequence does not follow existing jobs.");
        }

        if (processSequences.Count > 0 &&
            saveData.NextProcessSequence <= processSequences.Max())
        {
            throw new InvalidOperationException(
                "Saved next process sequence does not follow existing jobs.");
        }

        runtime._maximumObservedRunning = runtime.RunningCount;
        if (runtime.RunningCount < station.ParallelSlots &&
            runtime.QueuedCount > 0)
        {
            throw new InvalidOperationException(
                "Saved queue contains queued jobs while a station slot is free.");
        }

        return runtime;
    }

    private IndustryProcessExecutionReport Complete(JobState job)
    {
        List<CraftingStackDefinition> consumedCatalysts = new();
        List<CraftingStackDefinition> retainedCatalysts = new();
        foreach (CatalystStackDefinition catalyst in job.Recipe.Catalysts)
        {
            CraftingStackDefinition stack = new(
                catalyst.DefinitionId,
                catalyst.Quantity);
            if (IndustryProcessRuntime.ShouldConsumeCatalyst(
                job.Recipe.RecipeId,
                catalyst.DefinitionId,
                catalyst.ConsumptionChance,
                job.ProcessSequence))
            {
                consumedCatalysts.Add(stack);
            }
            else
            {
                AddInventory(stack.DefinitionId, stack.Quantity);
                retainedCatalysts.Add(stack);
            }
        }

        List<CraftingStackDefinition> outputs = new();
        foreach (CraftingStackDefinition output in job.Recipe.Outputs)
        {
            int quantity = checked(
                output.Quantity *
                job.Recipe.BatchSize *
                job.RequestedBatches);
            AddInventory(output.DefinitionId, quantity);
            outputs.Add(new CraftingStackDefinition(
                output.DefinitionId,
                quantity));
        }

        List<CraftingStackDefinition> byproducts = new();
        foreach (CraftingStackDefinition byproduct in job.Recipe.Byproducts)
        {
            int quantity = checked(
                byproduct.Quantity * job.RequestedBatches);
            AddInventory(byproduct.DefinitionId, quantity);
            byproducts.Add(new CraftingStackDefinition(
                byproduct.DefinitionId,
                quantity));
        }

        IndustryProcessExecutionReport report = new(
            IndustryProcessResult.Completed,
            $"queued recipe {job.Recipe.RecipeId} completed in slot " +
            $"{job.SlotIndex}: batches={job.RequestedBatches}; " +
            $"energy={job.ReservedEnergy.ToString("0.###", CultureInfo.InvariantCulture)}",
            job.Recipe.RecipeId,
            job.RequestedBatches,
            job.Recipe.BatchSize,
            job.ReservedEnergy,
            EnergyRemaining,
            outputs,
            byproducts,
            consumedCatalysts,
            retainedCatalysts,
            job.Recipe.Hazards.ToArray(),
            job.ProcessSequence);
        _jobs.Remove(job);
        _completed.Add(report);
        return report;
    }

    private void ScheduleQueuedJobs()
    {
        HashSet<int> occupied = _jobs
            .Where(job => job.Status == ProductionQueueJobStatus.Running)
            .Select(job => job.SlotIndex)
            .ToHashSet();
        foreach (JobState job in _jobs
            .Where(job => job.Status == ProductionQueueJobStatus.Queued)
            .OrderBy(job => job.JobSequence))
        {
            int slot = -1;
            for (int index = 0; index < _station.ParallelSlots; index++)
            {
                if (!occupied.Contains(index))
                {
                    slot = index;
                    break;
                }
            }

            if (slot < 0)
            {
                break;
            }

            job.Status = ProductionQueueJobStatus.Running;
            job.SlotIndex = slot;
            occupied.Add(slot);
        }

        _maximumObservedRunning = Math.Max(
            _maximumObservedRunning,
            RunningCount);
    }

    private static void ValidateSavedJob(
        CraftingStationDefinition station,
        CraftingRecipeDefinition recipe,
        ProductionQueueJobSaveData savedJob,
        ISet<int> occupiedSlots)
    {
        if (!Enum.IsDefined(
            typeof(ProductionQueueJobStatus),
            savedJob.Status))
        {
            throw new InvalidOperationException(
                $"Saved queue job {savedJob.JobId} has invalid status.");
        }

        if (!GameContentCatalog.IsStableId(savedJob.JobId) ||
            savedJob.RequestedBatches <= 0 ||
            savedJob.JobSequence < 0 ||
            savedJob.ProcessSequence < 0 ||
            !double.IsFinite(savedJob.DurationSeconds) ||
            savedJob.DurationSeconds <= 0.0 ||
            !double.IsFinite(savedJob.ElapsedSeconds) ||
            savedJob.ElapsedSeconds < 0.0 ||
            savedJob.ElapsedSeconds > savedJob.DurationSeconds + Epsilon ||
            !double.IsFinite(savedJob.ReservedEnergy) ||
            savedJob.ReservedEnergy < 0.0)
        {
            throw new InvalidOperationException(
                $"Saved queue job {savedJob.JobId} has invalid numeric state.");
        }

        if (!double.IsFinite(savedJob.TemperatureKelvin) ||
            !double.IsFinite(savedJob.PressureKPa) ||
            savedJob.TemperatureKelvin <
                recipe.Environment.MinimumTemperatureKelvin - Epsilon ||
            savedJob.TemperatureKelvin >
                recipe.Environment.MaximumTemperatureKelvin + Epsilon ||
            savedJob.PressureKPa <
                recipe.Environment.MinimumPressureKPa - Epsilon ||
            savedJob.PressureKPa >
                recipe.Environment.MaximumPressureKPa + Epsilon ||
            (recipe.Environment.RequiresVacuum && !savedJob.IsVacuum))
        {
            throw new InvalidOperationException(
                $"Saved process environment for {savedJob.JobId} is invalid.");
        }

        if (!string.Equals(
                recipe.RequiredStation,
                station.StationId,
                StringComparison.Ordinal) ||
            station.Tier < recipe.StationTier ||
            !station.SupportedCategories.Contains(
                recipe.Category,
                StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Saved recipe {recipe.RecipeId} is incompatible with " +
                $"station {station.StationId}.");
        }

        double expectedDuration =
            recipe.CraftTimeSeconds * savedJob.RequestedBatches;
        double expectedEnergy = recipe.EnergyCost * savedJob.RequestedBatches;
        if (Math.Abs(savedJob.DurationSeconds - expectedDuration) > Epsilon ||
            Math.Abs(savedJob.ReservedEnergy - expectedEnergy) > Epsilon)
        {
            throw new InvalidOperationException(
                $"Saved queue job {savedJob.JobId} differs from recipe data.");
        }

        IReadOnlyList<ProductionQueueStackSaveData> expectedInputs =
            recipe.Inputs
                .Select(input => new ProductionQueueStackSaveData(
                    input.DefinitionId,
                    checked(input.Quantity * savedJob.RequestedBatches)))
                .OrderBy(stack => stack.DefinitionId, StringComparer.Ordinal)
                .ToArray();
        IReadOnlyList<ProductionQueueStackSaveData> expectedCatalysts =
            recipe.Catalysts
                .Select(catalyst => new ProductionQueueStackSaveData(
                    catalyst.DefinitionId,
                    catalyst.Quantity))
                .OrderBy(stack => stack.DefinitionId, StringComparer.Ordinal)
                .ToArray();
        if (!StacksEqual(expectedInputs, savedJob.ReservedInputs) ||
            !StacksEqual(expectedCatalysts, savedJob.ReservedCatalysts))
        {
            throw new InvalidOperationException(
                $"Saved reservations for {savedJob.JobId} differ from recipe data.");
        }

        if (savedJob.Status == ProductionQueueJobStatus.Running)
        {
            if (savedJob.SlotIndex < 0 ||
                savedJob.SlotIndex >= station.ParallelSlots ||
                !occupiedSlots.Add(savedJob.SlotIndex))
            {
                throw new InvalidOperationException(
                    $"Saved running slot for {savedJob.JobId} is invalid.");
            }
        }
        else if (savedJob.SlotIndex != -1)
        {
            throw new InvalidOperationException(
                $"Non-running job {savedJob.JobId} must not own a slot.");
        }
    }

    private static bool StacksEqual(
        IReadOnlyList<ProductionQueueStackSaveData> expected,
        IReadOnlyList<ProductionQueueStackSaveData> actual)
    {
        ProductionQueueStackSaveData[] orderedActual = actual
            .OrderBy(stack => stack.DefinitionId, StringComparer.Ordinal)
            .ToArray();
        if (expected.Count != orderedActual.Length)
        {
            return false;
        }

        for (int index = 0; index < expected.Count; index++)
        {
            if (!string.Equals(
                    expected[index].DefinitionId,
                    orderedActual[index].DefinitionId,
                    StringComparison.Ordinal) ||
                expected[index].Quantity != orderedActual[index].Quantity)
            {
                return false;
            }
        }

        return true;
    }

    private JobState? FindJob(string jobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        return _jobs.FirstOrDefault(job => string.Equals(
            job.JobId,
            jobId,
            StringComparison.Ordinal));
    }

    private void Consume(string definitionId, int quantity)
    {
        if (!_inventory.TryGetValue(definitionId, out int available) ||
            available < quantity)
        {
            throw new InvalidOperationException(
                $"Production queue inventory underflow for {definitionId}: " +
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

    private static ProductionQueueJobView ToView(JobState job)
    {
        return new ProductionQueueJobView(
            job.JobId,
            job.Recipe.RecipeId,
            job.RequestedBatches,
            job.DurationSeconds,
            job.ElapsedSeconds,
            job.Status,
            job.SlotIndex,
            job.JobSequence,
            job.ProcessSequence,
            job.ReservedEnergy,
            job.ReservedInputs
                .Select(stack => new CraftingStackDefinition(
                    stack.DefinitionId,
                    stack.Quantity))
                .ToArray(),
            job.ReservedCatalysts
                .Select(stack => new CraftingStackDefinition(
                    stack.DefinitionId,
                    stack.Quantity))
                .ToArray(),
            job.DurationSeconds <= Epsilon
                ? 1.0
                : Math.Clamp(
                    job.ElapsedSeconds / job.DurationSeconds,
                    0.0,
                    1.0));
    }

    private static ProductionQueueCommandReport CommandFailure(
        ProductionQueueCommandResult result,
        string text,
        IndustryProcessResult validation = IndustryProcessResult.Ready,
        string jobId = "")
    {
        return new ProductionQueueCommandReport(
            result,
            text,
            jobId,
            validation,
            Array.Empty<CraftingStackDefinition>(),
            Array.Empty<CraftingStackDefinition>(),
            0.0);
    }
}
