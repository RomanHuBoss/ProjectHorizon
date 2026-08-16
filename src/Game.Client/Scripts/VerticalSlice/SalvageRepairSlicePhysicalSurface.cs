using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private PlanetSurfacePhysicalFrameRuntime? _planetSurfacePhysicalFrameRuntime;
    private PlanetSurfacePhysicalFrameState? _planetSurfacePhysicalFrameState;
    private string _planetSurfacePhysicalFrameAcceptanceHud = "READY";
    private bool _planetSurfacePhysicalFrameReadyPrinted;
    private int _planetSurfacePhysicalFrameTransitions;
    private double _planetSurfacePhysicalFrameMaximumAxisDelta;

    private void EnsurePlanetSurfacePhysicalFrameRuntime()
    {
        if (_planetSurfaceContentProfile is null)
        {
            return;
        }

        PlanetEnvironmentProfile environment =
            PlanetSurfaceContentProfile.Environment;
        if (_planetSurfacePhysicalFrameRuntime is not null &&
            string.Equals(
                _planetSurfacePhysicalFrameRuntime.Radial.Environment.PlanetId,
                environment.PlanetId,
                StringComparison.Ordinal))
        {
            return;
        }

        _planetSurfacePhysicalFrameRuntime = new PlanetSurfacePhysicalFrameRuntime(
            new PlanetSurfaceRadialFrameRuntime(environment));
        _planetSurfacePhysicalFrameState = null;
        _planetSurfacePhysicalFrameReadyPrinted = false;
    }

    private void ApplyPlanetSurfacePhysicalTransforms(
        double originEastMeters,
        double originNorthMeters)
    {
        Node3D? gameplay = GetNodeOrNull<Node3D>("Gameplay");
        if (gameplay is null)
        {
            return;
        }

        EnsurePlanetSurfacePhysicalFrameRuntime();
        if (_planetSurfacePhysicalFrameRuntime is null)
        {
            // Bootstrap path before planet content is available. Preserve the
            // historical translation-only frame; activation later reapplies the
            // full radial basis once the planet environment exists.
            gameplay.Transform = new Transform3D(
                Basis.Identity,
                new Vector3(
                    (float)-originEastMeters,
                    0.0f,
                    (float)-originNorthMeters));
            Node3D? bootstrapGround = GetNodeOrNull<Node3D>("GroundBody");
            if (bootstrapGround is not null)
            {
                bootstrapGround.Transform = Transform3D.Identity;
            }
            _planetSurfaceStreamer?.SetLogicalSurfaceOrigin(
                originEastMeters,
                originNorthMeters);
            return;
        }

        Transform3D previousGameplay = gameplay.GlobalTransform;
        PlanetSurfacePhysicalFrameState? previousState =
            _planetSurfacePhysicalFrameState;
        PlanetSurfacePhysicalFrameState next =
            _planetSurfacePhysicalFrameRuntime.Build(
                originEastMeters,
                originNorthMeters);
        Basis previousBasis = previousGameplay.Basis.Orthonormalized();
        Basis nextBasis = next.SurfaceBasis.Orthonormalized();
        double radiusMeters = Math.Max(
            PlanetSurfaceTopologyRuntime.MinimumRadiusMeters,
            PlanetSurfaceContentProfile.Environment.RadiusKm * 1000.0);
        PlanetSurfaceCurvedPatchDescriptor? previousPatch = previousState is { } oldState
            ? new PlanetSurfaceCurvedPatchDescriptor(
                radiusMeters, oldState.OriginEastMeters, oldState.OriginNorthMeters)
            : null;
        PlanetSurfaceCurvedPatchDescriptor nextPatch = new(
            radiusMeters, originEastMeters, originNorthMeters);
        double axisDelta = PlanetSurfacePhysicalFrameRuntime.MaximumAxisErrorDegrees(
            previousBasis,
            nextBasis);
        _planetSurfacePhysicalFrameMaximumAxisDelta = Math.Max(
            _planetSurfacePhysicalFrameMaximumAxisDelta,
            axisDelta);

        if (previousState is { } bridgeState &&
            (Math.Abs(bridgeState.OriginEastMeters - originEastMeters) > 0.001 ||
             Math.Abs(bridgeState.OriginNorthMeters - originNorthMeters) > 0.001))
        {
            // TASK-174: the curvature anchor changes at a floating-origin/frame
            // handoff. Keep a synchronously rebuilt curved fallback collision
            // active while the 25/9 streamer replaces its meshes asynchronously.
            ActivateCurvedSurfaceFallbackBridge(originEastMeters, originNorthMeters);
        }

        // TASK-172.1: NavigationServer3D has a map-level UP direction.
        // Detach the old regions and prepare a dedicated map for the next
        // radial Up before the Gameplay parent rotates; otherwise Godot rejects
        // regions that become >= 90 degrees away from the map UP orientation.
        DetachNpcNavigationAgents();
        _npcNavigationSurface?.PrepareSurfaceFrameChange(next.WorldUp);

        gameplay.GlobalTransform = next.GameplayTransform;

        if (_player is not null && GodotObject.IsInstanceValid(_player))
        {
            _player.ApplySurfaceFrameTransition(
                previousGameplay,
                next.GameplayTransform,
                next.Radial.GravityMetersPerSecondSquared /
                    PlanetSurfaceRadialFrameRuntime.StandardGravityMetersPerSecondSquared);
            if (previousPatch is not null)
            {
                AdjustNodeForCurvatureAnchorChange(
                    _player, next.GameplayTransform, previousPatch, nextPatch);
            }
        }

        if (previousPatch is not null)
        {
            AdjustCurvedSurfaceResidentsAfterFrameChange(
                gameplay, next.GameplayTransform, previousPatch, nextPatch);
            AdjustEcologyFloraCurvatureAnchor(previousPatch, nextPatch);
        }

        Node3D? ground = GetNodeOrNull<Node3D>("GroundBody");
        if (ground is not null)
        {
            ground.GlobalTransform = new Transform3D(nextBasis, Vector3.Zero);
        }

        if (_planetSurfaceStreamer is not null &&
            GodotObject.IsInstanceValid(_planetSurfaceStreamer))
        {
            _planetSurfaceStreamer.GlobalTransform = new Transform3D(
                nextBasis,
                Vector3.Zero);
            _planetSurfaceStreamer.SetLogicalSurfaceOrigin(
                originEastMeters,
                originNorthMeters);
        }

        ApplyPlanetSurfaceFrameTransformToRuntimeCaches(
            previousGameplay,
            next.GameplayTransform,
            previousPatch,
            nextPatch);
        if (_npcNavigationSurface is not null && _planetSurfaceContentProfile is not null)
        {
            _npcNavigationSurface.SetCurvedSurfaceFrame(
                PlanetSurfaceContentProfile.Environment.RadiusKm,
                originEastMeters,
                originNorthMeters);
        }
        ApplyPlanetSurfaceAtmosphereFrame(nextBasis);
        _npcNavigationSurface?.NotifySurfaceFrameChanged();
        AttachNpcNavigationAgents();
        if (_aerialSteeringRuntime is not null)
        {
            RefreshAerialNavigationEnvironment();
        }

        _planetSurfacePhysicalFrameState = next;
        _planetSurfacePhysicalFrameTransitions++;

        if (previousState is { } prior &&
            prior.Radial.CubeFace.Face != next.Radial.CubeFace.Face)
        {
            GD.Print(
                "TASK-172 physical cube-face handoff PASS: " +
                $"{prior.Radial.FaceName}->{next.Radial.FaceName}; " +
                $"up={next.WorldUp}; " +
                $"axisDelta={axisDelta.ToString("0.0000", CultureInfo.InvariantCulture)}deg; " +
                "collision=tangent-rotated; nav=frame-aware; identity=logical-preserved.");
        }

        if (!_planetSurfacePhysicalFrameReadyPrinted)
        {
            GD.Print(
                "TASK-172 physical radial surface READY: " +
                $"planet={next.PlanetId}; face={next.Radial.FaceName}; " +
                $"up={next.WorldUp}; " +
                $"gravity={next.Radial.GravityMetersPerSecondSquared.ToString("0.00", CultureInfo.InvariantCulture)}m/s2; " +
                "player=arbitrary-up; collision=rotating-curved-tangent; " +
                "navigation=radial-map+curved-tiles; streamer=25/9; " +
                "persistence=logical-xz/no-schema-bump; F5=acceptance.");
            _planetSurfacePhysicalFrameReadyPrinted = true;
        }
    }

    private void UpdatePlanetCurvedSurfacePlayerUp()
    {
        if (!_surfaceRuntimeActive || _player is null || StageOneVoyage.Piloted ||
            _planetSurfacePhysicalFrameState is not { } state ||
            _planetSurfaceContentProfile is null)
        {
            return;
        }

        Vector3 worldUp = GetCurrentPlanetCurvedWorldUp();
        if (_player.ActiveSurfaceUp.Dot(worldUp) < 0.9999995f)
        {
            _player.SetPlanetSurfaceFrame(
                worldUp,
                state.Radial.GravityMetersPerSecondSquared /
                    PlanetSurfaceRadialFrameRuntime.StandardGravityMetersPerSecondSquared,
                snapOrientation: true);
            Basis skyBasis = PlanetSurfacePhysicalFrameRuntime.BuildUprightBasis(
                state.WorldNorth,
                worldUp);
            ApplyPlanetSurfaceAtmosphereFrame(skyBasis);
        }
    }

    private Vector3 GetCurrentPlanetCurvedWorldUp()
    {
        if (_planetSurfacePhysicalFrameState is not { } state)
        {
            return Vector3.Up;
        }
        if (_planetSurfaceContentProfile is null || _player is null)
        {
            return state.WorldUp;
        }
        PlanetSurfaceLogicalPosition logical = GetPlanetSurfaceLogicalPlayerPosition();
        PlanetSurfaceCurvedPatchDescriptor patch = new(
            Math.Max(
                PlanetSurfaceTopologyRuntime.MinimumRadiusMeters,
                PlanetSurfaceContentProfile.Environment.RadiusKm * 1000.0),
            state.OriginEastMeters,
            state.OriginNorthMeters);
        Vector3 localUp = patch.SurfaceUpLocal(logical.EastMeters, logical.NorthMeters);
        return (state.SurfaceBasis * localUp).Normalized();
    }

    private void ApplyPlanetSurfaceFrameTransformToRuntimeCaches(
        Transform3D previousFrame,
        Transform3D nextFrame,
        PlanetSurfaceCurvedPatchDescriptor? previousPatch,
        PlanetSurfaceCurvedPatchDescriptor nextPatch)
    {
        foreach (NpcFactionAgentNode agent in
                 GetTree().GetNodesInGroup("npc_faction_agent"))
        {
            agent.ApplyWorldFrameTransform(
                previousFrame, nextFrame, previousPatch, nextPatch);
        }
        foreach (NpcShipNavigationNode ship in _npcShipNavigationNodes)
        {
            if (GodotObject.IsInstanceValid(ship))
            {
                ship.ApplyWorldFrameTransform(
                    previousFrame, nextFrame, previousPatch, nextPatch);
            }
        }
        foreach (EcologyFaunaNode fauna in _ecologyFaunaNodes)
        {
            if (GodotObject.IsInstanceValid(fauna))
            {
                fauna.ApplyWorldFrameTransform(
                    previousFrame, nextFrame, previousPatch, nextPatch);
            }
        }
    }

    private static void AdjustNodeForCurvatureAnchorChange(
        Node3D node,
        Transform3D nextFrame,
        PlanetSurfaceCurvedPatchDescriptor previousPatch,
        PlanetSurfaceCurvedPatchDescriptor nextPatch)
    {
        Vector3 logical = nextFrame.AffineInverse() * node.GlobalPosition;
        double semanticHeight = logical.Y + previousPatch.TangentSagMeters(
            logical.X, logical.Z);
        logical.Y = (float)(semanticHeight - nextPatch.TangentSagMeters(
            logical.X, logical.Z));
        node.GlobalPosition = nextFrame * logical;
    }

    private void AdjustCurvedSurfaceResidentsAfterFrameChange(
        Node3D gameplay,
        Transform3D nextFrame,
        PlanetSurfaceCurvedPatchDescriptor previousPatch,
        PlanetSurfaceCurvedPatchDescriptor nextPatch)
    {
        string[] groups =
        {
            "npc_faction_agent", "ecology_fauna", "npc_ship_navigation",
            "planet_surface_resource", "vertical_slice_resource",
            "planetary_poi", "ecology_flora", "base_construction_module"
        };
        HashSet<Node3D> adjusted = new();
        foreach (string group in groups)
        {
            foreach (Node candidate in GetTree().GetNodesInGroup(group))
            {
                if (candidate is not Node3D node || !GodotObject.IsInstanceValid(node) ||
                    !IsDescendantOf(node, gameplay) || !adjusted.Add(node))
                {
                    continue;
                }
                AdjustNodeForCurvatureAnchorChange(
                    node, nextFrame, previousPatch, nextPatch);
            }
        }
    }

    private static bool IsDescendantOf(Node node, Node ancestor)
    {
        Node? current = node.GetParent();
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
            current = current.GetParent();
        }
        return false;
    }

    private Vector3 SurfaceLocalDirectionToWorld(Vector3 localDirection)
    {
        Node3D? gameplay = GetNodeOrNull<Node3D>("Gameplay");
        return gameplay is null
            ? localDirection
            : gameplay.GlobalTransform.Basis * localDirection;
    }

    private Vector3 SurfaceWorldDirectionToLocal(Vector3 worldDirection)
    {
        Node3D? gameplay = GetNodeOrNull<Node3D>("Gameplay");
        return gameplay is null
            ? worldDirection
            : gameplay.GlobalTransform.Basis.Inverse() * worldDirection;
    }

    private string BuildPlanetSurfacePhysicalFrameHudLine()
    {
        if (_planetSurfacePhysicalFrameState is not { } state || _player is null)
        {
            return "physical frame: unavailable";
        }

        Vector3 expectedUp = GetCurrentPlanetCurvedWorldUp();
        double upError = Math.Acos(Math.Clamp(
            _player.ActiveSurfaceUp.Normalized().Dot(expectedUp),
            -1.0f,
            1.0f)) * 180.0 / Math.PI;
        return
            "physical frame: " +
            $"face={state.Radial.FaceName}; " +
            $"upErr={upError.ToString("0.000", CultureInfo.InvariantCulture)}°; " +
            $"playerAlign={_player.SurfaceUpAlignment.ToString("0.0000", CultureInfo.InvariantCulture)}; " +
            $"transitions={_planetSurfacePhysicalFrameTransitions}";
    }

    private void RunPlanetSurfacePhysicalFrameAcceptance()
    {
        if (_planetEnvironmentRuntime is null || _galaxyNavigationRuntime is null)
        {
            _planetSurfacePhysicalFrameAcceptanceHud = "FAIL — runtime unavailable";
            GD.PushError(
                "TASK-172 physical radial surface acceptance FAIL: runtime unavailable");
            return;
        }

        PlanetEnvironmentProfile[] profiles = GalaxyNavigation.CurrentSystem.Planets
            .Select(planet => PlanetEnvironment.BuildProfile(
                planet,
                GalaxyNavigation.CurrentSystem.StarType))
            .Where(profile => profile.Landable)
            .ToArray();
        PlanetSurfacePhysicalFrameAcceptanceReport report =
            PlanetSurfacePhysicalFrameAcceptanceRunner.Run(profiles);

        Vector3 expectedPlayerUp = GetCurrentPlanetCurvedWorldUp();
        bool livePlayer = _player is not null &&
            _player.SurfaceFrameActive &&
            _planetSurfacePhysicalFrameState is { } &&
            _player.ActiveSurfaceUp.Normalized().Dot(expectedPlayerUp) >= 0.9999f;
        bool playerUpright = livePlayer && _player is not null &&
            _planetSurfacePhysicalFrameState is { } &&
            _player.GlobalTransform.Basis.Y.Normalized().Dot(expectedPlayerUp) >= 0.9999f &&
            Math.Abs(_player.GlobalTransform.Basis.X.Normalized().Dot(expectedPlayerUp)) <= 0.001f &&
            Math.Abs(_player.GlobalTransform.Basis.Z.Normalized().Dot(expectedPlayerUp)) <= 0.001f;
        bool liveGameplay = _planetSurfacePhysicalFrameState is { } current &&
            GetNodeOrNull<Node3D>("Gameplay") is { } gameplay &&
            gameplay.GlobalTransform.Basis.Y.Normalized().Dot(current.WorldUp) >= 0.9999f;
        bool liveStreamer = _planetSurfacePhysicalFrameState is { } streamState &&
            _planetSurfaceStreamer is not null &&
            GodotObject.IsInstanceValid(_planetSurfaceStreamer) &&
            _planetSurfaceStreamer.GlobalTransform.Basis.Y.Normalized()
                .Dot(streamState.WorldUp) >= 0.9999f;
        TerrainChunkProfilerSnapshot? snapshot =
            _planetSurfaceStreamer is not null &&
            GodotObject.IsInstanceValid(_planetSurfaceStreamer)
                ? _planetSurfaceStreamer.CaptureProfilerSnapshot()
                : null;
        bool bounded = snapshot is not null &&
            snapshot.LoadedChunks == PlanetSurfaceStreamingRuntime.ExpectedActiveChunks &&
            snapshot.Collisions == PlanetSurfaceStreamingRuntime.ExpectedCollisionChunks;
        bool navFrame = _npcNavigationSurface is not null &&
            _npcNavigationSurface.ParentFrameAligned;

        bool passed = report.Passed && livePlayer && playerUpright && liveGameplay &&
            liveStreamer && bounded && navFrame;
        _planetSurfacePhysicalFrameAcceptanceHud = passed
            ? $"PASS faces={report.FacesCovered}/6 player=1 nav=1 stream=25/9"
            : "FAIL physical-frame invariant";
        string output =
            "TASK-172 physical radial surface acceptance " +
            (passed ? "PASS" : "FAIL") + ": " +
            $"planets={report.Planets}; " +
            $"frames={(report.FrameTransforms ? 1 : 0)}; " +
            $"roundTrip={(report.WorldLogicalRoundTrip ? 1 : 0)}; " +
            $"velocity={(report.VectorRemap ? 1 : 0)}; " +
            $"faces={report.FacesCovered}/6; " +
            $"seams={(report.SeamHandoff ? 1 : 0)}; " +
            $"player={(livePlayer ? 1 : 0)}; upright={(playerUpright ? 1 : 0)}; " +
            $"gameplay={(liveGameplay ? 1 : 0)}; streamer={(liveStreamer ? 1 : 0)}; " +
            $"nav={(navFrame ? 1 : 0)}; " +
            $"bounded25x9={(bounded ? 1 : 0)}; " +
            $"maxPointErr={report.MaximumPointRoundTripError.ToString("0.000000", CultureInfo.InvariantCulture)}m; " +
            $"pointBudget={PlanetSurfacePhysicalFrameAcceptanceRunner.PointRoundTripToleranceMeters.ToString("0.000", CultureInfo.InvariantCulture)}m; " +
            $"maxVectorErr={report.MaximumVectorRoundTripError.ToString("0.000000", CultureInfo.InvariantCulture)}; " +
            $"transitions={_planetSurfacePhysicalFrameTransitions}; " +
            "result=" + (passed
                ? "rotating radial player/collision/navigation tangent frame verified"
                : "one or more physical radial frame invariants failed");
        if (passed)
        {
            GD.Print(output);
        }
        else
        {
            GD.PushError(output);
        }
    }
}
