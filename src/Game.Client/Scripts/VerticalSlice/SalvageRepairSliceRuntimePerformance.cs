using System;
using Godot;

public partial class SalvageRepairSlice
{
    private readonly RuntimePerformanceTelemetryRuntime _runtimePerformanceTelemetry = new();
    private RuntimePerformanceProfile _runtimePerformanceProfile = RuntimePerformanceProfile.Medium;
    private RuntimePerformanceQualitySettings _runtimePerformanceQualitySettings =
        RuntimePerformanceBudgetPolicy.ResolveQualitySettings(
            RuntimePerformanceProfile.Medium,
            RuntimePerformanceQualityState.Nominal);
    private RuntimePerformanceQualityState _lastRuntimePerformanceQualityState =
        RuntimePerformanceQualityState.Nominal;
    private string _runtimePerformanceAcceptanceHud = "READY";
    private bool? _runtimePerformanceAcceptancePassed;
    private bool _runtimePerformanceReadyPrinted;

    private double PerformanceVegetationDistanceScale =>
        _runtimePerformanceQualitySettings.VegetationDistanceScale * GraphicsVegetationDistanceScale;

    private void InitializeRuntimePerformanceBudgeting()
    {
        _runtimePerformanceProfile = ResolveGraphicsPerformanceBudgetProfile();
        _runtimePerformanceTelemetry.Reset();
        _lastRuntimePerformanceQualityState = RuntimePerformanceQualityState.Nominal;
        _runtimePerformanceQualitySettings = RuntimePerformanceBudgetPolicy.ResolveQualitySettings(
            _runtimePerformanceProfile,
            _lastRuntimePerformanceQualityState);
        ApplyRuntimePerformancePresentationSettings();
        PrintRuntimePerformanceReady();
    }

    private void PrintRuntimePerformanceReady()
    {
        if (_runtimePerformanceReadyPrinted)
        {
            return;
        }
        _runtimePerformanceReadyPrinted = true;
        RuntimePerformanceBudget budget =
            RuntimePerformanceBudgetPolicy.Get(_runtimePerformanceProfile);
        GD.Print(
            "TASK-200 runtime performance READY: " +
            $"profile={_runtimePerformanceProfile}; targetFps={budget.TargetFramesPerSecond:0}; " +
            $"cpuBudget={budget.CpuFrameMilliseconds:0.0}ms; gpuBudget={budget.GpuFrameMilliseconds:0.0}ms-policy; " +
            $"scene=draw:{budget.MaximumDrawCalls}/primitives:{budget.MaximumRenderedPrimitives}/" +
            $"physics:{budget.MaximumActivePhysicsBodies}/ai:{budget.MaximumFullAi}+{budget.MaximumSimplifiedAi}; " +
            $"memory=vram:{budget.MaximumVideoMemoryBytes / RuntimePerformanceBudgetPolicy.Mebibyte}MiB/" +
            $"ram:{budget.MaximumProcessMemoryBytes / RuntimePerformanceBudgetPolicy.Mebibyte}MiB; " +
            $"managedAlloc<={budget.MaximumManagedAllocationBytesPerFrame / RuntimePerformanceBudgetPolicy.Kibibyte}KiB/frame; " +
            "telemetry=Godot-Performance+GC+working-set@2Hz; " +
            "adaptive=presentation-only-vegetation+clouds; F5=acceptance.");
    }

    private void UpdateRuntimePerformanceBudgeting(double delta)
    {
        if (!_runtimePerformanceTelemetry.Advance(delta))
        {
            return;
        }

        RuntimePerformanceQualityState before = _runtimePerformanceTelemetry.Governor.State;
        RuntimePerformanceLiveSnapshot snapshot = _runtimePerformanceTelemetry.Capture(
            _runtimePerformanceProfile,
            CountHighFrequencyAi(),
            CountSimplifiedAi());
        RuntimePerformanceQualityState after = snapshot.QualityState;
        _runtimePerformanceQualitySettings = RuntimePerformanceBudgetPolicy.ResolveQualitySettings(
            _runtimePerformanceProfile,
            after);
        ApplyRuntimePerformancePresentationSettings();

        if (before != after)
        {
            GD.Print(
                "TASK-200 adaptive quality transition: " +
                $"profile={snapshot.Profile}; from={before}; to={after}; " +
                $"pressure={snapshot.Evaluation.PressureRatio:0.00}; " +
                $"overruns={snapshot.Evaluation.Overruns}; " +
                $"vegetationScale={_runtimePerformanceQualitySettings.VegetationDistanceScale:0.00}; " +
                $"cloudLayers={_runtimePerformanceQualitySettings.MaximumCloudLayers}.");
        }
        _lastRuntimePerformanceQualityState = after;

        if (snapshot.SampleIndex == 1 || snapshot.SampleIndex % 20 == 0)
        {
            GD.Print(BuildRuntimePerformanceSampleLine(snapshot));
        }
    }

    private void ApplyRuntimePerformancePresentationSettings()
    {
        _planetAtmosphereCloudNode?.SetPerformanceQuality(
            _runtimePerformanceQualitySettings.MaximumCloudLayers,
            _runtimePerformanceQualitySettings.SecondaryCloudOpacityScale);
        ApplyGraphicsQualityPresentationSettings();
    }

    private int CountHighFrequencyAi()
    {
        if (!_surfaceRuntimeActive)
        {
            return 0;
        }
        int count = 0;
        foreach (EcologyFaunaNode fauna in _ecologyFaunaNodes)
        {
            if (GodotObject.IsInstanceValid(fauna) &&
                fauna.Visible &&
                fauna.Health > 0.0 &&
                fauna.SimulationTier == FaunaSimulationTier.Near)
            {
                count++;
            }
        }
        if (_npcPopulationRoot is not null && GodotObject.IsInstanceValid(_npcPopulationRoot))
        {
            count += _npcPopulationRoot.GetChildCount();
        }
        count += _npcShipNavigationNodes.Count;
        return count;
    }

    private int CountSimplifiedAi()
    {
        if (!_surfaceRuntimeActive)
        {
            return 0;
        }
        int count = 0;
        foreach (EcologyFaunaNode fauna in _ecologyFaunaNodes)
        {
            if (!GodotObject.IsInstanceValid(fauna) || !fauna.Visible || fauna.Health <= 0.0)
            {
                continue;
            }
            if (fauna.SimulationTier == FaunaSimulationTier.MidHigh ||
                fauna.SimulationTier == FaunaSimulationTier.MidLow)
            {
                count++;
            }
        }
        return count;
    }

    private static string BuildRuntimePerformanceSampleLine(
        RuntimePerformanceLiveSnapshot snapshot)
    {
        RuntimePerformanceSampleCore sample = snapshot.Sample;
        return
            "TASK-200 performance sample: " +
            $"profile={snapshot.Profile}; quality={snapshot.QualityState}; " +
            $"fps={sample.FramesPerSecond:0.0}; cpu={sample.CpuFrameMilliseconds:0.00}ms; " +
            $"physics={sample.PhysicsMilliseconds:0.00}ms; nav={sample.NavigationMilliseconds:0.00}ms; " +
            $"draw={sample.DrawCalls}; primitives={sample.RenderedPrimitives}; " +
            $"physicsBodies={sample.ActivePhysicsBodies}; ai={sample.FullAi}+{sample.SimplifiedAi}; " +
            $"alloc={sample.ManagedAllocationBytesPerFrame / RuntimePerformanceBudgetPolicy.Kibibyte}KiB/frame; " +
            $"vram={sample.VideoMemoryBytes / RuntimePerformanceBudgetPolicy.Mebibyte}MiB; " +
            $"ram={sample.ProcessMemoryBytes / RuntimePerformanceBudgetPolicy.Mebibyte}MiB; " +
            $"budget={snapshot.BudgetStatus}; samples={snapshot.SampleIndex}.";
    }

    private void RunRuntimePerformanceAcceptance()
    {
        RuntimePerformanceLiveSnapshot? live = _runtimePerformanceTelemetry.Latest;
        if (live is null)
        {
            live = _runtimePerformanceTelemetry.Capture(
                _runtimePerformanceProfile,
                CountHighFrequencyAi(),
                CountSimplifiedAi());
        }
        RuntimePerformanceAcceptanceReport report =
            RuntimePerformanceAcceptanceRunner.Evaluate(
                live,
                vegetationAdaptiveHook: PerformanceVegetationDistanceScale <= GraphicsVegetationDistanceScale + 0.0001,
                cloudAdaptiveHook: _planetAtmosphereCloudNode is null ||
                    _planetAtmosphereCloudNode.PerformanceCloudLayerLimit <= 2);
        _runtimePerformanceAcceptancePassed = report.Passed;
        _runtimePerformanceAcceptanceHud = report.Passed
            ? $"PASS {report.Profile} samples={report.Samples} budget={report.RuntimeBudgetStatus}"
            : "FAIL performance telemetry contract";
        if (report.Passed)
        {
            GD.Print(report.BuildOutputLine());
            if (live is not null)
            {
                GD.Print(BuildRuntimePerformanceSampleLine(live));
            }
        }
        else
        {
            GD.PushError(report.BuildOutputLine());
        }
    }
}
