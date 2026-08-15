using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public sealed record StarSystemSimulationDiagnostics(
    string SystemId,
    int GeneratedBodies,
    int PlanetBodies,
    int MoonBodies,
    int StationBodies,
    int ShipContacts,
    int VisualNodes,
    int RuntimeSamples,
    int Rebuilds,
    int DetailedPlanets,
    int Proxies,
    int Markers,
    int Statistical,
    bool SurfaceRuntimeActive,
    string FocusPlanetId);

public partial class StarSystemSimulationNode : Node3D
{
    private readonly Dictionary<string, MeshInstance3D> _visuals = new(
        StringComparer.Ordinal);
    private StarSystemSimulationRuntime? _runtime;
    private StarSystemSimulationSnapshot? _snapshot;
    private int _runtimeSamples;
    private int _rebuilds;
    private bool _surfaceRuntimeActive = true;
    private string _focusPlanetId = string.Empty;

    public Vector3 DisplayAnchor { get; set; } =
        new(0.0f, 115.0f, -390.0f);

    public string CurrentSystemId => _runtime?.SystemId ?? string.Empty;

    public void Configure(StarSystemSimulationRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        foreach (MeshInstance3D visual in _visuals.Values)
        {
            if (GodotObject.IsInstanceValid(visual))
            {
                if (visual.GetParent() == this)
                {
                    RemoveChild(visual);
                }
                visual.QueueFree();
            }
        }
        _visuals.Clear();
        _runtime = runtime;
        _snapshot = null;
        _runtimeSamples = 0;
        _rebuilds++;

        foreach (StarSystemBodyDefinition definition in runtime.Definitions)
        {
            MeshInstance3D visual = CreateVisual(definition);
            AddChild(visual);
            _visuals.Add(definition.BodyId, visual);
        }
    }

    public void UpdateSimulation(
        double delta,
        string focusPlanetId,
        bool surfaceRuntimeActive,
        bool renderSystemProxies)
    {
        if (_runtime is null)
        {
            return;
        }
        _focusPlanetId = focusPlanetId;
        _surfaceRuntimeActive = surfaceRuntimeActive;
        _runtime.Advance(delta);
        _snapshot = _runtime.CreateSnapshot(
            focusPlanetId,
            focusPlanetId,
            surfaceRuntimeActive);
        _runtimeSamples++;
        Visible = renderSystemProxies;
        RenderSnapshot(_snapshot);
    }

    public bool TryGetBodyDisplayPosition(
        string bodyId,
        out Vector3 globalPosition)
    {
        if (!string.IsNullOrWhiteSpace(bodyId) &&
            _visuals.TryGetValue(bodyId, out MeshInstance3D? visual) &&
            GodotObject.IsInstanceValid(visual) &&
            visual.IsInsideTree())
        {
            globalPosition = visual.GlobalPosition;
            return true;
        }

        globalPosition = Vector3.Zero;
        return false;
    }

    public StarSystemSimulationDiagnostics CreateDiagnostics()
    {
        StarSystemSimulationRuntime? runtime = _runtime;
        StarSystemSimulationSnapshot? snapshot = _snapshot;
        if (runtime is null)
        {
            return new StarSystemSimulationDiagnostics(
                string.Empty,
                0,
                0,
                0,
                0,
                0,
                0,
                _runtimeSamples,
                _rebuilds,
                0,
                0,
                0,
                0,
                _surfaceRuntimeActive,
                _focusPlanetId);
        }

        int liveVisuals = _visuals.Values.Count(visual =>
            GodotObject.IsInstanceValid(visual) && visual.GetParent() == this);
        return new StarSystemSimulationDiagnostics(
            runtime.SystemId,
            runtime.Definitions.Count,
            runtime.PlanetCount,
            runtime.MoonCount,
            runtime.StationCount,
            runtime.ShipContactCount,
            liveVisuals,
            _runtimeSamples,
            _rebuilds,
            snapshot?.DetailedPlanetCount ?? 0,
            snapshot?.ProxyCount ?? 0,
            snapshot?.MarkerCount ?? 0,
            snapshot?.StatisticalCount ?? 0,
            _surfaceRuntimeActive,
            _focusPlanetId);
    }

    private void RenderSnapshot(StarSystemSimulationSnapshot snapshot)
    {
        StarSystemBodyState focusState = snapshot.Bodies.First(body =>
            string.Equals(
                body.Definition.BodyId,
                snapshot.FocusBodyId,
                StringComparison.Ordinal));
        SystemDouble3 focus = focusState.Position;
        foreach (StarSystemBodyState state in snapshot.Bodies)
        {
            if (!_visuals.TryGetValue(
                    state.Definition.BodyId,
                    out MeshInstance3D? visual))
            {
                continue;
            }

            SystemDouble3 relative = state.Position - focus;
            visual.Position = DisplayAnchor + new Vector3(
                (float)relative.X,
                (float)relative.Y,
                (float)relative.Z);
            visual.Visible = state.Representation is
                StarSystemRepresentation.Proxy or
                StarSystemRepresentation.Marker;
            float representationScale = state.Representation ==
                    StarSystemRepresentation.Marker
                ? 0.28f
                : 1.0f;
            visual.Scale = Vector3.One * representationScale;
        }
    }

    private static MeshInstance3D CreateVisual(
        StarSystemBodyDefinition definition)
    {
        MeshInstance3D visual = new()
        {
            Name = definition.BodyId.Replace('.', '_')
        };
        StandardMaterial3D material = new()
        {
            AlbedoColor = ResolveColor(definition)
        };
        if (definition.Kind is StarSystemBodyKind.Station or
            StarSystemBodyKind.ShipContact)
        {
            BoxMesh mesh = new()
            {
                Size = definition.Kind == StarSystemBodyKind.Station
                    ? new Vector3(
                        (float)(definition.VisualRadius * 2.0),
                        (float)Math.Max(1.0, definition.VisualRadius * 0.65),
                        (float)(definition.VisualRadius * 1.35))
                    : new Vector3(1.2f, 0.45f, 1.8f),
                Material = material
            };
            visual.Mesh = mesh;
        }
        else
        {
            float radius = (float)Math.Max(0.25, definition.VisualRadius);
            SphereMesh mesh = new()
            {
                Radius = radius,
                Height = radius * 2.0f,
                RadialSegments = definition.Kind == StarSystemBodyKind.Star
                    ? 24
                    : 16,
                Rings = definition.Kind == StarSystemBodyKind.Star
                    ? 12
                    : 8,
                Material = material
            };
            visual.Mesh = mesh;
        }
        return visual;
    }

    private static Color ResolveColor(StarSystemBodyDefinition definition)
    {
        if (definition.Kind == StarSystemBodyKind.Star)
        {
            return definition.Archetype switch
            {
                nameof(GalaxyStarType.RedDwarf) => new Color(1.0f, 0.32f, 0.18f),
                nameof(GalaxyStarType.OrangeDwarf) => new Color(1.0f, 0.55f, 0.2f),
                nameof(GalaxyStarType.YellowStar) => new Color(1.0f, 0.9f, 0.42f),
                nameof(GalaxyStarType.WhiteStar) => new Color(0.86f, 0.92f, 1.0f),
                nameof(GalaxyStarType.BlueStar) => new Color(0.42f, 0.68f, 1.0f),
                _ => new Color(0.78f, 0.86f, 1.0f)
            };
        }
        if (definition.Kind == StarSystemBodyKind.Moon)
        {
            return new Color(0.58f, 0.62f, 0.67f);
        }
        if (definition.Kind == StarSystemBodyKind.Station)
        {
            return new Color(0.25f, 0.72f, 0.95f);
        }
        if (definition.Kind == StarSystemBodyKind.ShipContact)
        {
            return definition.Archetype == "security"
                ? new Color(0.3f, 0.72f, 1.0f)
                : definition.Archetype == "trader"
                    ? new Color(0.35f, 1.0f, 0.58f)
                    : new Color(0.82f, 0.82f, 0.88f);
        }
        return definition.Archetype switch
        {
            "temperate" => new Color(0.25f, 0.72f, 0.38f),
            "desert" => new Color(0.85f, 0.62f, 0.27f),
            "frozen" => new Color(0.62f, 0.83f, 1.0f),
            "volcanic" => new Color(0.82f, 0.22f, 0.12f),
            "toxic" => new Color(0.48f, 0.82f, 0.18f),
            "radioactive" => new Color(0.76f, 0.9f, 0.18f),
            "oceanic" => new Color(0.08f, 0.42f, 0.82f),
            "gas_giant" => new Color(0.65f, 0.48f, 0.8f),
            _ => new Color(0.5f, 0.48f, 0.45f)
        };
    }
}
