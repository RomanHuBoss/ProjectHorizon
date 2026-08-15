using Godot;

public sealed record NpcNavigationAcceptanceReport(
    bool Passed,
    int Regions,
    int MaximumRegions,
    int WalkableCells,
    int StaticObstacles,
    int AvoidanceObstacles,
    int TilesTouched,
    int PathPoints,
    int NavigationAgents,
    int PathRequests,
    int AvoidanceSamples,
    int Recoveries,
    int EvictedRegions,
    bool LocalTileBudget,
    bool CrossTilePath,
    bool ObstacleClearance,
    bool BoundedStreaming,
    bool NavigationAgentRuntime,
    bool AvoidanceRuntime,
    bool RecoveryProbe,
    bool ServerSynchronized,
    string Result);

public enum NpcNavigationAcceptancePhase
{
    Idle = 0,
    WaitInitialSync = 1,
    WaitShiftSync = 2,
    WaitRestoreSync = 3,
    ObserveAgents = 4,
    Completed = 5
}
