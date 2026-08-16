using System;
using Godot;

public partial class SalvageRepairSlice
{
    private Node3D? _spaceStarfieldRoot;
    private MultiMeshInstance3D? _spaceStarfield;
    private bool _orbitalBackdropReadyPrinted;

    private void UpdateOrbitalBackdropRuntime()
    {
        EnsureOrbitalBackdropRuntime();
        if (_spaceStarfieldRoot is null || _worldSceneCoordinatorRuntime is null)
        {
            return;
        }

        WorldSceneKind kind = WorldScenes.Current.Kind;
        double altitude = _voyageShip?.AltitudeAboveSurface ??
            double.PositiveInfinity;
        OrbitalHandoffPresentationState handoff =
            OrbitalHandoffPresentationRuntime.Evaluate(altitude);

        bool starfieldVisible = kind is
            WorldSceneKind.InterplanetaryTransit or
            WorldSceneKind.HyperspaceTransit ||
            (kind == WorldSceneKind.Orbit && handoff.StarfieldVisible);
        _spaceStarfieldRoot.Visible = starfieldVisible;
        if (starfieldVisible)
        {
            _spaceStarfieldRoot.GlobalPosition = _voyageShip?.GlobalPosition ??
                Vector3.Zero;
        }

        bool stationVisible = kind == WorldSceneKind.Orbit &&
            _stageOneVoyageRuntime is not null &&
            (StageOneVoyage.Location is
                StageOneVoyageLocation.OutboundFlight or
                StageOneVoyageLocation.InboundFlight) &&
            handoff.StationVisible;
        ApplyOrbitalStationVisibility(stationVisible);
    }

    private void EnsureOrbitalBackdropRuntime()
    {
        if (_spaceStarfieldRoot is not null &&
            GodotObject.IsInstanceValid(_spaceStarfieldRoot))
        {
            return;
        }

        _spaceStarfieldRoot = new Node3D
        {
            Name = "SpaceStarfield",
            Visible = false
        };

        StandardMaterial3D material = new()
        {
            AlbedoColor = new Color(0.78f, 0.88f, 1.0f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = new Color(0.72f, 0.84f, 1.0f),
            EmissionEnergyMultiplier = 2.2f
        };
        SphereMesh starMesh = new()
        {
            Radius = 2.4f,
            Height = 4.8f,
            Material = material
        };
        MultiMesh multi = new()
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = starMesh,
            InstanceCount = OrbitalHandoffPresentationRuntime.StarCount
        };

        const double goldenAngle = 2.39996322972865332;
        for (int index = 0;
             index < OrbitalHandoffPresentationRuntime.StarCount;
             index++)
        {
            double unit = (index + 0.5) /
                OrbitalHandoffPresentationRuntime.StarCount;
            double y = 1.0 - (2.0 * unit);
            double horizontal = Math.Sqrt(Math.Max(0.0, 1.0 - (y * y)));
            double angle = index * goldenAngle;
            Vector3 direction = new(
                (float)(Math.Cos(angle) * horizontal),
                (float)y,
                (float)(Math.Sin(angle) * horizontal));
            float distanceJitter = 0.92f +
                ((index * 37 % 101) / 100.0f) * 0.08f;
            float scale = 0.55f +
                ((index * 53 % 97) / 96.0f) * 1.85f;
            Vector3 position = direction *
                (OrbitalHandoffPresentationRuntime.StarfieldRadiusMeters *
                 distanceJitter);
            multi.SetInstanceTransform(
                index,
                new Transform3D(
                    Basis.Identity.Scaled(Vector3.One * scale),
                    position));
        }

        _spaceStarfield = new MultiMeshInstance3D
        {
            Name = "Stars",
            Multimesh = multi
        };
        _spaceStarfieldRoot.AddChild(_spaceStarfield);
        AddChild(_spaceStarfieldRoot);

        if (!_orbitalBackdropReadyPrinted)
        {
            _orbitalBackdropReadyPrinted = true;
            GD.Print(
                "TASK-178.3 orbital backdrop READY: " +
                $"stars={OrbitalHandoffPresentationRuntime.StarCount}; " +
                $"radius={OrbitalHandoffPresentationRuntime.StarfieldRadiusMeters:0}m; " +
                $"stationTravel={OrbitalHandoffPresentationRuntime.StationTravelDistanceMeters():0}m; " +
                $"surfaceRuntime={PlanetRuntimeActivationRadiusMeters:0}m; " +
                $"handoff={OrbitalHandoffPresentationRuntime.VacuumBlendStartMeters:0}.." +
                $"{OrbitalHandoffPresentationRuntime.VacuumBlendEndMeters:0}m.");
        }
    }

    private void ApplyOrbitalStationVisibility(bool visible)
    {
        if (_orbitalStation is not null)
        {
            _orbitalStation.Visible = visible;
            if (_orbitalStation is CollisionObject3D collision)
            {
                collision.CollisionLayer = visible
                    ? _orbitalStationCollisionLayer
                    : 0u;
                collision.CollisionMask = visible
                    ? _orbitalStationCollisionMask
                    : 0u;
            }
        }

        if (_orbitalDockMarker is not null &&
            (!visible ||
             _stageOneVoyageRuntime?.Location !=
                 StageOneVoyageLocation.OutboundFlight))
        {
            _orbitalDockMarker.Visible = false;
        }
    }
}
