using System;
using System.Linq;
using Godot;

public static class DeveloperToolContext
{
    public const string WorkbenchScene = "res://Scenes/Developer/DeveloperWorkbench.tscn";
    public const string GameplayScene = "res://Scenes/VerticalSlice/SalvageRepairSlice.tscn";
    public const string PlanetPreviewScene = "res://Scenes/Planet/CubeSpherePrototype.tscn";
    public const string ChunkProfilerScene = "res://Scenes/Terrain/TerrainChunkPrototype.tscn";

    public static bool ReturnToWorkbenchOnF6 { get; set; }
    public static bool OpenConsoleOnGameplay { get; set; }
    public static long PreviewUniverseSeed { get; set; } = GalaxyNavigationRuntime.DefaultUniverseSeed;
    public static long PreviewPlanetSeed { get; set; } = 20260801;
    public static string PreviewPlanetId { get; set; } = GalaxyNavigationRuntime.StarterSystemId + ".p1";
    public static int PreviewLod { get; set; } = 1;
    public static bool PreviewChunkGrid { get; set; } = true;
    public static bool PreviewBiomes { get; set; } = true;
    public static bool PreviewHeight { get; set; } = true;
    public static bool PreviewResourceDensity { get; set; } = true;

    public static bool IsDeveloperModeAllowed()
    {
        if (OS.IsDebugBuild()) return true;
        return OS.GetCmdlineUserArgs().Any(argument =>
            string.Equals(argument, "--developer", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(argument, "developer", StringComparison.OrdinalIgnoreCase));
    }

    public static Error ReturnToWorkbench(SceneTree tree)
    {
        ReturnToWorkbenchOnF6 = false;
        OpenConsoleOnGameplay = false;
        return tree.ChangeSceneToFile(WorkbenchScene);
    }
}
