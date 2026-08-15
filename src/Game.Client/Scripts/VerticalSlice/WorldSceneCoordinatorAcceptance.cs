using System;

public sealed record WorldSceneCoordinatorAcceptanceReport(
    bool Passed,
    bool TransitionGraph,
    bool IllegalTransitionRejected,
    bool HyperspaceSystemChange,
    bool ContextValidation,
    bool PackedScenes,
    bool SingleLiveScene,
    bool LiveContextMatch,
    bool ResidencyPolicy,
    int TransitionCount,
    int Reloads,
    int RejectedTransitions,
    int HyperspaceTransitions,
    string Result);

public static class WorldSceneCoordinatorAcceptanceRunner
{
    public static WorldSceneCoordinatorAcceptanceReport Run(
        WorldSceneCoordinatorNode liveCoordinator,
        bool residencyPolicy)
    {
        ArgumentNullException.ThrowIfNull(liveCoordinator);

        WorldSceneContext alphaSurface = WorldSceneContext.Create(
            WorldSceneKind.Surface,
            "system.alpha",
            "planet.alpha.0");
        WorldSceneCoordinatorRuntime runtime = new(alphaSurface);

        bool transitionGraph =
            Apply(runtime, WorldSceneKind.Orbit, "system.alpha", "planet.alpha.0") &&
            Apply(runtime, WorldSceneKind.StationInterior, "system.alpha", "planet.alpha.0") &&
            Apply(runtime, WorldSceneKind.HyperspaceTransit, "system.alpha", "planet.alpha.0") &&
            Apply(runtime, WorldSceneKind.StationInterior, "system.beta", "planet.beta.0") &&
            Apply(runtime, WorldSceneKind.Orbit, "system.beta", "planet.beta.0") &&
            Apply(runtime, WorldSceneKind.Surface, "system.beta", "planet.beta.0");

        WorldSceneTransitionResult illegal = runtime.TryTransition(
            WorldSceneContext.Create(
                WorldSceneKind.StationInterior,
                "system.beta",
                "planet.beta.0"),
            out _);
        bool illegalRejected =
            illegal == WorldSceneTransitionResult.Rejected &&
            runtime.Current.Kind == WorldSceneKind.Surface;

        WorldSceneCoordinatorRuntime hyperspaceRuntime = new(
            WorldSceneContext.Create(
                WorldSceneKind.StationInterior,
                "system.source",
                "planet.source.0"));
        bool hyperspaceSystemChange =
            Apply(hyperspaceRuntime, WorldSceneKind.HyperspaceTransit,
                "system.source", "planet.source.0") &&
            Apply(hyperspaceRuntime, WorldSceneKind.StationInterior,
                "system.destination", "planet.destination.0") &&
            string.Equals(
                hyperspaceRuntime.Current.SystemId,
                "system.destination",
                StringComparison.Ordinal) &&
            hyperspaceRuntime.HyperspaceTransitions == 1;

        bool contextValidation = false;
        try
        {
            _ = WorldSceneContext.Create(
                WorldSceneKind.Surface,
                " ",
                "planet.invalid");
        }
        catch (ArgumentException)
        {
            contextValidation = true;
        }

        bool packedScenes =
            WorldSceneCoordinatorNode.ValidatePackedScenes(out _);
        WorldSceneCoordinatorDiagnostics diagnostics =
            liveCoordinator.CreateDiagnostics();
        bool singleLiveScene = diagnostics.SingleScene;
        bool liveContextMatch = diagnostics.ShellMatchesContext &&
            diagnostics.HostChildren == 1 &&
            !string.IsNullOrWhiteSpace(diagnostics.ScenePath);

        bool passed = transitionGraph &&
            illegalRejected &&
            hyperspaceSystemChange &&
            contextValidation &&
            packedScenes &&
            singleLiveScene &&
            liveContextMatch &&
            residencyPolicy;

        string result = passed
            ? "world scene coordinator keeps one packed context resident and gates Surface/Orbit/Station/Hyperspace transitions"
            : BuildFailure(
                transitionGraph,
                illegalRejected,
                hyperspaceSystemChange,
                contextValidation,
                packedScenes,
                singleLiveScene,
                liveContextMatch,
                residencyPolicy);

        return new WorldSceneCoordinatorAcceptanceReport(
            passed,
            transitionGraph,
            illegalRejected,
            hyperspaceSystemChange,
            contextValidation,
            packedScenes,
            singleLiveScene,
            liveContextMatch,
            residencyPolicy,
            diagnostics.Transitions,
            diagnostics.Reloads,
            diagnostics.RejectedTransitions,
            diagnostics.HyperspaceTransitions,
            result);
    }

    private static bool Apply(
        WorldSceneCoordinatorRuntime runtime,
        WorldSceneKind kind,
        string systemId,
        string planetId)
    {
        return runtime.TryTransition(
            WorldSceneContext.Create(kind, systemId, planetId),
            out _) == WorldSceneTransitionResult.Applied;
    }

    private static string BuildFailure(
        bool transitionGraph,
        bool illegalRejected,
        bool hyperspaceSystemChange,
        bool contextValidation,
        bool packedScenes,
        bool singleLiveScene,
        bool liveContextMatch,
        bool residencyPolicy)
    {
        return $"transitionGraph={(transitionGraph ? 1 : 0)}; " +
            $"illegalRejected={(illegalRejected ? 1 : 0)}; " +
            $"hyperspaceSystemChange={(hyperspaceSystemChange ? 1 : 0)}; " +
            $"contextValidation={(contextValidation ? 1 : 0)}; " +
            $"packedScenes={(packedScenes ? 1 : 0)}; " +
            $"singleLiveScene={(singleLiveScene ? 1 : 0)}; " +
            $"liveContextMatch={(liveContextMatch ? 1 : 0)}; " +
            $"residencyPolicy={(residencyPolicy ? 1 : 0)}";
    }
}
