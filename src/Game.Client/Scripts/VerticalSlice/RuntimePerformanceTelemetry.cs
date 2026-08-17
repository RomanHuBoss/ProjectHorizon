using System;
using Godot;

public sealed record RuntimePerformanceLiveSnapshot(
    RuntimePerformanceProfile Profile,
    RuntimePerformanceQualityState QualityState,
    RuntimePerformanceSampleCore Sample,
    RuntimePerformanceEvaluation Evaluation,
    long EngineStaticMemoryBytes,
    int SceneNodeCount,
    int ResourceCount,
    int SampleIndex,
    int BudgetOverrunSamples,
    double PeakCpuFrameMilliseconds,
    int PeakDrawCalls,
    long PeakRenderedPrimitives)
{
    public string BudgetStatus => Evaluation.WithinBudget ? "OK" : Evaluation.Overruns.ToString();
}

/// <summary>
/// Samples Godot's built-in performance monitors at the architecture telemetry cadence.
/// Sampling is deliberately not performed every frame; managed allocation accounting is
/// amortized over the frames since the previous sample.
/// </summary>
public sealed class RuntimePerformanceTelemetryRuntime
{
    private readonly SystemFrequencyGate _sampleGate =
        new(SystemFrequencyPolicy.TelemetryFlushHz);
    private readonly RuntimePerformanceAdaptiveGovernor _governor = new();
    private long _allocatedBytesAtLastSample = GC.GetTotalAllocatedBytes(false);
    private int _framesSinceSample;
    private double _elapsedSinceSample;
    private int _sampleCount;
    private int _budgetOverrunSamples;
    private double _peakCpuMilliseconds;
    private int _peakDrawCalls;
    private long _peakRenderedPrimitives;

    public RuntimePerformanceAdaptiveGovernor Governor => _governor;
    public RuntimePerformanceLiveSnapshot? Latest { get; private set; }
    public int SampleCount => _sampleCount;
    public int BudgetOverrunSamples => _budgetOverrunSamples;

    public bool Advance(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0.0)
        {
            return false;
        }
        _framesSinceSample++;
        _elapsedSinceSample += deltaSeconds;
        return _sampleGate.Consume(deltaSeconds);
    }

    public RuntimePerformanceLiveSnapshot Capture(
        RuntimePerformanceProfile profile,
        int fullAiCount,
        int simplifiedAiCount)
    {
        int frames = Math.Max(1, _framesSinceSample);
        double elapsed = Math.Max(0.000001, _elapsedSinceSample);
        double fallbackFps = frames / elapsed;
        double fallbackFrameMs = elapsed * 1000.0 / frames;

        double fpsMonitor = ReadMonitor(Performance.Monitor.TimeFps);
        double cpuSeconds = ReadMonitor(Performance.Monitor.TimeProcess);
        double physicsSeconds = ReadMonitor(Performance.Monitor.TimePhysicsProcess);
        double navigationSeconds = ReadMonitor(Performance.Monitor.TimeNavigationProcess);
        int drawCalls = ToInt(ReadMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
        long primitives = ToLong(ReadMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame));
        long videoMemory = ToLong(ReadMonitor(Performance.Monitor.RenderVideoMemUsed));
        int activePhysics = ToInt(ReadMonitor(Performance.Monitor.Physics3DActiveObjects));
        long staticMemory = ToLong(ReadMonitor(Performance.Monitor.MemoryStatic));
        int nodeCount = ToInt(ReadMonitor(Performance.Monitor.ObjectNodeCount));
        int resourceCount = ToInt(ReadMonitor(Performance.Monitor.ObjectResourceCount));

        long totalAllocated = GC.GetTotalAllocatedBytes(false);
        long allocationDelta = Math.Max(0L, totalAllocated - _allocatedBytesAtLastSample);
        long allocatedPerFrame = allocationDelta / frames;
        _allocatedBytesAtLastSample = totalAllocated;

        double fps = fpsMonitor > 0.0 ? fpsMonitor : fallbackFps;
        double cpuMilliseconds = cpuSeconds > 0.0
            ? cpuSeconds * 1000.0
            : fallbackFrameMs;
        bool renderingAvailable = drawCalls > 0 || primitives > 0;
        bool videoMemoryAvailable = videoMemory > 0;
        RuntimePerformanceSampleCore sample = new(
            fps,
            cpuMilliseconds,
            Math.Max(0.0, physicsSeconds * 1000.0),
            Math.Max(0.0, navigationSeconds * 1000.0),
            drawCalls,
            primitives,
            Math.Max(0, activePhysics),
            Math.Max(0, fullAiCount),
            Math.Max(0, simplifiedAiCount),
            videoMemory,
            Math.Max(0L, System.Environment.WorkingSet),
            allocatedPerFrame,
            renderingAvailable,
            videoMemoryAvailable);
        RuntimePerformanceEvaluation evaluation =
            RuntimePerformanceBudgetPolicy.Evaluate(sample, profile);
        _governor.Observe(evaluation);

        _sampleCount++;
        if (!evaluation.WithinBudget)
        {
            _budgetOverrunSamples++;
        }
        _peakCpuMilliseconds = Math.Max(_peakCpuMilliseconds, cpuMilliseconds);
        _peakDrawCalls = Math.Max(_peakDrawCalls, drawCalls);
        _peakRenderedPrimitives = Math.Max(_peakRenderedPrimitives, primitives);
        RuntimePerformanceLiveSnapshot snapshot = new(
            profile,
            _governor.State,
            sample,
            evaluation,
            staticMemory,
            nodeCount,
            resourceCount,
            _sampleCount,
            _budgetOverrunSamples,
            _peakCpuMilliseconds,
            _peakDrawCalls,
            _peakRenderedPrimitives);
        Latest = snapshot;
        _framesSinceSample = 0;
        _elapsedSinceSample = 0.0;
        return snapshot;
    }

    public void Reset()
    {
        _sampleGate.Reset();
        _governor.Reset();
        _allocatedBytesAtLastSample = GC.GetTotalAllocatedBytes(false);
        _framesSinceSample = 0;
        _elapsedSinceSample = 0.0;
        _sampleCount = 0;
        _budgetOverrunSamples = 0;
        _peakCpuMilliseconds = 0.0;
        _peakDrawCalls = 0;
        _peakRenderedPrimitives = 0;
        Latest = null;
    }

    private static double ReadMonitor(Performance.Monitor monitor)
    {
        double value = Performance.GetMonitor(monitor);
        return double.IsFinite(value) && value >= 0.0 ? value : 0.0;
    }

    private static int ToInt(double value) =>
        value <= 0.0 ? 0 : (int)Math.Min(int.MaxValue, Math.Round(value));

    private static long ToLong(double value) =>
        value <= 0.0 ? 0L : (long)Math.Min(long.MaxValue, Math.Round(value));
}
