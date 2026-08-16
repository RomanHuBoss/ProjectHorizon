using System;
using Godot;

public partial class SalvageRepairSlice
{
    private string _lastWorldEnvironmentPresentationProfile = string.Empty;
    private bool _orbitalHandoffSourceCaptured;
    private Color _orbitalHandoffSourceBackground;
    private Color _orbitalHandoffSourceAmbient;
    private float _orbitalHandoffSourceAmbientEnergy;
    private bool _orbitalHandoffSourceFogEnabled;
    private float _orbitalHandoffSourceFogDensity;
    private Color _orbitalHandoffSourceDirectionalColor = Colors.White;
    private float _orbitalHandoffSourceDirectionalEnergy = 1.0f;
    private double _orbitalHandoffSourceBlend;
    private Vector3 _smoothedOrbitalLightDirection = new(0.35f, -0.22f, 0.91f);
    private bool _orbitalLightDirectionInitialized;

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
            if (kind == WorldSceneKind.Surface)
            {
                _orbitalHandoffSourceCaptured = false;
                _orbitalLightDirectionInitialized = false;
            }
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
            DirectionalLight3D? surfaceDirectional =
                GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");
            if (surfaceDirectional is not null)
            {
                surfaceDirectional.ShadowEnabled = _surfaceRuntimeActive;
                surfaceDirectional.Set("directional_shadow_max_distance", 320.0f);
            }

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
            DirectionalLight3D? currentDirectional =
                GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");
            if (!_orbitalHandoffSourceCaptured)
            {
                // TASK-178.4: capture the *actual* weather-driven frame at the
                // first upper-atmosphere sample. The previous implementation
                // jumped to hard-coded colors at 110 m, which was mathematically
                // smooth afterwards but still visibly stepped at ownership handoff.
                _orbitalHandoffSourceCaptured = true;
                _orbitalHandoffSourceBackground = environment.BackgroundColor;
                _orbitalHandoffSourceAmbient = environment.AmbientLightColor;
                _orbitalHandoffSourceAmbientEnergy = environment.AmbientLightEnergy;
                _orbitalHandoffSourceFogEnabled = environment.FogEnabled;
                _orbitalHandoffSourceFogDensity = environment.FogDensity;
                _orbitalHandoffSourceDirectionalColor =
                    currentDirectional?.LightColor ?? new Color(1.0f, 0.92f, 0.80f);
                _orbitalHandoffSourceDirectionalEnergy =
                    currentDirectional?.LightEnergy ?? 1.2f;
                _orbitalHandoffSourceBlend = handoff.VacuumBlend;
            }

            float t = (float)Math.Clamp(
                (handoff.VacuumBlend - _orbitalHandoffSourceBlend) /
                    Math.Max(0.000001, 1.0 - _orbitalHandoffSourceBlend),
                0.0,
                1.0);
            Color vacuumBackground = new(
                (float)profile.BackgroundRed,
                (float)profile.BackgroundGreen,
                (float)profile.BackgroundBlue);
            Color vacuumAmbient = new(
                (float)profile.AmbientRed,
                (float)profile.AmbientGreen,
                (float)profile.AmbientBlue);

            background = _orbitalHandoffSourceBackground.Lerp(vacuumBackground, t);
            ambient = _orbitalHandoffSourceAmbient.Lerp(vacuumAmbient, t);
            ambientEnergy = Mathf.Lerp(
                _orbitalHandoffSourceAmbientEnergy,
                (float)profile.AmbientEnergy,
                t);
            directionalEnergy = Mathf.Lerp(
                _orbitalHandoffSourceDirectionalEnergy,
                (float)profile.DirectionalEnergy,
                t);
            float remainingAtmosphere = 1.0f - t;
            fogEnabled = _orbitalHandoffSourceFogEnabled && t < 0.985f;
            fogDensity = _orbitalHandoffSourceFogDensity *
                remainingAtmosphere * remainingAtmosphere;
            directionalColor = _orbitalHandoffSourceDirectionalColor.Lerp(
                new Color(0.94f, 0.96f, 1.0f),
                t);
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
            // TASK-180.1: an orbital camera has a 1,200 km far plane. Feeding that
            // frustum into directional shadow splitting produced repeated
            // create_frustum_points errors in the runtime log. Surface weather
            // owns bounded 320 m shadows; orbit/transit/interior use direct light
            // without distant dynamic shadows.
            directional.ShadowEnabled = false;
            directional.Set("directional_shadow_max_distance", 320.0f);
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

    private void UpdateOrbitalKeyLightDirection(double delta)
    {
        if (_worldSceneCoordinatorRuntime is null ||
            _starSystemSimulationNode is null ||
            _galaxyNavigationRuntime is null ||
            WorldScenes.Current.Kind is not
                (WorldSceneKind.Orbit or WorldSceneKind.InterplanetaryTransit))
        {
            return;
        }

        if (WorldScenes.Current.Kind == WorldSceneKind.Orbit &&
            OrbitalHandoffPresentationRuntime.Evaluate(
                _voyageShip?.AltitudeAboveSurface ?? double.PositiveInfinity)
                .SurfaceSkyOwned)
        {
            // Surface weather owns the sun direction until the visual handoff
            // actually begins; orbital lighting must not rotate the lower-sky sun.
            return;
        }

        string starId = $"{GalaxyNavigation.CurrentSystem.SystemId}.star";
        if (!_starSystemSimulationNode.TryGetBodyDisplayPosition(
                starId,
                out Vector3 starPosition) ||
            !_starSystemSimulationNode.TryGetBodyDisplayPosition(
                GalaxyNavigation.CurrentPlanetId,
                out Vector3 planetPosition))
        {
            return;
        }

        Vector3 desired = planetPosition - starPosition;
        if (desired.LengthSquared() < 0.0001f)
        {
            return;
        }
        desired = desired.Normalized();

        if (WorldScenes.Current.Kind == WorldSceneKind.Orbit)
        {
            // TASK-178.7: blend the *direction* of the key light through the
            // same atmosphere/vacuum envelope, not just its color/energy. This
            // keeps the day/night terminator continuous on both ascent and
            // re-entry and prevents the lower-atmosphere weather sun from
            // snapping to a different orbital star direction at ownership
            // handoff.
            OrbitalHandoffPresentationState handoff =
                OrbitalHandoffPresentationRuntime.Evaluate(
                    _voyageShip?.AltitudeAboveSurface ?? double.PositiveInfinity);
            Vector3 surfaceRay = -SurfaceLocalDirectionToWorld(
                _planetSurfaceSunDirection).Normalized();
            float lightingBlend = (float)Math.Clamp(
                handoff.VacuumBlend, 0.0, 1.0);
            Vector3 blendedTarget = surfaceRay.Lerp(desired, lightingBlend);
            if (blendedTarget.LengthSquared() > 0.0001f)
            {
                desired = blendedTarget.Normalized();
            }
        }

        if (!_orbitalLightDirectionInitialized)
        {
            _smoothedOrbitalLightDirection = desired;
            _orbitalLightDirectionInitialized = true;
        }
        else
        {
            float factor = 1.0f - Mathf.Exp(-(float)Math.Max(0.0, delta) * 3.2f);
            Vector3 blended = _smoothedOrbitalLightDirection.Lerp(desired, factor);
            if (blended.LengthSquared() > 0.0001f)
            {
                _smoothedOrbitalLightDirection = blended.Normalized();
            }
        }

        DirectionalLight3D? directional =
            GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");
        if (directional is null)
        {
            return;
        }

        Vector3 up = Math.Abs(_smoothedOrbitalLightDirection.Dot(Vector3.Up)) > 0.96f
            ? Vector3.Right
            : Vector3.Up;
        directional.LookAt(
            directional.GlobalPosition + _smoothedOrbitalLightDirection,
            up);
    }
}
