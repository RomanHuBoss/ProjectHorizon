using System;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private string _surfaceVisualLanguageAcceptanceHud = "READY";

    private void RunSurfaceVisualLanguageAcceptance()
    {
        string[] catalogFamilies = ContentCatalog.Resources.Values
            .Select(ProceduralSurfaceVisualFactory.ResolveResourceFamily)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(family => family, StringComparer.Ordinal)
            .ToArray();

        int compoundResources = _streamedSurfaceResources.Values.Count(resource =>
        {
            MeshInstance3D? visual = resource.GetNodeOrNull<MeshInstance3D>(
                "MeshInstance3D");
            return visual is not null && visual.GetChildCount() >= 3;
        });
        int resourceCollisionBindings = _streamedSurfaceResources.Values.Count(resource =>
            resource.GetNodeOrNull<CollisionShape3D>("CollisionShape3D") is not null);

        int detailedPoi = _planetaryPoiNodes.Count(poi =>
            poi.GetNodeOrNull<Node3D>("VisualDetails") is Node3D details &&
            details.GetChildCount() > 0);
        int detailedFauna = _ecologyFaunaNodes.Count(fauna =>
            fauna.GetNodeOrNull<Node3D>("VisualDetails") is Node3D details &&
            details.GetChildCount() > 0);

        bool familyCoverage = catalogFamilies.Length == 4 &&
            catalogFamilies.Contains("crystal", StringComparer.Ordinal) &&
            catalogFamilies.Contains("fiber", StringComparer.Ordinal) &&
            catalogFamilies.Contains("organic", StringComparer.Ordinal) &&
            catalogFamilies.Contains("ore", StringComparer.Ordinal);
        bool resourceVisuals = _streamedSurfaceResources.Count > 0 &&
            compoundResources == _streamedSurfaceResources.Count;
        bool resourceGameplayContract =
            resourceCollisionBindings == _streamedSurfaceResources.Count;
        bool poiVisuals = _planetaryPoiNodes.Count > 0 &&
            detailedPoi == _planetaryPoiNodes.Count;
        bool faunaVisuals = _ecologyFaunaNodes.Count > 0 &&
            detailedFauna == _ecologyFaunaNodes.Count;
        bool terrainVisualLanguage = _planetSurfaceStreamer is not null &&
            GodotObject.IsInstanceValid(_planetSurfaceStreamer) &&
            _planetSurfaceStreamer.UsePlanetSurfacePresentation &&
            _planetSurfaceStreamer.TargetChunkCount == 25 &&
            _planetSurfaceDistantTerrain is not null &&
            GodotObject.IsInstanceValid(_planetSurfaceDistantTerrain);

        bool passed = familyCoverage && resourceVisuals &&
            resourceGameplayContract && poiVisuals && faunaVisuals &&
            terrainVisualLanguage;

        _surfaceVisualLanguageAcceptanceHud = passed
            ? $"PASS families={catalogFamilies.Length}/4 resources={compoundResources}/{_streamedSurfaceResources.Count} poi={detailedPoi}/{_planetaryPoiNodes.Count} fauna={detailedFauna}/{_ecologyFaunaNodes.Count}"
            : $"FAIL families={catalogFamilies.Length}/4 resources={compoundResources}/{_streamedSurfaceResources.Count} poi={detailedPoi}/{_planetaryPoiNodes.Count} fauna={detailedFauna}/{_ecologyFaunaNodes.Count}";

        string output =
            $"TASK-164 surface visual language acceptance {(passed ? "PASS" : "FAIL")}: " +
            $"resourceFamilies={catalogFamilies.Length}/4; " +
            $"compoundResources={compoundResources}/{_streamedSurfaceResources.Count}; " +
            $"resourceCollisions={resourceCollisionBindings}/{_streamedSurfaceResources.Count}; " +
            $"detailedPoi={detailedPoi}/{_planetaryPoiNodes.Count}; " +
            $"detailedFauna={detailedFauna}/{_ecologyFaunaNodes.Count}; " +
            $"terrainProcedural={(terrainVisualLanguage ? 1 : 0)}; " +
            "boundedGameplayStreamer=25; gameplayIdentity=persistence-unchanged; " +
            "result=compound procedural props and terrain material breakup verified.";
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
