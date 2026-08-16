using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private readonly Dictionary<string, PlanetaryCavePrefabNode> _planetaryCavePrefabs =
        new(StringComparer.Ordinal);
    private readonly List<SalvageResourceNode> _planetaryCaveResourceNodes = new();
    private string _activePlanetaryCaveId = string.Empty;
    private PlanetSurfaceLogicalPosition? _planetaryCaveReturnLogical;
    private string _planetaryCaveAcceptanceHud = "READY";
    private bool? _planetaryCaveAcceptancePassed;

    private bool IsPlayerInsidePlanetaryCave =>
        !string.IsNullOrWhiteSpace(_activePlanetaryCaveId);

    private void PrintPlanetaryCaveReady()
    {
        GD.Print(
            "TASK-192 planetary caves READY: " +
            "mode=prefab-only; archetypes=3; globalProcedural=0; terrainDeformation=0; " +
            "entry=poi.cave_entrance; interior=isolated-subsurface-pocket; " +
            "resources=object-deposits+persistent-node-ids; exit=interactive; F5=acceptance.");
    }

    private void RebuildPlanetaryCavePrefabs()
    {
        // RebuildPlanetaryPoiScene owns the old POI-root children and has already
        // queued them for deletion. Clear references here instead of queueing the
        // same cave nodes a second time.
        _planetaryCavePrefabs.Clear();
        _planetaryCaveResourceNodes.Clear();
        _activePlanetaryCaveId = string.Empty;
        _planetaryCaveReturnLogical = null;

        if (_planetaryPoisRoot is null ||
            _planetSurfaceContentProfile is null ||
            _planetaryPoiCatalog is null)
        {
            return;
        }

        foreach (PlanetaryPoiNode entrance in _planetaryPoiNodes.Where(node =>
                     string.Equals(
                         node.PoiTypeId,
                         "poi.cave_entrance",
                         StringComparison.Ordinal)))
        {
            PlanetaryCavePlan plan = PlanetaryCaveRuntime.BuildPlan(
                PlanetSurfaceContentProfile.PlanetId,
                entrance.InstanceId,
                PlanetaryPoiCatalog.WorldSeed);
            PlanetaryCavePrefabNode cave = new()
            {
                Position = entrance.Position,
                Rotation = entrance.Rotation
            };
            cave.Configure(plan, ContentCatalog.Resources);
            _planetaryPoisRoot.AddChild(cave);
            _planetaryCavePrefabs[plan.CaveInstanceId] = cave;
            foreach (SalvageResourceNode deposit in cave.Deposits)
            {
                _planetaryCaveResourceNodes.Add(deposit);
                deposit.SetCollected(
                    Session.CollectedNodeIds.Contains(deposit.ResourceNodeId));
            }
        }

        if (_planetaryCavePrefabs.Count > 0)
        {
            PlanetaryCavePrefabNode first = _planetaryCavePrefabs.Values
                .OrderBy(cave => cave.Plan.CaveInstanceId, StringComparer.Ordinal)
                .First();
            GD.Print(
                "TASK-192 cave prefab binding PASS: " +
                $"planet={first.Plan.PlanetId}; cave={first.Plan.CaveInstanceId}; " +
                $"archetype={first.Plan.Archetype.ArchetypeId}; deposits={first.Deposits.Count}; " +
                $"collisions={first.CollisionShapeCount}; terrainDeformation=0; globalProcedural=0.");
        }
    }

    private bool TryEnterPlanetaryCave(
        PlanetaryPoiNode entrance,
        Node3D interactor)
    {
        if (_player is null ||
            !ReferenceEquals(interactor, _player) ||
            StageOneVoyage.Piloted ||
            !_surfaceRuntimeActive)
        {
            _status = L("ui.cave.entry_unavailable");
            return false;
        }

        PlanetaryCavePrefabNode? cave = _planetaryCavePrefabs.Values
            .FirstOrDefault(candidate => string.Equals(
                candidate.Plan.EntrancePoiInstanceId,
                entrance.InstanceId,
                StringComparison.Ordinal));
        if (cave is null)
        {
            _status = L("ui.cave.prefab_missing");
            return false;
        }

        foreach (PlanetaryCavePrefabNode candidate in _planetaryCavePrefabs.Values)
        {
            candidate.SetRuntimeActive(ReferenceEquals(candidate, cave));
        }
        _planetaryCaveReturnLogical = GetPlanetSurfaceLogicalPlayerPosition();
        _activePlanetaryCaveId = cave.Plan.CaveInstanceId;
        _player.GlobalPosition = cave.EntryWorldPosition;
        _player.Velocity = Vector3.Zero;
        SetPlanetaryWaterState(false, false, default, "none");
        _status = LF(
            "ui.cave.entered",
            ("cave", L(cave.Plan.Archetype.LocalizationKey)));
        _lastDomainEvent = $"CaveEntered({cave.Plan.CaveInstanceId})";
        GD.Print(
            "TASK-192 cave entry PASS: " +
            $"cave={cave.Plan.CaveInstanceId}; archetype={cave.Plan.Archetype.ArchetypeId}; " +
            $"depth={cave.Plan.Archetype.InteriorDepthMeters.ToString("0.0", CultureInfo.InvariantCulture)}m; " +
            $"deposits={cave.Deposits.Count}; interactor={interactor.Name}.");
        return true;
    }

    public bool TryExitPlanetaryCave(
        string caveInstanceId,
        Node3D interactor)
    {
        if (_player is null ||
            !ReferenceEquals(interactor, _player) ||
            !string.Equals(
                _activePlanetaryCaveId,
                caveInstanceId,
                StringComparison.Ordinal) ||
            !_planetaryCavePrefabs.TryGetValue(
                caveInstanceId,
                out PlanetaryCavePrefabNode? cave))
        {
            return false;
        }

        PlanetSurfaceLogicalPosition destination = _planetaryCaveReturnLogical ??
            GetFallbackCaveReturnLogical(cave);
        cave.SetRuntimeActive(false);
        _activePlanetaryCaveId = string.Empty;
        _planetaryCaveReturnLogical = null;
        _player.GlobalPosition = SurfaceLogicalToLocalPosition(
            destination.EastMeters,
            Math.Max(
                destination.HeightMeters,
                SamplePlanetSurfaceHeight(
                    destination.EastMeters,
                    destination.NorthMeters) + 1.05),
            destination.NorthMeters);
        _player.Velocity = Vector3.Zero;
        _status = L("ui.cave.exited");
        _lastDomainEvent = $"CaveExited({caveInstanceId})";
        GD.Print(
            "TASK-192 cave exit PASS: " +
            $"cave={caveInstanceId}; depositsCollected={cave.Deposits.Count(node => node.IsCollected)}; " +
            "terrainDeformation=0.");
        return true;
    }

    private PlanetSurfaceLogicalPosition GetFallbackCaveReturnLogical(
        PlanetaryCavePrefabNode cave)
    {
        PlanetaryPoiNode? entrance = _planetaryPoiNodes.FirstOrDefault(node =>
            string.Equals(
                node.InstanceId,
                cave.Plan.EntrancePoiInstanceId,
                StringComparison.Ordinal));
        if (entrance is null)
        {
            return new PlanetSurfaceLogicalPosition(0.0, 1.05, 5.5);
        }
        Vector3 logical = WorldToPlanetSurfaceLogicalPosition(
            entrance.GlobalPosition);
        return new PlanetSurfaceLogicalPosition(
            logical.X,
            SamplePlanetSurfaceHeight(logical.X, logical.Z) + 1.05,
            logical.Z);
    }

    private PlanetSurfaceLogicalPosition GetSnapshotLogicalPlayerPosition()
    {
        return IsPlayerInsidePlanetaryCave && _planetaryCaveReturnLogical is { } outside
            ? outside
            : GetPlanetSurfaceLogicalPlayerPosition();
    }

    private void UpdatePlanetaryCaveRuntime(double delta)
    {
        _ = delta;
        if (!IsPlayerInsidePlanetaryCave)
        {
            return;
        }
        if (!_surfaceRuntimeActive || StageOneVoyage.Piloted || _player is null)
        {
            if (_planetaryCavePrefabs.TryGetValue(
                _activePlanetaryCaveId,
                out PlanetaryCavePrefabNode? cave))
            {
                cave.SetRuntimeActive(false);
            }
            _activePlanetaryCaveId = string.Empty;
            _planetaryCaveReturnLogical = null;
            return;
        }

        // A cave is an isolated authored prefab below the terrain. Surface water
        // must not interpret its negative local Y as ocean submersion.
        SetPlanetaryWaterState(false, false, default, "none");
    }

    private void ResetPlanetaryCaveTransientState()
    {
        foreach (PlanetaryCavePrefabNode cave in _planetaryCavePrefabs.Values)
        {
            cave.SetRuntimeActive(false);
        }
        _activePlanetaryCaveId = string.Empty;
        _planetaryCaveReturnLogical = null;
    }

    private void ApplyPlanetaryCaveSessionState()
    {
        foreach (SalvageResourceNode deposit in _planetaryCaveResourceNodes)
        {
            deposit.SetCollected(
                Session.CollectedNodeIds.Contains(deposit.ResourceNodeId));
        }
    }

    private void RunPlanetaryCaveAcceptance()
    {
        PlanetaryCavePrefabNode? cave = _planetaryCavePrefabs.Values
            .OrderBy(candidate => candidate.Plan.CaveInstanceId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (cave is null)
        {
            _planetaryCaveAcceptancePassed = false;
            _planetaryCaveAcceptanceHud = "FAIL prefab missing";
            GD.PushError(
                "TASK-192 planetary cave prefab acceptance FAIL: no live cave prefab.");
            return;
        }

        PlanetaryCaveAcceptanceReport report =
            PlanetaryCaveAcceptanceRunner.Evaluate(
                cave.Plan,
                ContentCatalog.Resources,
                cave.CollisionShapeCount,
                cave.EntryExitReady,
                GodotObject.IsInstanceValid(cave));
        _planetaryCaveAcceptancePassed = report.Passed;
        _planetaryCaveAcceptanceHud = report.Passed
            ? $"PASS prefab=1 arch={report.ArchetypeCount} deposits={report.DepositCount} deform=0"
            : "FAIL cave prefab contract";
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
