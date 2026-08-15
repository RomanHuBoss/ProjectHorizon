using System;
using Godot;

public sealed record RendererProfileSnapshot(
    string RenderingMethod,
    string RenderingDriver,
    bool CompatibilityExportFeature,
    bool IsPrimaryRenderer,
    bool IsCompatibilityRenderer,
    bool IsValidForProfile);

/// <summary>
/// Captures the actual renderer/driver selected by Godot instead of trusting static project settings.
/// </summary>
public static class RendererProfileDiagnostics
{
    public static RendererProfileSnapshot Capture()
    {
        string method = RenderingServer.GetCurrentRenderingMethod();
        string driver = RenderingServer.GetCurrentRenderingDriverName();
        bool compatibilityFeature = OS.HasFeature("compatibility");
        bool compatibility = string.Equals(
            method,
            "gl_compatibility",
            StringComparison.OrdinalIgnoreCase);
        bool primary = string.Equals(
            method,
            "mobile",
            StringComparison.OrdinalIgnoreCase);
        bool openGl = driver.StartsWith("opengl3", StringComparison.OrdinalIgnoreCase);
        bool renderingDevice =
            string.Equals(driver, "vulkan", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(driver, "d3d12", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(driver, "metal", StringComparison.OrdinalIgnoreCase);

        // A primary export may legitimately enter Godot's OpenGL fallback on unsupported
        // RenderingDevice hardware. A dedicated Compatibility export must never resolve back
        // to Mobile/RenderingDevice.
        bool valid = compatibilityFeature
            ? compatibility && openGl
            : (primary && renderingDevice) || (compatibility && openGl);

        return new RendererProfileSnapshot(
            method,
            driver,
            compatibilityFeature,
            primary,
            compatibility,
            valid);
    }

    public static string BuildEvidence(RendererProfileSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        string expected = snapshot.CompatibilityExportFeature
            ? "compatibility"
            : "primary-or-engine-fallback";
        return
            $"TASK-144 renderer profile {(snapshot.IsValidForProfile ? "PASS" : "FAIL")}: " +
            $"feature={(snapshot.CompatibilityExportFeature ? "compatibility" : "primary")}; " +
            $"method={snapshot.RenderingMethod}; driver={snapshot.RenderingDriver}; " +
            $"primary={(snapshot.IsPrimaryRenderer ? 1 : 0)}; " +
            $"compatibility={(snapshot.IsCompatibilityRenderer ? 1 : 0)}; expected={expected}.";
    }
}
