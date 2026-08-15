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
    bool SingleScene,
    bool ShellMatchesContext);

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

    public WorldSceneCoordinatorRuntime Runtime =>
        _runtime ?? throw new InvalidOperationException(
            "World scene coordinator runtime is unavailable.");

    public void Configure(WorldSceneCoordinatorRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
        ReloadCurrentShell(force: true);
    }

    public WorldSceneTransitionResult TryTransition(
        WorldSceneContext context,
        out string result)
    {
        WorldSceneTransitionResult transition =
            Runtime.TryTransition(context, out result);
        if (transition == WorldSceneTransitionResult.Applied)
        {
            ReloadCurrentShell(force: false);
        }

        return transition;
    }

    public void Restore(WorldSceneContext context)
    {
        Runtime.Restore(context);
        ReloadCurrentShell(force: true);
    }

    public void ReloadCurrentShell(bool force)
    {
        string scenePath = GetScenePath(Runtime.Current.Kind);
        if (!force &&
            _activeShell is not null &&
            GodotObject.IsInstanceValid(_activeShell) &&
            string.Equals(_activeScenePath, scenePath, StringComparison.Ordinal))
        {
            return;
        }

        RemoveActiveShell();
        PackedScene packed = GD.Load<PackedScene>(scenePath) ??
            throw new InvalidOperationException(
                $"World scene shell could not be loaded: {scenePath}");
        WorldSceneShell shell = packed.Instantiate<WorldSceneShell>();
        if (shell.Kind != Runtime.Current.Kind)
        {
            shell.Free();
            throw new InvalidOperationException(
                $"World scene shell kind mismatch for {scenePath}: " +
                $"expected {Runtime.Current.Kind}, actual {shell.Kind}.");
        }

        shell.Name = $"Active{Runtime.Current.Kind}World";
        shell.SetMeta("world_system_id", Runtime.Current.SystemId);
        shell.SetMeta("world_planet_id", Runtime.Current.PlanetId);
        shell.SetMeta("world_generation", Runtime.Generation);
        AddChild(shell);
        _activeShell = shell;
        _activeScenePath = scenePath;
        _reloadCount++;
    }

    public WorldSceneCoordinatorDiagnostics CreateDiagnostics()
    {
        WorldSceneContext context = Runtime.Current;
        int hostChildren = GetChildCount();
        bool validShell = _activeShell is not null &&
            GodotObject.IsInstanceValid(_activeShell);
        bool shellMatches = validShell &&
            _activeShell!.Kind == context.Kind &&
            string.Equals(
                _activeShell.GetMeta("world_system_id").AsString(),
                context.SystemId,
                StringComparison.Ordinal) &&
            string.Equals(
                _activeShell.GetMeta("world_planet_id").AsString(),
                context.PlanetId,
                StringComparison.Ordinal);

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

    private void RemoveActiveShell()
    {
        if (_activeShell is null)
        {
            return;
        }

        if (GodotObject.IsInstanceValid(_activeShell))
        {
            if (_activeShell.GetParent() == this)
            {
                RemoveChild(_activeShell);
            }
            _activeShell.QueueFree();
        }

        _activeShell = null;
        _activeScenePath = string.Empty;
    }
}
