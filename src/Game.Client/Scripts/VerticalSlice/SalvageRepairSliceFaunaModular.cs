using System;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private string _faunaModularAcceptanceHud = "READY";
    private bool? _faunaModularAcceptancePassed;
    private bool _faunaModularReadyPrinted;
    private int _faunaFlockUpdatePasses;

    private void PrintFaunaModularReady()
    {
        if (_faunaModularReadyPrinted)
        {
            return;
        }
        _faunaModularReadyPrinted = true;
        GD.Print(
            "TASK-198 modular fauna READY: " +
            "bodyPlans=6-fixed-skeletons; modules=head+torso+limbs+tail+horns+shell; " +
            "compatibility=skeleton-family-only; morphology=deterministic-per-instance; " +
            "ai=hierarchical-state-machine+utility-scoring; " +
            "movement=steering+ground-navmesh+boids+aerial-grid; " +
            "tiers=near:10Hz/mid:5..2Hz/far:statistical-0.5Hz; " +
            "visualInterpolation=per-frame; persistence=TASK-116-seed+deltas; F5=acceptance.");
    }

    private FaunaModularDiagnostics CaptureFaunaModularDiagnostics()
    {
        EcologyFaunaNode[] live = _ecologyFaunaNodes
            .Where(GodotObject.IsInstanceValid)
            .ToArray();
        FaunaStatisticalSnapshot statistical = Ecology.FarFaunaSnapshot;
        return new FaunaModularDiagnostics(
            live.Length,
            live.Count(node => node.Morphology is not null &&
                FaunaBodyPlanRuntime.IsCompatible(node.Morphology)),
            live.Count(node => string.Equals(node.MovementMode, "Ground", StringComparison.Ordinal)),
            live.Count(node => node.GroundNavigationBound),
            live.Sum(node => node.VisualInterpolationFrames),
            _faunaFlockUpdatePasses,
            statistical.Population,
            statistical.Species,
            _surfaceRuntimeActive && _npcNavigationSurface?.IsConfigured == true);
    }

    private void RunFaunaModularAcceptance()
    {
        if (_ecologyCatalog is null || _ecologyPlan is null || _ecologyRuntime is null)
        {
            _faunaModularAcceptancePassed = false;
            _faunaModularAcceptanceHud = "FAIL ecology unavailable";
            GD.PushError("TASK-198 modular fauna acceptance FAIL: ecology runtime unavailable.");
            return;
        }
        FaunaModularAcceptanceReport report = FaunaModularAcceptanceRunner.Evaluate(
            EcologyCatalog,
            EcologyPlan,
            CaptureFaunaModularDiagnostics());
        _faunaModularAcceptancePassed = report.Passed;
        _faunaModularAcceptanceHud = report.Passed
            ? $"PASS body=6 states=11 active={report.ActiveNodes} far={report.StatisticalPopulation}"
            : "FAIL modular fauna contract";
        if (report.Passed)
        {
            GD.Print(report.BuildOutputLine());
        }
        else
        {
            GD.PushError(report.BuildOutputLine());
        }
    }
}
