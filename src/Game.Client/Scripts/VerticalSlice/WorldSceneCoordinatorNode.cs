using System;
using System.Collections.Generic;
using Godot;

public sealed record WorldSceneCoordinatorDiagnostics(
    WorldSceneKind Kind,
    string SystemId,
    string PlanetId,
    string ScenePath,
    string EnvironmentProfile,
    int HostChildren,
    int Generation,
    int Transitions,
    int Reloads,
    int RejectedTransitions,
    int HyperspaceTransitions,
    int SceneLoadFailures,
    int Rollbacks,
    bool SingleScene,
    bool ShellMatchesContext);

public sealed record WorldSceneCoordinatorNodeSnapshot(
    WorldSceneCoordinatorRuntimeSnapshot Runtime,
    int Reloads,
    int SceneLoadFailures,
    int Rollbacks);

public partial class WorldSceneCoordinatorNode : Node3D
{
    public const string SurfaceScenePath =
        "res://Scenes/World/SurfaceWorldShell.tscn";
    public const string OrbitScenePath =
        "res://Scenes/World/OrbitWorldShell.tscn";
    public const string StationScenePath =
        "res://Scenes/World/StationInteriorShell.tscn";
    public const string HyperspaceScenePath =
        "res://Scenes/World/HyperspaceTransitShell.tscn";

    private static readonly IReadOnlyDictionary<WorldSceneKind, string>
        ScenePaths = new Dictionary<WorldSceneKind, string>
        {
            [WorldSceneKind.Surface] = SurfaceScenePath,
            [WorldSceneKind.Orbit] = OrbitScenePath,
            [WorldSceneKind.StationInterior] = StationScenePath,
            [WorldSceneKind.HyperspaceTransit] = HyperspaceScenePath
        };

    private WorldSceneCoordinatorRuntime? _runtime;
    private WorldSceneShell? _activeShell;
    private string _activeScenePath = string.Empty;
    private int _reloadCount;
    private int _sceneLoadFailures;
    private int _rollbackCount;

    public WorldSceneCoordinatorRuntime Runtime =>
        _runtime ?? throw new InvalidOperationException(
            "World scene coordinator runtime is unavailable.");

    public void Configure(WorldSceneCoordinatorRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
        ReloadCurrentShell(force: true);
    }

    public WorldSceneCoordinatorNodeSnapshot CaptureSnapshot()
    {
        return new WorldSceneCoordinatorNodeSnapshot(
            Runtime.CaptureSnapshot(),
            _reloadCount,
            _sceneLoadFailures,
            _rollbackCount);
    }

    /// <summary>
    /// Applies a world-context transition as a scene transaction. The target
    /// PackedScene is loaded, instantiated and attached before application state
    /// is mutated; the previous shell remains live until the new shell is proven
    /// resident. Any failure discards the staged shell and leaves the old context
    /// and shell intact.
    /// </summary>
    public WorldSceneTransitionResult TryTransition(
        WorldSceneContext context,
        out string result)
    {
        ArgumentNullException.ThrowIfNull(context);
        WorldSceneCoordinatorRuntime.ValidateContext(context);

        if (Runtime.Current == context ||
            !WorldSceneCoordinatorRuntime.IsAllowedTransition(
                Runtime.Current,
                context))
        {
            return Runtime.TryTransition(context, out result);
        }

        WorldSceneShell? stagedShell = null;
        WorldSceneCoordinatorRuntimeSnapshot runtimeSnapshot =
            Runtime.CaptureSnapshot();
        try
        {
            stagedShell = StageShell(
                context,
                Runtime.Generation + 1,
                out string scenePath);
            if (!TryAttachStagedShell(stagedShell, out string attachFailure))
            {
                _sceneLoadFailures++;
                _rollbackCount++;
                result =
                    $"World scene transition aborted before state mutation: {attachFailure}";
                return WorldSceneTransitionResult.Rejected;
            }

            WorldSceneTransitionResult transition =
                Runtime.TryTransition(context, out string runtimeResult);
            if (transition != WorldSceneTransitionResult.Applied)
            {
                DiscardShell(stagedShell);
                result = runtimeResult;
                return transition;
            }

            CommitStagedShell(stagedShell, scenePath);
            result = runtimeResult;
            return WorldSceneTransitionResult.Applied;
        }
        catch (Exception exception)
        {
            Runtime.RestoreSnapshot(runtimeSnapshot);
            if (stagedShell is not null &&
                GodotObject.IsInstanceValid(stagedShell) &&
                stagedShell != _activeShell)
            {
                DiscardShell(stagedShell);
            }

            _sceneLoadFailures++;
            _rollbackCount++;
            result =
                "World scene transition rolled back after staged-shell failure: " +
                exception.Message;
            return WorldSceneTransitionResult.Rejected;
        }
    }

    public void Restore(WorldSceneContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        WorldSceneCoordinatorRuntime.ValidateContext(context);

        WorldSceneShell? stagedShell = null;
        WorldSceneCoordinatorRuntimeSnapshot runtimeSnapshot =
            Runtime.CaptureSnapshot();
        try
        {
            stagedShell = StageShell(
                context,
                Runtime.Generation + 1,
                out string scenePath);
            if (!TryAttachStagedShell(stagedShell, out string attachFailure))
            {
                _sceneLoadFailures++;
                _rollbackCount++;
                throw new InvalidOperationException(attachFailure);
            }

            Runtime.Restore(context);
            CommitStagedShell(stagedShell, scenePath);
        }
        catch
        {
            Runtime.RestoreSnapshot(runtimeSnapshot);
            if (stagedShell is not null &&
                GodotObject.IsInstanceValid(stagedShell) &&
                stagedShell != _activeShell)
            {
                DiscardShell(stagedShell);
            }
            throw;
        }
    }

    /// <summary>
    /// Exact non-persistent restore used by F5 acceptance cleanup. Counters and
    /// active context are restored to their pre-test values after a fresh shell
    /// for that context has successfully entered the coordinator tree.
    /// </summary>
    public void RestoreSnapshot(WorldSceneCoordinatorNodeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        WorldSceneCoordinatorRuntimeSnapshot beforeRuntime =
            Runtime.CaptureSnapshot();
        WorldSceneShell? stagedShell = null;
        try
        {
            stagedShell = StageShell(
                snapshot.Runtime.Current,
                snapshot.Runtime.Generation,
                out string scenePath);
            if (!TryAttachStagedShell(stagedShell, out string attachFailure))
            {
                _sceneLoadFailures++;
                _rollbackCount++;
                throw new InvalidOperationException(attachFailure);
            }

            Runtime.RestoreSnapshot(snapshot.Runtime);
            CommitStagedShell(stagedShell, scenePath);
            _reloadCount = snapshot.Reloads;
            _sceneLoadFailures = snapshot.SceneLoadFailures;
            _rollbackCount = snapshot.Rollbacks;
        }
        catch
        {
            Runtime.RestoreSnapshot(beforeRuntime);
            if (stagedShell is not null &&
                GodotObject.IsInstanceValid(stagedShell) &&
                stagedShell != _activeShell)
            {
                DiscardShell(stagedShell);
            }
            throw;
        }
    }

    public void ReloadCurrentShell(bool force)
    {
        string scenePath = GetScenePath(Runtime.Current.Kind);
        if (!force &&
            IsActiveShellValid() &&
            string.Equals(_activeScenePath, scenePath, StringComparison.Ordinal))
        {
            return;
        }

        WorldSceneShell stagedShell = StageShell(
            Runtime.Current,
            Runtime.Generation,
            out scenePath);
        if (!TryAttachStagedShell(stagedShell, out string attachFailure))
        {
            _sceneLoadFailures++;
            _rollbackCount++;
            throw new InvalidOperationException(
                "World scene shell reload aborted; previous shell retained: " +
                attachFailure);
        }

        CommitStagedShell(stagedShell, scenePath);
    }

    public WorldSceneCoordinatorDiagnostics CreateDiagnostics()
    {
        WorldSceneContext context = Runtime.Current;
        int hostChildren = GetChildCount();
        bool validShell = IsActiveShellValid();
        bool shellMatches = validShell &&
            _activeShell!.Kind == context.Kind &&
            _activeShell.GetParent() == this &&
            string.Equals(
                _activeShell.GetMeta("world_system_id").AsString(),
                context.SystemId,
                StringComparison.Ordinal) &&
            string.Equals(
                _activeShell.GetMeta("world_planet_id").AsString(),
                context.PlanetId,
                StringComparison.Ordinal) &&
            _activeShell.GetMeta("world_generation").AsInt32() == Runtime.Generation;

        return new WorldSceneCoordinatorDiagnostics(
            context.Kind,
            context.SystemId,
            context.PlanetId,
            _activeScenePath,
            validShell ? _activeShell!.EnvironmentProfile : string.Empty,
            hostChildren,
            Runtime.Generation,
            Runtime.TransitionCount,
            _reloadCount,
            Runtime.RejectedTransitions,
            Runtime.HyperspaceTransitions,
            _sceneLoadFailures,
            _rollbackCount,
            hostChildren == 1 && validShell,
            shellMatches);
    }

    public static bool ValidatePackedScenes(out string result)
    {
        foreach ((WorldSceneKind kind, string scenePath) in ScenePaths)
        {
            PackedScene? packed = GD.Load<PackedScene>(scenePath);
            if (packed is null)
            {
                result = $"missing packed scene {scenePath}";
                return false;
            }

            WorldSceneShell shell;
            try
            {
                shell = packed.Instantiate<WorldSceneShell>();
            }
            catch (Exception exception)
            {
                result = $"failed to instantiate {scenePath}: {exception.Message}";
                return false;
            }

            bool matches = shell.Kind == kind;
            shell.Free();
            if (!matches)
            {
                result = $"packed scene kind mismatch {scenePath}";
                return false;
            }
        }

        result = "world-scene-shells-ok";
        return true;
    }

    public static string GetScenePath(WorldSceneKind kind)
    {
        if (!ScenePaths.TryGetValue(kind, out string? path))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind), kind, "Unknown world scene kind.");
        }

        return path;
    }

    private WorldSceneShell StageShell(
        WorldSceneContext context,
        int generation,
        out string scenePath)
    {
        scenePath = GetScenePath(context.Kind);
        PackedScene packed = GD.Load<PackedScene>(scenePath) ??
            throw new InvalidOperationException(
                $"World scene shell could not be loaded: {scenePath}");
        WorldSceneShell shell = packed.Instantiate<WorldSceneShell>();
        if (shell.Kind != context.Kind)
        {
            shell.Free();
            throw new InvalidOperationException(
                $"World scene shell kind mismatch for {scenePath}: " +
                $"expected {context.Kind}, actual {shell.Kind}.");
        }

        shell.Name = $"Active{context.Kind}World";
        shell.SetMeta("world_system_id", context.SystemId);
        shell.SetMeta("world_planet_id", context.PlanetId);
        shell.SetMeta("world_generation", generation);
        return shell;
    }

    private bool TryAttachStagedShell(
        WorldSceneShell shell,
        out string failure)
    {
        try
        {
            AddChild(shell);
        }
        catch (Exception exception)
        {
            DiscardShell(shell);
            failure = "add_child threw: " + exception.Message;
            return false;
        }

        bool parentMatches = shell.GetParent() == this;
        bool treeMatches = !IsInsideTree() || shell.IsInsideTree();
        if (!parentMatches || !treeMatches)
        {
            DiscardShell(shell);
            failure =
                $"staged shell did not enter coordinator tree " +
                $"(parent={(parentMatches ? 1 : 0)}, tree={(treeMatches ? 1 : 0)}).";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private void CommitStagedShell(
        WorldSceneShell stagedShell,
        string scenePath)
    {
        WorldSceneShell? previousShell = _activeShell;
        _activeShell = stagedShell;
        _activeScenePath = scenePath;
        _reloadCount++;

        if (previousShell is not null &&
            previousShell != stagedShell &&
            GodotObject.IsInstanceValid(previousShell))
        {
            if (previousShell.GetParent() == this)
            {
                RemoveChild(previousShell);
            }
            previousShell.QueueFree();
        }
    }

    private bool IsActiveShellValid()
    {
        return _activeShell is not null &&
            GodotObject.IsInstanceValid(_activeShell) &&
            _activeShell.GetParent() == this;
    }

    private void DiscardShell(WorldSceneShell shell)
    {
        if (!GodotObject.IsInstanceValid(shell))
        {
            return;
        }

        if (shell.GetParent() == this)
        {
            RemoveChild(shell);
        }
        shell.QueueFree();
    }
}
