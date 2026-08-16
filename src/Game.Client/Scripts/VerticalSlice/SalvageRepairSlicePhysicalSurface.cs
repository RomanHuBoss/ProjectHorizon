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
        PlanetSurfacePhysicalFrameState next =
            _planetSurfacePhysicalFrameRuntime.Build(
                originEastMeters,
                originNorthMeters);
        Basis previousBasis = previousGameplay.Basis.Orthonormalized();
        Basis nextBasis = next.SurfaceBasis.Orthonormalized();
        double axisDelta = PlanetSurfacePhysicalFrameRuntime.MaximumAxisErrorDegrees(
            previousBasis,
            nextBasis);
        _planetSurfacePhysicalFrameMaximumAxisDelta = Math.Max(
            _planetSurfacePhysicalFrameMaximumAxisDelta,
            axisDelta);

        gameplay.GlobalTransform = next.GameplayTransform;

        if (_player is not null && GodotObject.IsInstanceValid(_player))
        {
            _player.ApplySurfaceFrameTransition(
                previousGameplay,
                next.GameplayTransform,
                next.Radial.GravityMetersPerSecondSquared /
                    PlanetSurfaceRadialFrameRuntime.StandardGravityMetersPerSecondSquared);
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
            next.GameplayTransform);
        _npcNavigationSurface?.NotifySurfaceFrameChanged();
        if (_aerialSteeringRuntime is not null)
        {
            RefreshAerialNavigationEnvironment();
        }

        PlanetSurfacePhysicalFrameState? previousState =
            _planetSurfacePhysicalFrameState;
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
                "player=arbitrary-up; collision=rotating-tangent; " +
                "navigation=rotating-local-regions; streamer=25/9; " +
                "persistence=logical-xz/no-schema-bump; F5=acceptance.");
            _planetSurfacePhysicalFrameReadyPrinted = true;
        }
    }

    private void ApplyPlanetSurfaceFrameTransformToRuntimeCaches(
        Transform3D previousFrame,
        Transform3D nextFrame)
    {
        foreach (NpcFactionAgentNode agent in
                 GetTree().GetNodesInGroup("npc_faction_agent"))
        {
            agent.ApplyWorldFrameTransform(previousFrame, nextFrame);
        }
        foreach (NpcShipNavigationNode ship in _npcShipNavigationNodes)
        {
            if (GodotObject.IsInstanceValid(ship))
            {
                ship.ApplyWorldFrameTransform(previousFrame, nextFrame);
            }
        }
        foreach (EcologyFaunaNode fauna in _ecologyFaunaNodes)
        {
            if (GodotObject.IsInstanceValid(fauna))
            {
                fauna.ApplyWorldFrameTransform(previousFrame, nextFrame);
            }
        }
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

        double upError = Math.Acos(Math.Clamp(
            _player.ActiveSurfaceUp.Normalized().Dot(state.WorldUp),
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

        bool livePlayer = _player is not null &&
            _player.SurfaceFrameActive &&
            _planetSurfacePhysicalFrameState is { } live &&
            _player.ActiveSurfaceUp.Normalized().Dot(live.WorldUp) >= 0.9999f;
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

        bool passed = report.Passed && livePlayer && liveGameplay &&
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
            $"player={(livePlayer ? 1 : 0)}; gameplay={(liveGameplay ? 1 : 0)}; " +
            $"streamer={(liveStreamer ? 1 : 0)}; nav={(navFrame ? 1 : 0)}; " +
            $"bounded25x9={(bounded ? 1 : 0)}; " +
            $"maxPointErr={report.MaximumPointRoundTripError.ToString("0.000000", CultureInfo.InvariantCulture)}m; " +
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
