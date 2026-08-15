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
    bool LiveTransitionPath,
    bool TransactionalSwap,
    bool StateRestored,
    int LiveSteps,
    int MaxHostChildren,
    int TransitionCount,
    int Reloads,
    int RejectedTransitions,
    int HyperspaceTransitions,
    string Result);

public static class WorldSceneCoordinatorAcceptanceRunner
{
    public static WorldSceneCoordinatorAcceptanceReport Run(
        WorldSceneCoordinatorNode liveCoordinator,
        Action refreshResidencyPolicy,
        Func<bool> residencyPolicyMatches)
    {
        ArgumentNullException.ThrowIfNull(liveCoordinator);
        ArgumentNullException.ThrowIfNull(refreshResidencyPolicy);
        ArgumentNullException.ThrowIfNull(residencyPolicyMatches);

        WorldSceneCoordinatorNodeSnapshot original =
            liveCoordinator.CaptureSnapshot();
        WorldSceneContext alphaSurface = WorldSceneContext.Create(
            WorldSceneKind.Surface,
            "system.alpha",
            "planet.alpha.0");
        WorldSceneContext[] livePath =
        {
            WorldSceneContext.Create(
                WorldSceneKind.Orbit,
                "system.alpha",
                "planet.alpha.0"),
            WorldSceneContext.Create(
                WorldSceneKind.StationInterior,
                "system.alpha",
                "planet.alpha.0"),
            WorldSceneContext.Create(
                WorldSceneKind.HyperspaceTransit,
                "system.alpha",
                "planet.alpha.0"),
            WorldSceneContext.Create(
                WorldSceneKind.StationInterior,
                "system.beta",
                "planet.beta.0"),
            WorldSceneContext.Create(
                WorldSceneKind.Orbit,
                "system.beta",
                "planet.beta.0"),
            WorldSceneContext.Create(
                WorldSceneKind.Surface,
                "system.beta",
                "planet.beta.0")
        };

        bool contextValidation = ValidateContextGuard();
        bool packedScenes =
            WorldSceneCoordinatorNode.ValidatePackedScenes(out _);
        bool transitionGraph = true;
        bool illegalRejected = false;
        bool hyperspaceSystemChange = false;
        bool singleLiveScene = true;
        bool liveContextMatch = true;
        bool residencyPolicy = true;
        bool liveTransitionPath = true;
        bool transactionalSwap = true;
        bool stateRestored = false;
        int liveSteps = 0;
        int maxHostChildren = 0;
        int transitionCount = 0;
        int reloads = 0;
        int rejectedTransitions = 0;
        int hyperspaceTransitions = 0;
        string executionFailure = string.Empty;

        try
        {
            liveCoordinator.Restore(alphaSurface);
            refreshResidencyPolicy();
            liveTransitionPath &= VerifyLiveStep(
                liveCoordinator,
                alphaSurface,
                residencyPolicyMatches,
                ref singleLiveScene,
                ref liveContextMatch,
                ref residencyPolicy,
                ref liveSteps,
                ref maxHostChildren);

            foreach (WorldSceneContext context in livePath)
            {
                WorldSceneTransitionResult transitionResult =
                    liveCoordinator.TryTransition(context, out string detail);
                if (transitionResult != WorldSceneTransitionResult.Applied)
                {
                    executionFailure =
                        $"live transition to {context.Kind} failed: {detail}";
                    transitionGraph = false;
                    liveTransitionPath = false;
                    break;
                }

                refreshResidencyPolicy();
                bool stepPassed = VerifyLiveStep(
                    liveCoordinator,
                    context,
                    residencyPolicyMatches,
                    ref singleLiveScene,
                    ref liveContextMatch,
                    ref residencyPolicy,
                    ref liveSteps,
                    ref maxHostChildren);
                liveTransitionPath &= stepPassed;
                transitionGraph &= stepPassed;

                if (context.Kind == WorldSceneKind.StationInterior &&
                    string.Equals(
                        context.SystemId,
                        "system.beta",
                        StringComparison.Ordinal))
                {
                    hyperspaceSystemChange =
                        string.Equals(
                            liveCoordinator.Runtime.Current.SystemId,
                            "system.beta",
                            StringComparison.Ordinal) &&
                        liveCoordinator.Runtime.HyperspaceTransitions -
                            original.Runtime.HyperspaceTransitions == 1;
                }
            }

            transitionGraph &= liveSteps == 7;
            liveTransitionPath &= liveSteps == 7;

            if (transitionGraph &&
                liveCoordinator.Runtime.Current.Kind == WorldSceneKind.Surface)
            {
                WorldSceneCoordinatorDiagnostics beforeIllegal =
                    liveCoordinator.CreateDiagnostics();
                WorldSceneTransitionResult illegal =
                    liveCoordinator.TryTransition(
                        WorldSceneContext.Create(
                            WorldSceneKind.StationInterior,
                            "system.beta",
                            "planet.beta.0"),
                        out _);
                refreshResidencyPolicy();
                WorldSceneCoordinatorDiagnostics afterIllegal =
                    liveCoordinator.CreateDiagnostics();
                illegalRejected =
                    illegal == WorldSceneTransitionResult.Rejected &&
                    afterIllegal.Kind == WorldSceneKind.Surface &&
                    string.Equals(
                        afterIllegal.SystemId,
                        "system.beta",
                        StringComparison.Ordinal) &&
                    string.Equals(
                        beforeIllegal.ScenePath,
                        afterIllegal.ScenePath,
                        StringComparison.Ordinal) &&
                    beforeIllegal.Reloads == afterIllegal.Reloads &&
                    afterIllegal.SingleScene &&
                    afterIllegal.ShellMatchesContext &&
                    residencyPolicyMatches();
            }

            WorldSceneCoordinatorNodeSnapshot exercised =
                liveCoordinator.CaptureSnapshot();
            transitionCount =
                exercised.Runtime.TransitionCount -
                original.Runtime.TransitionCount;
            reloads = exercised.Reloads - original.Reloads;
            rejectedTransitions =
                exercised.Runtime.RejectedTransitions -
                original.Runtime.RejectedTransitions;
            hyperspaceTransitions =
                exercised.Runtime.HyperspaceTransitions -
                original.Runtime.HyperspaceTransitions;
            transactionalSwap =
                exercised.SceneLoadFailures == original.SceneLoadFailures &&
                exercised.Rollbacks == original.Rollbacks;
        }
        catch (Exception exception)
        {
            transitionGraph = false;
            liveTransitionPath = false;
            transactionalSwap = false;
            executionFailure = exception.Message;
        }
        finally
        {
            try
            {
                liveCoordinator.RestoreSnapshot(original);
                refreshResidencyPolicy();
                WorldSceneCoordinatorNodeSnapshot restored =
                    liveCoordinator.CaptureSnapshot();
                WorldSceneCoordinatorDiagnostics diagnostics =
                    liveCoordinator.CreateDiagnostics();
                stateRestored =
                    restored == original &&
                    diagnostics.SingleScene &&
                    diagnostics.ShellMatchesContext &&
                    diagnostics.Kind == original.Runtime.Current.Kind &&
                    string.Equals(
                        diagnostics.SystemId,
                        original.Runtime.Current.SystemId,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        diagnostics.PlanetId,
                        original.Runtime.Current.PlanetId,
                        StringComparison.Ordinal) &&
                    residencyPolicyMatches();
            }
            catch (Exception exception)
            {
                stateRestored = false;
                executionFailure = string.IsNullOrWhiteSpace(executionFailure)
                    ? "acceptance cleanup failed: " + exception.Message
                    : executionFailure +
                      "; acceptance cleanup failed: " + exception.Message;
            }
        }

        bool passed = transitionGraph &&
            illegalRejected &&
            hyperspaceSystemChange &&
            contextValidation &&
            packedScenes &&
            singleLiveScene &&
            liveContextMatch &&
            residencyPolicy &&
            liveTransitionPath &&
            transactionalSwap &&
            stateRestored &&
            liveSteps == 7 &&
            maxHostChildren == 1 &&
            transitionCount == 6 &&
            reloads == 7 &&
            rejectedTransitions == 1 &&
            hyperspaceTransitions == 1;

        string result = passed
            ? "live Surface->Orbit->Station->Hyperspace->Station->Orbit->Surface path kept one shell resident and restored the exact pre-test state"
            : BuildFailure(
                transitionGraph,
                illegalRejected,
                hyperspaceSystemChange,
                contextValidation,
                packedScenes,
                singleLiveScene,
                liveContextMatch,
                residencyPolicy,
                liveTransitionPath,
                transactionalSwap,
                stateRestored,
                liveSteps,
                maxHostChildren,
                transitionCount,
                reloads,
                rejectedTransitions,
                hyperspaceTransitions,
                executionFailure);

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
            liveTransitionPath,
            transactionalSwap,
            stateRestored,
            liveSteps,
            maxHostChildren,
            transitionCount,
            reloads,
            rejectedTransitions,
            hyperspaceTransitions,
            result);
    }

    private static bool VerifyLiveStep(
        WorldSceneCoordinatorNode liveCoordinator,
        WorldSceneContext expected,
        Func<bool> residencyPolicyMatches,
        ref bool singleLiveScene,
        ref bool liveContextMatch,
        ref bool residencyPolicy,
        ref int liveSteps,
        ref int maxHostChildren)
    {
        WorldSceneCoordinatorDiagnostics diagnostics =
            liveCoordinator.CreateDiagnostics();
        bool single = diagnostics.SingleScene &&
            diagnostics.HostChildren == 1;
        bool contextMatches = diagnostics.ShellMatchesContext &&
            diagnostics.Kind == expected.Kind &&
            string.Equals(
                diagnostics.SystemId,
                expected.SystemId,
                StringComparison.Ordinal) &&
            string.Equals(
                diagnostics.PlanetId,
                expected.PlanetId,
                StringComparison.Ordinal) &&
            string.Equals(
                diagnostics.ScenePath,
                WorldSceneCoordinatorNode.GetScenePath(expected.Kind),
                StringComparison.Ordinal);
        bool residency = residencyPolicyMatches();

        singleLiveScene &= single;
        liveContextMatch &= contextMatches;
        residencyPolicy &= residency;
        liveSteps++;
        maxHostChildren = Math.Max(maxHostChildren, diagnostics.HostChildren);
        return single && contextMatches && residency;
    }

    private static bool ValidateContextGuard()
    {
        try
        {
            _ = WorldSceneContext.Create(
                WorldSceneKind.Surface,
                " ",
                "planet.invalid");
        }
        catch (ArgumentException)
        {
            return true;
        }

        return false;
    }

    private static string BuildFailure(
        bool transitionGraph,
        bool illegalRejected,
        bool hyperspaceSystemChange,
        bool contextValidation,
        bool packedScenes,
        bool singleLiveScene,
        bool liveContextMatch,
        bool residencyPolicy,
        bool liveTransitionPath,
        bool transactionalSwap,
        bool stateRestored,
        int liveSteps,
        int maxHostChildren,
        int transitionCount,
        int reloads,
        int rejectedTransitions,
        int hyperspaceTransitions,
        string executionFailure)
    {
        string failure =
            $"transitionGraph={(transitionGraph ? 1 : 0)}; " +
            $"illegalRejected={(illegalRejected ? 1 : 0)}; " +
            $"hyperspaceSystemChange={(hyperspaceSystemChange ? 1 : 0)}; " +
            $"contextValidation={(contextValidation ? 1 : 0)}; " +
            $"packedScenes={(packedScenes ? 1 : 0)}; " +
            $"singleLiveScene={(singleLiveScene ? 1 : 0)}; " +
            $"liveContextMatch={(liveContextMatch ? 1 : 0)}; " +
            $"residencyPolicy={(residencyPolicy ? 1 : 0)}; " +
            $"livePath={(liveTransitionPath ? 1 : 0)}; " +
            $"transactionalSwap={(transactionalSwap ? 1 : 0)}; " +
            $"stateRestored={(stateRestored ? 1 : 0)}; " +
            $"steps={liveSteps}; maxHostChildren={maxHostChildren}; " +
            $"transitions={transitionCount}; reloads={reloads}; " +
            $"rejected={rejectedTransitions}; hyperspace={hyperspaceTransitions}";
        if (!string.IsNullOrWhiteSpace(executionFailure))
        {
            failure += "; error=" + executionFailure;
        }
        return failure;
    }
}
