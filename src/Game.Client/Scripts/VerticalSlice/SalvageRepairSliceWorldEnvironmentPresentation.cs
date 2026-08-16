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

        double altitude = _voyageShip?.AltitudeAboveSurface ??
            double.PositiveInfinity;
        OrbitalHandoffPresentationState handoff =
            OrbitalHandoffPresentationRuntime.Evaluate(altitude);

        bool surfaceOwned = kind == WorldSceneKind.Surface ||
            (kind == WorldSceneKind.Orbit && handoff.SurfaceSkyOwned);
        string effectiveProfile = kind == WorldSceneKind.Orbit
            ? handoff.Phase
            : WorldSceneEnvironmentPresentationRuntime.Resolve(kind).ProfileName;
        bool changed = !string.Equals(
            _lastWorldEnvironmentPresentationProfile,
            effectiveProfile,
            System.StringComparison.Ordinal);

        if (surfaceOwned)
        {
            // The lower-atmosphere part of Orbit deliberately remains owned by
            // the same weather sky as Surface. This avoids a one-frame switch
            // from blue atmosphere to black vacuum at the old ~85 m boundary.
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
                    "TASK-178.3 world environment handoff PASS: " +
                    $"kind={kind}; phase={effectiveProfile}; " +
                    $"altitude={altitude:0.0}m; blend={handoff.VacuumBlend:0.000}; " +
                    "owner=weather.");
            }
            return;
        }

        WorldSceneEnvironmentPresentationProfile profile =
            WorldSceneEnvironmentPresentationRuntime.Resolve(kind);

        Color background;
        Color ambient;
        float ambientEnergy;
        float directionalEnergy;
        bool fogEnabled;
        float fogDensity;
        Color directionalColor;

        if (kind == WorldSceneKind.Orbit)
        {
            float t = (float)handoff.VacuumBlend;
            Color highAtmosphere = new(0.16f, 0.28f, 0.50f);
            Color highAmbient = new(0.30f, 0.36f, 0.48f);
            Color vacuumBackground = new(
                (float)profile.BackgroundRed,
                (float)profile.BackgroundGreen,
                (float)profile.BackgroundBlue);
            Color vacuumAmbient = new(
                (float)profile.AmbientRed,
                (float)profile.AmbientGreen,
                (float)profile.AmbientBlue);

            background = highAtmosphere.Lerp(vacuumBackground, t);
            ambient = highAmbient.Lerp(vacuumAmbient, t);
            ambientEnergy = Mathf.Lerp(
                0.46f,
                (float)profile.AmbientEnergy,
                t);
            directionalEnergy = Mathf.Lerp(
                1.22f,
                (float)profile.DirectionalEnergy,
                t);
            fogEnabled = t < 0.96f;
            float remainingAtmosphere = 1.0f - t;
            fogDensity = 0.0022f *
                remainingAtmosphere * remainingAtmosphere;
            directionalColor = new Color(1.0f, 0.92f, 0.80f)
                .Lerp(new Color(0.86f, 0.91f, 1.0f), t);
        }
        else
        {
            background = new Color(
                (float)profile.BackgroundRed,
                (float)profile.BackgroundGreen,
                (float)profile.BackgroundBlue);
            ambient = new Color(
                (float)profile.AmbientRed,
                (float)profile.AmbientGreen,
                (float)profile.AmbientBlue);
            ambientEnergy = (float)profile.AmbientEnergy;
            directionalEnergy = (float)profile.DirectionalEnergy;
            fogEnabled = profile.FogEnabled;
            fogDensity = 0.0f;
            directionalColor = kind == WorldSceneKind.HyperspaceTransit
                ? new Color(0.58f, 0.38f, 1.0f)
                : new Color(0.86f, 0.91f, 1.0f);
        }

        environment.Set("background_mode", 1); // BG_COLOR
        environment.Set("background_color", background);
        environment.Set("ambient_light_source", 2); // AMBIENT_SOURCE_COLOR
        environment.Set("ambient_light_color", ambient);
        environment.Set("ambient_light_energy", ambientEnergy);
        environment.Set("ambient_light_sky_contribution", 0.0f);
        environment.Set("fog_enabled", fogEnabled);
        environment.Set("fog_density", fogDensity);
        environment.Set("fog_height_density", 0.0f);
        environment.Set("fog_sky_affect", fogEnabled ? 0.18f : 0.0f);
        environment.Set("tonemap_mode", 3); // ACES
        environment.Set(
            "tonemap_exposure",
            kind == WorldSceneKind.StationInterior
                ? 1.18f
                : kind == WorldSceneKind.Orbit
                    ? 1.12f
                    : 1.06f);

        DirectionalLight3D? directional =
            GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");
        if (directional is not null)
        {
            directional.LightEnergy = directionalEnergy;
            directional.LightColor = directionalColor;
        }

        _lastWorldEnvironmentPresentationProfile = effectiveProfile;
        if (changed)
        {
            GD.Print(
                "TASK-178.3 world environment handoff PASS: " +
                $"kind={kind}; phase={effectiveProfile}; " +
                $"altitude={altitude:0.0}m; blend={handoff.VacuumBlend:0.000}; " +
                $"fog={(fogEnabled ? 1 : 0)}; ambient={ambientEnergy:0.00}; " +
                $"directional={directionalEnergy:0.00}.");
        }
    }
}
