using Godot;

public partial class SalvageRepairSlice
{
    private string _hardSurfaceVisualRedesignAcceptanceHud = "READY";
    private bool? _hardSurfaceVisualRedesignAcceptancePassed;

    private void PrintHardSurfaceVisualRedesignReady()
    {
        GD.Print(
            "TASK-186 hard-surface visual redesign READY: " +
            "player=lofted-explorer; npc=arrowhead-interceptor; station=segmented-industrial-ring; " +
            "primarySphere=0; primaryTorus=0; collision=unchanged; lod=preserved; F5=acceptance; manualVisual=required.");
    }

    private void RunHardSurfaceVisualRedesignAcceptance()
    {
        _hardSurfaceVisualRedesignAcceptanceHud = "RUNNING";
        _hardSurfaceVisualRedesignAcceptancePassed = null;

        Node3D? playerModel = _voyageShip?.GetNodeOrNull<Node3D>("Visuals/ProductionExterior");
        Node3D? stationModel = GetNodeOrNull<Node3D>("Gameplay/OrbitalStation/ProductionModel");
        Node3D? npcModel = _npcShipNavigationNodes.Count > 0
            ? _npcShipNavigationNodes[0].GetNodeOrNull<Node3D>("ProductionModel")
            : null;

        Node? playerLod0 = playerModel?.GetNodeOrNull<Node>("LOD0");
        Node? stationLod0 = stationModel?.GetNodeOrNull<Node>("LOD0");
        Node? npcLod0 = npcModel?.GetNodeOrNull<Node>("LOD0");

        bool playerSignature =
            HasDescendantNamed(playerLod0, "PrimaryHull") &&
            HasDescendantNamed(playerLod0, "WingPort") &&
            HasDescendantNamed(playerLod0, "WingStarboard") &&
            HasDescendantNamed(playerLod0, "EngineNacellePort") &&
            HasDescendantNamed(playerLod0, "EngineNacelleStarboard");
        bool npcSignature =
            HasDescendantNamed(npcLod0, "PrimaryHull") &&
            HasDescendantNamed(npcLod0, "BladeWingPort") &&
            HasDescendantNamed(npcLod0, "BladeWingStarboard") &&
            HasDescendantNamed(npcLod0, "EngineNacellePort");
        bool stationSignature =
            HasDescendantNamed(stationLod0, "RingModule_00") &&
            HasDescendantNamed(stationLod0, "RingTruss_00") &&
            HasDescendantNamed(stationLod0, "UtilityPylon_00") &&
            HasDescendantNamed(stationLod0, "DockingCollar") &&
            HasDescendantNamed(stationLod0, "DockingTunnel");

        bool fallbackHidden =
            _voyageShip?.GetNodeOrNull<MeshInstance3D>("Visuals/Hull")?.Visible == false &&
            GetNodeOrNull<MeshInstance3D>("Gameplay/OrbitalStation/MeshInstance3D")?.Visible == false &&
            _npcShipNavigationNodes.TrueForAll(ship =>
                ship.GetNodeOrNull<Node3D>("LegacyProceduralFallback")?.Visible == false);

        bool collisionSeparated =
            !ContainsCollisionShape(playerModel) &&
            !ContainsCollisionShape(stationModel) &&
            !ContainsCollisionShape(npcModel);

        bool lodReady =
            playerModel is ProductionModelLodController &&
            stationModel is ProductionModelLodController &&
            npcModel is ProductionModelLodController;

        HardSurfaceVisualRedesignAcceptanceReport report =
            HardSurfaceVisualRedesignAcceptanceRunner.Evaluate(
                CountMeshInstances(playerLod0),
                CountMeshInstances(npcLod0),
                CountMeshInstances(stationLod0),
                playerSignature,
                npcSignature,
                stationSignature,
                fallbackHidden,
                collisionSeparated,
                lodReady);

        _hardSurfaceVisualRedesignAcceptancePassed = report.Passed;
        _hardSurfaceVisualRedesignAcceptanceHud = report.Passed
            ? $"PASS parts={report.PlayerMeshParts}/{report.NpcMeshParts}/{report.StationMeshParts}"
            : $"FAIL {report.Result}";

        if (report.Passed)
        {
            GD.Print(report.BuildOutputLine());
        }
        else
        {
            GD.PushError(report.BuildOutputLine());
        }
    }

    private static int CountMeshInstances(Node? root)
    {
        if (root is null)
        {
            return 0;
        }
        int count = root is MeshInstance3D ? 1 : 0;
        foreach (Node child in root.GetChildren())
        {
            count += CountMeshInstances(child);
        }
        return count;
    }

    private static bool HasDescendantNamed(Node? root, string name)
    {
        if (root is null)
        {
            return false;
        }
        if (root.Name.ToString() == name)
        {
            return true;
        }
        foreach (Node child in root.GetChildren())
        {
            if (HasDescendantNamed(child, name))
            {
                return true;
            }
        }
        return false;
    }
}
