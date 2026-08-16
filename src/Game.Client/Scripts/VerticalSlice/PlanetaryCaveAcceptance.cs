using System;
using System.Collections.Generic;

public sealed record PlanetaryCaveAcceptanceReport(
    bool Passed,
    int ArchetypeCount,
    int DepositCount,
    int CollisionShapeCount,
    bool EntryExitReady,
    bool PersistenceReady,
    bool GlobalProceduralCavesDisabled,
    bool TerrainDeformationDisabled,
    bool LivePrefabReady)
{
    public string BuildOutputLine() =>
        $"TASK-192 planetary cave prefab acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"archetypes={ArchetypeCount}; deposits={DepositCount}; " +
        $"collisions={CollisionShapeCount}; entryExit={(EntryExitReady ? 1 : 0)}; " +
        $"persistence={(PersistenceReady ? 1 : 0)}; globalProcedural={(GlobalProceduralCavesDisabled ? 0 : 1)}; " +
        $"terrainDeformation={(TerrainDeformationDisabled ? 0 : 1)}; " +
        $"livePrefab={(LivePrefabReady ? 1 : 0)}; result=prefab-only-subsurface-caves.";
}

public static class PlanetaryCaveAcceptanceRunner
{
    public static PlanetaryCaveAcceptanceReport Evaluate(
        PlanetaryCavePlan plan,
        IReadOnlyDictionary<string, GameResourceDefinition> resources,
        int liveCollisionShapeCount,
        bool entryExitReady,
        bool livePrefabReady)
    {
        bool planValid = PlanetaryCaveRuntime.ValidatePlan(
            plan,
            resources,
            out _);
        bool persistentIds = plan.Deposits.Count == PlanetaryCaveRuntime.DepositsPerCave;
        bool passed =
            PlanetaryCaveRuntime.SupportedArchetypes.Count ==
                PlanetaryCaveRuntime.RequiredArchetypeCount &&
            planValid &&
            liveCollisionShapeCount >= 12 &&
            entryExitReady &&
            persistentIds &&
            !plan.GlobalProceduralCaveNetwork &&
            !plan.TerrainDeformationEnabled &&
            livePrefabReady;
        return new PlanetaryCaveAcceptanceReport(
            passed,
            PlanetaryCaveRuntime.SupportedArchetypes.Count,
            plan.Deposits.Count,
            liveCollisionShapeCount,
            entryExitReady,
            persistentIds,
            !plan.GlobalProceduralCaveNetwork,
            !plan.TerrainDeformationEnabled,
            livePrefabReady);
    }
}
