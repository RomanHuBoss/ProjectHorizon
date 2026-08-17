using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private string _vegetationRegionalAcceptanceHud = "READY";
    private bool? _vegetationRegionalAcceptancePassed;
    private bool _vegetationRegionalReadyPrinted;

    private void PrintRegionalVegetationReady()
    {
        if (_vegetationRegionalReadyPrinted)
        {
            return;
        }
        _vegetationRegionalReadyPrinted = true;
        GD.Print(
            "TASK-196 regional vegetation READY: " +
            "partition=region+species; region=32m; lod=LOD0/LOD1/cull; " +
            "smallObjectCull=52m; residency=TASK-194-full/simplified/preload; " +
            "promotion=proximity+scan+damage+harvest+quest; demotion=distance+quest-pin; " +
            "persistence=TASK-116-seed+deltas; F5=acceptance.");
    }

    private void BuildRegionalVegetationMultiMeshes()
    {
        if (_ecologyRoot is null || _ecologyRuntime is null ||
            _ecologyPlan is null || _ecologyCatalog is null)
        {
            return;
        }
        IReadOnlyList<VegetationRegionBatch> batches =
            VegetationRegionRuntime.BuildRegionalBatches(
                EcologyPlan.Flora,
                EcologyCatalog,
                Ecology.IsFloraRemoved);
        foreach (VegetationRegionBatch batch in batches)
        {
            EcologyFloraDefinition definition = EcologyCatalog.GetFlora(batch.FloraId);
            StandardMaterial3D material = new()
            {
                AlbedoColor = new Color(
                    (float)definition.ColorR,
                    (float)definition.ColorG,
                    (float)definition.ColorB,
                    1.0f),
                Roughness = 0.90f
            };
            MultiMeshInstance3D lod0 = CreateVegetationMultiMeshNode(
                batch,
                definition,
                material,
                lod: 0);
            MultiMeshInstance3D lod1 = CreateVegetationMultiMeshNode(
                batch,
                definition,
                material,
                lod: 1);
            lod1.Visible = false;
            _ecologyRoot.AddChild(lod0);
            _ecologyRoot.AddChild(lod1);
            _ecologyFloraGroups.Add(new EcologyMultiMeshGroup(
                lod0,
                lod1,
                batch.Placements,
                batch.Region,
                batch.SmallObject));
        }
        UpdateRegionalVegetationVisibility();
        PrintRegionalVegetationReady();
    }

    private MultiMeshInstance3D CreateVegetationMultiMeshNode(
        VegetationRegionBatch batch,
        EcologyFloraDefinition definition,
        StandardMaterial3D material,
        int lod)
    {
        MultiMesh multiMesh = new()
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = EcologyFloraSpecimenNode.CreateLodMesh(definition, material, lod),
            InstanceCount = batch.Placements.Count
        };
        for (int index = 0; index < batch.Placements.Count; index++)
        {
            EcologyFloraPlacement placement = batch.Placements[index];
            float angle = Mathf.DegToRad((float)placement.RotationDegrees);
            float scale = (float)placement.Scale;
            Basis basis = Basis.Identity.Rotated(Vector3.Up, angle).Scaled(
                Vector3.One * scale);
            multiMesh.SetInstanceTransform(
                index,
                new Transform3D(
                    basis,
                    new Vector3(
                        (float)placement.PositionX,
                        (float)FloraSurfaceY(placement),
                        (float)placement.PositionZ)));
        }
        return new MultiMeshInstance3D
        {
            Name = $"Vegetation_R{batch.Region.X}_{batch.Region.Z}_{GetShortContentId(batch.FloraId)}_LOD{lod}",
            Multimesh = multiMesh
        };
    }

    private void ClearRegionalVegetationMultiMeshes()
    {
        foreach (EcologyMultiMeshGroup group in _ecologyFloraGroups)
        {
            foreach (MultiMeshInstance3D node in new[] { group.Lod0Node, group.Lod1Node })
            {
                if (!GodotObject.IsInstanceValid(node))
                {
                    continue;
                }
                if (node.GetParent() is Node parent)
                {
                    parent.RemoveChild(node);
                }
                node.QueueFree();
            }
        }
        _ecologyFloraGroups.Clear();
    }

    private void UpdateRegionalVegetationVisibility()
    {
        if (_ecologyFloraGroups.Count == 0)
        {
            return;
        }
        if (!_surfaceRuntimeActive)
        {
            foreach (EcologyMultiMeshGroup group in _ecologyFloraGroups)
            {
                if (GodotObject.IsInstanceValid(group.Lod0Node))
                {
                    group.Lod0Node.Visible = false;
                }
                if (GodotObject.IsInstanceValid(group.Lod1Node))
                {
                    group.Lod1Node.Visible = false;
                }
            }
            return;
        }
        Node3D? observerNode = StageOneVoyage.Piloted && _voyageShip is not null
            ? _voyageShip
            : _player;
        if (observerNode is null || !GodotObject.IsInstanceValid(observerNode))
        {
            return;
        }
        Vector3 logicalObserver = IsPlayerInsidePlanetaryCave &&
            _planetaryCaveReturnLogical is { } outside
            ? new Vector3((float)outside.EastMeters, (float)outside.HeightMeters, (float)outside.NorthMeters)
            : WorldToPlanetSurfaceLogicalPosition(observerNode.GlobalPosition);
        foreach (EcologyMultiMeshGroup group in _ecologyFloraGroups)
        {
            (double east, double north) = VegetationRegionRuntime.RegionCenter(group.Region);
            double dx = east - logicalObserver.X;
            double dz = north - logicalObserver.Z;
            double distance = Math.Sqrt((dx * dx) + (dz * dz));
            double qualityScale = Math.Clamp(PerformanceVegetationDistanceScale, 0.45, 1.25);
            if (!ShouldRenderVegetationBatchForGraphics(group, distance))
            {
                if (GodotObject.IsInstanceValid(group.Lod0Node))
                {
                    group.Lod0Node.Visible = false;
                }
                if (GodotObject.IsInstanceValid(group.Lod1Node))
                {
                    group.Lod1Node.Visible = false;
                }
                continue;
            }
            distance /= qualityScale;
            WorldStreamingRegionDetail? residency =
                _worldStreamingCoordinator?.GetDetailAt(east, north) ??
                WorldStreamingRegionDetail.Full;
            VegetationLodTier tier = VegetationRegionRuntime.ResolveLod(
                distance,
                group.SmallObject,
                residency);
            if (GodotObject.IsInstanceValid(group.Lod0Node))
            {
                group.Lod0Node.Visible = tier == VegetationLodTier.Near;
            }
            if (GodotObject.IsInstanceValid(group.Lod1Node))
            {
                group.Lod1Node.Visible = tier == VegetationLodTier.Mid;
            }
        }
    }

    private bool TryFindFloraPlacement(
        string instanceId,
        out EcologyFloraPlacement? placement)
    {
        placement = _ecologyPlan?.Flora.FirstOrDefault(item =>
            string.Equals(item.InstanceId, instanceId, StringComparison.Ordinal));
        return placement is not null;
    }

    private bool IsFloraQuestRelevant(EcologyFloraPlacement placement)
    {
        if (_proceduralQuestRuntime is null || _ecologyCatalog is null)
        {
            return false;
        }
        EcologyFloraDefinition definition = EcologyCatalog.GetFlora(placement.FloraId);
        return ProceduralQuests.Views.Any(view =>
            (view.Status is ProceduralQuestStatus.Accepted or
                ProceduralQuestStatus.ReturnRequired or
                ProceduralQuestStatus.ReadyToClaim) &&
            ((view.Definition.ObjectiveType == ProceduralQuestObjectiveType.ScanSpecies &&
              string.Equals(view.Definition.TargetDefinitionId, placement.FloraId, StringComparison.Ordinal)) ||
             (view.Definition.ObjectiveType == ProceduralQuestObjectiveType.CollectResource &&
              string.Equals(view.Definition.TargetDefinitionId, definition.HarvestDefinitionId, StringComparison.Ordinal))));
    }

    private void EnsureFloraPromoted(
        EcologyFloraPlacement placement,
        VegetationPromotionReason reason)
    {
        if (_ecologyRoot is null || _ecologyRuntime is null ||
            Ecology.IsFloraRemoved(placement.InstanceId))
        {
            return;
        }
        RecordVegetationPromotionReason(placement.InstanceId, reason);
        if (_promotedFloraNodes.ContainsKey(placement.InstanceId))
        {
            return;
        }
        EcologyFloraSpecimenNode specimen = new();
        EcologyFloraPlacement terrainPlacement = placement with
        {
            PositionY = SamplePlanetSurfacePhysicalHeight(
                placement.PositionX,
                placement.PositionZ)
        };
        specimen.Configure(
            EcologyCatalog.GetFlora(placement.FloraId),
            terrainPlacement,
            renderMesh: false);
        specimen.HarvestRequested += OnEcologyFloraHarvestRequested;
        specimen.Damaged += OnEcologyFloraDamaged;
        _ecologyRoot.AddChild(specimen);
        _promotedFloraNodes[placement.InstanceId] = specimen;
    }

    private void RecordVegetationPromotionReason(
        string instanceId,
        VegetationPromotionReason reason)
    {
        if (!_vegetationPromotionReasons.TryGetValue(instanceId, out HashSet<VegetationPromotionReason>? reasons))
        {
            reasons = new HashSet<VegetationPromotionReason>();
            _vegetationPromotionReasons[instanceId] = reasons;
        }
        reasons.Add(reason);
    }

    private void OnEcologyFloraDamaged(EcologyFloraSpecimenNode node, Node3D source)
    {
        RecordVegetationPromotionReason(node.InstanceId, VegetationPromotionReason.Damage);
        GD.Print(
            "TASK-196 vegetation promotion PASS: " +
            $"instance={node.InstanceId}; reason=damage; source={source.Name}; fullEntity=1.");
    }

    private VegetationRegionalDiagnostics CaptureVegetationRegionalDiagnostics()
    {
        int near = _ecologyFloraGroups.Count(group =>
            GodotObject.IsInstanceValid(group.Lod0Node) && group.Lod0Node.Visible);
        int mid = _ecologyFloraGroups.Count(group =>
            GodotObject.IsInstanceValid(group.Lod1Node) && group.Lod1Node.Visible);
        int culled = Math.Max(0, _ecologyFloraGroups.Count - near - mid);
        bool partitioned = _ecologyFloraGroups
            .GroupBy(group => new { group.Region, FloraId = group.Placements.FirstOrDefault()?.FloraId ?? string.Empty })
            .All(group => group.Count() == 1);
        return new VegetationRegionalDiagnostics(
            _ecologyFloraGroups.Count,
            _ecologyFloraGroups.Select(group => group.Region).Distinct().Count(),
            _ecologyFloraGroups.Count(group => GodotObject.IsInstanceValid(group.Lod0Node)),
            _ecologyFloraGroups.Count(group => GodotObject.IsInstanceValid(group.Lod1Node)),
            near,
            mid,
            culled,
            _promotedFloraNodes.Count,
            _worldStreamingCoordinator is not null && GodotObject.IsInstanceValid(_worldStreamingCoordinator),
            partitioned);
    }

    private void RunVegetationRegionalAcceptance()
    {
        if (_ecologyCatalog is null || _ecologyPlan is null)
        {
            _vegetationRegionalAcceptancePassed = false;
            _vegetationRegionalAcceptanceHud = "FAIL ecology unavailable";
            GD.PushError("TASK-196 regional vegetation acceptance FAIL: ecology runtime unavailable.");
            return;
        }
        VegetationRegionalAcceptanceReport report =
            VegetationRegionalAcceptanceRunner.Evaluate(
                EcologyCatalog,
                EcologyPlan,
                CaptureVegetationRegionalDiagnostics());
        _vegetationRegionalAcceptancePassed = report.Passed;
        _vegetationRegionalAcceptanceHud = report.Passed
            ? $"PASS regions={report.Regions} groups={report.RegionalGroups} lod=2 promo=5/5"
            : "FAIL regional vegetation contract";
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
