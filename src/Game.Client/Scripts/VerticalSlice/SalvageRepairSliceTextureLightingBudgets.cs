using System;
using System.Collections.Generic;
using Godot;

public partial class SalvageRepairSlice
{
    private const double TextureLightingResidencyUpdateSeconds = 0.25;

    private readonly Dictionary<Light3D, float> _task208OriginalLightEnergy = new();
    private readonly Dictionary<Light3D, bool> _task208OriginalShadowState = new();
    private double _task208LightingAccumulator;
    private int _task208LocalLightsFound;
    private int _task208LocalLightsActive;
    private int _task208ShadowedLocalLights;
    private string _textureLightingAcceptanceHud = "READY";
    private bool? _textureLightingAcceptancePassed;
    private bool _textureLightingReadyPrinted;

    private void InitializeTextureLightingBudgets()
    {
        ApplyTextureLightingResidency(force: true);
        if (_textureLightingReadyPrinted)
        {
            return;
        }
        _textureLightingReadyPrinted = true;
        GD.Print(
            "TASK-208 texture/material/lighting READY: " +
            "textures=player:2048/ship:2048/npc:2048/building:2048/object:1024/plant:1024/ui:512/tiled:2048; " +
            "materials=atlas+reusable+production<=5; " +
            "surface=one-star+ambient+local<=6+localShadow=0; " +
            "interior=static-baked-baseline-policy+dynamic<=8+shadow<=2; " +
            "cave=dynamic<=4+shadow=0; distant=light-distance-cull; " +
            "runtime=0.25s-residency; F5=acceptance.");
    }

    private void UpdateTextureLightingBudgets(double delta)
    {
        _task208LightingAccumulator += Math.Max(0.0, delta);
        if (_task208LightingAccumulator < TextureLightingResidencyUpdateSeconds)
        {
            return;
        }
        _task208LightingAccumulator = 0.0;
        ApplyTextureLightingResidency(force: false);
    }

    private void ApplyTextureLightingResidency(bool force)
    {
        if (_worldSceneCoordinatorRuntime is null)
        {
            return;
        }

        WorldSceneKind worldKind = WorldScenes.Current.Kind;
        LightingResidencySettings settings =
            TextureLightingBudgetPolicy.ResolveLighting(worldKind, IsPlayerInsidePlanetaryCave);
        Vector3 observer = ResolveTask208LightingObserver();
        List<Light3D> lights = new();
        CollectTask208LocalLights(this, lights);

        List<(Light3D Light, double Distance, int Bias)> candidates = new();
        foreach (Light3D light in lights)
        {
            if (!_task208OriginalLightEnergy.ContainsKey(light))
            {
                _task208OriginalLightEnergy[light] = light.LightEnergy;
                _task208OriginalShadowState[light] = light.ShadowEnabled;
            }

            bool parentVisible = light.GetParent() is not Node3D parent || parent.IsVisibleInTree();
            double distance = observer.DistanceTo(light.GlobalPosition);
            bool inRange = parentVisible && distance <= settings.MaximumLocalLightDistanceMeters;
            if (inRange)
            {
                candidates.Add((light, distance, Task208LightPriorityBias(light.Name.ToString())));
            }
            else
            {
                light.LightEnergy = 0.0f;
                light.ShadowEnabled = false;
            }
        }

        candidates.Sort((a, b) =>
        {
            int bias = b.Bias.CompareTo(a.Bias);
            return bias != 0 ? bias : a.Distance.CompareTo(b.Distance);
        });

        int enabled = 0;
        int shadowed = 0;
        for (int index = 0; index < candidates.Count; index++)
        {
            Light3D light = candidates[index].Light;
            bool allow = index < settings.MaximumLocalLights;
            if (!allow)
            {
                light.LightEnergy = 0.0f;
                light.ShadowEnabled = false;
                continue;
            }

            enabled++;
            light.LightEnergy = _task208OriginalLightEnergy[light];
            bool originalShadow = _task208OriginalShadowState[light];
            bool allowShadow = GraphicsShadowsEnabled && originalShadow &&
                shadowed < settings.MaximumShadowedLocalLights;
            light.ShadowEnabled = allowShadow;
            if (allowShadow)
            {
                shadowed++;
            }
        }

        _task208LocalLightsFound = lights.Count;
        _task208LocalLightsActive = enabled;
        _task208ShadowedLocalLights = shadowed;

        if (force)
        {
            GD.Print(
                "TASK-208 lighting residency PASS: " +
                $"world={worldKind}; cave={(IsPlayerInsidePlanetaryCave ? 1 : 0)}; " +
                $"local={enabled}/{lights.Count}; max={settings.MaximumLocalLights}; " +
                $"shadowed={shadowed}/{settings.MaximumShadowedLocalLights}; " +
                $"distance={settings.MaximumLocalLightDistanceMeters:0}m.");
        }
    }

    private Vector3 ResolveTask208LightingObserver()
    {
        if (StageOneVoyage.Piloted && _voyageShip is not null)
        {
            return _voyageShip.GlobalPosition;
        }
        return _player?.GlobalPosition ?? GlobalPosition;
    }

    private static int Task208LightPriorityBias(string name)
    {
        if (name.Contains("Instrument", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Dock", StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }
        if (name.Contains("Hangar", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Cave", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }
        if (name.Contains("Discovery", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Guide", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }
        return 1;
    }

    private static void CollectTask208LocalLights(Node node, List<Light3D> output)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is OmniLight3D or SpotLight3D)
            {
                output.Add((Light3D)child);
            }
            CollectTask208LocalLights(child, output);
        }
    }

    private void RunTextureLightingBudgetAcceptance()
    {
        ApplyTextureLightingResidency(force: false);
        WorldSceneKind kind = WorldScenes.Current.Kind;
        TextureLightingBudgetAcceptanceReport report =
            TextureLightingBudgetAcceptanceRunner.Evaluate(
                kind,
                IsPlayerInsidePlanetaryCave,
                GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D"),
                GetNodeOrNull<WorldEnvironment>("WorldEnvironment"),
                _task208LocalLightsFound,
                _task208LocalLightsActive,
                _task208ShadowedLocalLights);
        _textureLightingAcceptancePassed = report.Passed;
        _textureLightingAcceptanceHud = report.Passed
            ? $"PASS tex=8 light={report.LocalLightsActive}/{report.LocalLightsFound} shadow={report.ShadowedLocalLights}"
            : "FAIL texture/light budget";
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
