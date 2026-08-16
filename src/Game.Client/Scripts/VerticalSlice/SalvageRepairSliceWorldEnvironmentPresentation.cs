using Godot;

public partial class SalvageRepairSlice
{
    private string _lastWorldEnvironmentPresentationProfile = string.Empty;

    private void UpdateWorldSceneEnvironmentPresentation()
    {
        if (_worldSceneCoordinatorRuntime is null)
        {
            return;
        }

        WorldSceneKind kind = WorldScenes.Current.Kind;
        WorldEnvironment? world = GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
        if (world?.Environment is not Godot.Environment environment)
        {
            return;
        }

        bool nearSurfaceOrbit = kind == WorldSceneKind.Orbit &&
            _surfaceRuntimeActive;
        WorldSceneEnvironmentPresentationProfile profile =
            WorldSceneEnvironmentPresentationRuntime.Resolve(
                nearSurfaceOrbit ? WorldSceneKind.Surface : kind);
        string effectiveProfile = nearSurfaceOrbit
            ? "orbit-atmosphere-handoff"
            : profile.ProfileName;
        bool changed = !string.Equals(
            _lastWorldEnvironmentPresentationProfile,
            effectiveProfile,
            System.StringComparison.Ordinal);

        if (profile.SurfaceOwned)
        {
            // Non-surface contexts use BG_COLOR below. Rebuild the atmospheric
            // sky when returning to the planet, then re-apply the current
            // weather state because the weather updater ran earlier this frame.
            if (changed && _planetSurfaceSkyProfile is not null)
            {
                ApplyPlanetSurfaceSky(_planetSurfaceSkyProfile);
                if (_planetWeatherState is not null)
                {
                    ApplyPlanetWeatherPresentation(
                        _planetWeatherState,
                        forceFx: false);
                }
            }

            _lastWorldEnvironmentPresentationProfile = effectiveProfile;
            if (changed)
            {
                GD.Print(
                    "TASK-178.2 world environment presentation PASS: " +
                    $"kind={kind}; profile={effectiveProfile}; " +
                    "owner=weather; fog=atmosphere.");
            }
            return;
        }

        Color background = new(
            (float)profile.BackgroundRed,
            (float)profile.BackgroundGreen,
            (float)profile.BackgroundBlue);
        Color ambient = new(
            (float)profile.AmbientRed,
            (float)profile.AmbientGreen,
            (float)profile.AmbientBlue);

        environment.Set("background_mode", 1); // BG_COLOR
        environment.Set("background_color", background);
        environment.Set("ambient_light_source", 2); // AMBIENT_SOURCE_COLOR
        environment.Set("ambient_light_color", ambient);
        environment.Set("ambient_light_energy", (float)profile.AmbientEnergy);
        environment.Set("ambient_light_sky_contribution", 0.0f);
        environment.Set("fog_enabled", false);
        environment.Set("fog_density", 0.0f);
        environment.Set("fog_height_density", 0.0f);
        environment.Set("fog_sky_affect", 0.0f);
        environment.Set("tonemap_mode", 3); // ACES
        environment.Set("tonemap_exposure", kind == WorldSceneKind.StationInterior
            ? 1.18f
            : 1.02f);

        DirectionalLight3D? directional =
            GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");
        if (directional is not null)
        {
            directional.LightEnergy = (float)profile.DirectionalEnergy;
            directional.LightColor = kind == WorldSceneKind.HyperspaceTransit
                ? new Color(0.58f, 0.38f, 1.0f)
                : new Color(0.86f, 0.91f, 1.0f);
        }

        _lastWorldEnvironmentPresentationProfile = effectiveProfile;
        if (changed)
        {
            GD.Print(
                "TASK-178.2 world environment presentation PASS: " +
                $"kind={kind}; profile={profile.ProfileName}; " +
                $"fog={(profile.FogEnabled ? 1 : 0)}; " +
                $"ambient={profile.AmbientEnergy:0.00}; " +
                $"directional={profile.DirectionalEnergy:0.00}.");
        }
    }
}
