using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private bool _planetSurfaceWorldCompositionInitialized;
    private PlanetSurfaceSkyProfile? _planetSurfaceSkyProfile;
    private ProceduralSkyMaterial? _planetSurfaceSkyMaterial;
    private StandardMaterial3D? _planetSurfaceCloudMaterial;
    private Node3D? _planetSurfaceCloudRoot;
    private Node3D? _planetSurfaceSunVisual;
    private Vector3 _planetSurfaceSunDirection = new(0.0f, 0.65f, -0.76f);
    private Node3D? _planetSurfaceResourceRoot;
    private readonly Dictionary<string, SalvageResourceNode>
        _streamedSurfaceResources = new(StringComparer.Ordinal);
    private PlanetSurfaceChunkCoordinate? _lastSurfaceResourceCenter;
    private string _planetSurfaceWorldCompositionAcceptanceHud = "READY";
    private string _surfacePresentationHotfixAcceptanceHud = "READY";
    private bool _planetSurfaceWorldCompositionReadyPrinted;
    private double _planetSurfaceCloudDrift;
    private Vector3 _planetSurfaceSkyFrameUp = Vector3.Up;
    private bool _planetSurfaceAtmosphereFrameAligned;

    private void InitializePlanetSurfaceWorldComposition()
    {
        if (_planetSurfaceWorldCompositionInitialized)
        {
            return;
        }

        _planetSurfaceCloudRoot = new Node3D
        {
            Name = "PlanetSurfaceClouds"
        };
        (GetNodeOrNull<Node3D>("Gameplay") ?? this).AddChild(
            _planetSurfaceCloudRoot);
        _planetSurfaceResourceRoot = new Node3D
        {
            Name = "PlanetSurfaceResources"
        };
        (GetNodeOrNull<Node3D>("Gameplay") ?? this).AddChild(
            _planetSurfaceResourceRoot);

        SuppressLegacyResourceFixtures();
        _planetSurfaceWorldCompositionInitialized = true;
        ApplyPlanetSurfaceWorldComposition();
    }

    private void ApplyPlanetSurfaceWorldComposition()
    {
        if (!_planetSurfaceWorldCompositionInitialized ||
            _planetSurfaceContentProfile is null)
        {
            return;
        }

        _planetSurfaceSkyProfile =
            PlanetSurfaceWorldCompositionRuntime.BuildSkyProfile(
                PlanetSurfaceContentProfile.Environment,
                GalaxyNavigation.CurrentSystem.StarType);
        ApplyPlanetSurfaceSky(_planetSurfaceSkyProfile);
        RebuildPlanetSurfaceClouds(_planetSurfaceSkyProfile);
        ApplyPlanetAtmosphereCloudProfile(PlanetSurfaceContentProfile.Environment);
        ClearStreamedSurfaceResources();
        _lastSurfaceResourceCenter = null;
        _planetSurfaceWorldCompositionReadyPrinted = false;
        _planetCurvedSurfaceReadyPrinted = false;
        RefreshStreamedSurfaceResources(force: true);
    }

    private void ApplyPlanetSurfaceSky(PlanetSurfaceSkyProfile profile)
    {
        WorldEnvironment? world = GetNodeOrNull<WorldEnvironment>(
            "WorldEnvironment");
        if (world?.Environment is not Godot.Environment environment)
        {
            return;
        }

        Color top = ToColor(profile.SkyTopColor);
        Color horizon = ToColor(profile.SkyHorizonColor);
        Color groundHorizon = ToColor(profile.GroundHorizonColor);
        ProceduralSkyMaterial skyMaterial = new();
        _planetSurfaceSkyMaterial = skyMaterial;
        skyMaterial.Set("sky_top_color", top);
        skyMaterial.Set("sky_horizon_color", horizon);
        skyMaterial.Set(
            "ground_bottom_color",
            profile.AtmosphereEnabled
                ? horizon.Lerp(top, 0.10f)
                : groundHorizon.Darkened(0.52f));
        skyMaterial.Set(
            "ground_horizon_color",
            profile.AtmosphereEnabled
                ? horizon.Lerp(top, 0.16f)
                : groundHorizon);
        skyMaterial.Set("sun_angle_max", 7.0f);
        skyMaterial.Set("sun_curve", 0.045f);
        skyMaterial.Set("sky_curve", 0.18f);
        skyMaterial.Set("ground_curve", 0.08f);
        skyMaterial.Set("use_debanding", true);
        skyMaterial.Set(
            "sky_energy_multiplier",
            profile.AtmosphereEnabled ? 1.08f : 0.40f);

        Sky sky = new();
        sky.Set("sky_material", skyMaterial);
        environment.Set("sky", sky);
        environment.Set("background_mode", 2); // BG_SKY
        environment.Set("ambient_light_source", 3); // AMBIENT_SOURCE_SKY
        environment.Set("ambient_light_sky_contribution", 0.82f);
        environment.Set(
            "ambient_light_energy",
            profile.AtmosphereEnabled ? 0.88f : 0.28f);
        environment.Set("ambient_light_color", horizon);
        environment.Set("reflection_source", 2); // REFLECTION_SOURCE_SKY
        environment.Set("tonemap_mode", 3); // TONEMAPPER_ACES
        environment.Set("tonemap_exposure", 1.05f);
        environment.Set("tonemap_white", 1.6f);
        environment.Set("fog_enabled", profile.AtmosphereEnabled);
        environment.Set("fog_density", (float)profile.FogDensity);
        environment.Set("fog_light_color", horizon.Lightened(0.12f));
        environment.Set("fog_light_energy", 1.0f);
        environment.Set("fog_aerial_perspective", 0.72f);
        environment.Set("fog_sun_scatter", (float)profile.FogSunScatter);
        environment.Set("fog_sky_affect", 0.32f);
        // Height fog is global-Y based. A radial planet rotates local Up, so a
        // height layer would become a vertical wall on +X/-X/+Z/-Z faces. Use
        // isotropic exponential/aerial fog for surface atmosphere instead.
        environment.Set("fog_height", 0.0f);
        environment.Set("fog_height_density", 0.0f);

        double azimuth = profile.SunAzimuthDegrees * Math.PI / 180.0;
        double elevation = profile.SunElevationDegrees * Math.PI / 180.0;
        Vector3 towardSun = new(
            (float)(Math.Cos(elevation) * Math.Sin(azimuth)),
            (float)Math.Sin(elevation),
            (float)(Math.Cos(elevation) * Math.Cos(azimuth)));
        towardSun = towardSun.Normalized();
        EnsurePlanetSurfaceSunVisual(profile, towardSun);

        DirectionalLight3D? sun = GetNodeOrNull<DirectionalLight3D>(
            "DirectionalLight3D");
        if (sun is null)
        {
            return;
        }

        sun.LightColor = ToColor(profile.SunColor);
        sun.LightEnergy = (float)profile.SunEnergy;
        bool surfaceLightingOwned = _worldSceneCoordinatorRuntime is null ||
            WorldScenes.Current.Kind == WorldSceneKind.Surface ||
            (WorldScenes.Current.Kind == WorldSceneKind.Orbit &&
                OrbitalHandoffPresentationRuntime.Evaluate(
                    _voyageShip?.AltitudeAboveSurface ?? double.PositiveInfinity)
                    .SurfaceSkyOwned);
        sun.ShadowEnabled = surfaceLightingOwned;
        // TASK-174: the procedural sky is rotated with radial Up. Godot rotates
        // its procedural sun disk with sky_rotation too, which would diverge
        // from the actual directional light. Keep lighting only here; the
        // explicit PlanetSurfaceSunVisual is the canonical stellar disc.
        sun.Set("sky_mode", 1); // LIGHT_ONLY
        sun.Set("light_angular_distance", (float)profile.SunAngularDiameterDegrees);
        sun.Set("directional_shadow_max_distance", 320.0f);
        sun.Set("directional_shadow_fade_start", 0.82f);
        Vector3 lightRay = -SurfaceLocalDirectionToWorld(towardSun).Normalized();
        sun.Position = Vector3.Zero;
        sun.LookAt(lightRay, SurfaceLocalDirectionToWorld(Vector3.Up).Normalized());
        Basis surfaceBasis = _planetSurfacePhysicalFrameState?.SurfaceBasis ??
            (GetNodeOrNull<Node3D>("Gameplay")?.GlobalTransform.Basis ?? Basis.Identity);
        ApplyPlanetSurfaceAtmosphereFrame(surfaceBasis);
    }

    private void ApplyPlanetSurfaceAtmosphereFrame(Basis surfaceBasis)
    {
        WorldEnvironment? world = GetNodeOrNull<WorldEnvironment>(
            "WorldEnvironment");
        if (world?.Environment is not Godot.Environment environment)
        {
            return;
        }

        Basis radialBasis = surfaceBasis.Orthonormalized();
        Vector3 up = radialBasis.Y.Normalized();
        // Environment.sky_rotation rotates ProceduralSkyMaterial's sky/ground
        // hemisphere. Without this, a +X radial surface sees Godot's global-Y
        // horizon as a vertical half-screen day/night-looking seam.
        environment.Set("sky_rotation", radialBasis.GetEuler());
        environment.Set("fog_height_density", 0.0f);
        _planetSurfaceSkyFrameUp = up;
        _planetSurfaceAtmosphereFrameAligned = true;
    }

    private void EnsurePlanetSurfaceSunVisual(
        PlanetSurfaceSkyProfile profile,
        Vector3 towardSun)
    {
        Node3D parent = GetNodeOrNull<Node3D>("Gameplay") ?? this;
        if (_planetSurfaceSunVisual is null ||
            !GodotObject.IsInstanceValid(_planetSurfaceSunVisual))
        {
            _planetSurfaceSunVisual = new Node3D
            {
                Name = "PlanetSurfaceSunVisual"
            };
            parent.AddChild(_planetSurfaceSunVisual);
        }

        foreach (Node child in _planetSurfaceSunVisual.GetChildren())
        {
            child.QueueFree();
        }

        Color sunColor = ToColor(profile.SunColor);
        StandardMaterial3D coreMaterial = new()
        {
            AlbedoColor = sunColor.Lightened(0.20f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = sunColor,
            EmissionEnergyMultiplier = 5.0f,
            Roughness = 0.0f
        };
        SphereMesh coreMesh = new()
        {
            Radius = 1.0f,
            Height = 2.0f,
            RadialSegments = 24,
            Rings = 12
        };
        _planetSurfaceSunVisual.AddChild(new MeshInstance3D
        {
            Name = "Core",
            Mesh = coreMesh,
            MaterialOverride = coreMaterial,
            Scale = Vector3.One * 7.5f
        });

        StandardMaterial3D haloMaterial = new()
        {
            AlbedoColor = new Color(sunColor.R, sunColor.G, sunColor.B, 0.12f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = sunColor,
            EmissionEnergyMultiplier = 2.1f,
            Roughness = 0.0f
        };
        SphereMesh haloMesh = new()
        {
            Radius = 1.0f,
            Height = 2.0f,
            RadialSegments = 20,
            Rings = 10
        };
        _planetSurfaceSunVisual.AddChild(new MeshInstance3D
        {
            Name = "Halo",
            Mesh = haloMesh,
            MaterialOverride = haloMaterial,
            Scale = Vector3.One * 13.5f
        });

        _planetSurfaceSunDirection = towardSun;
        UpdatePlanetSurfaceSunVisual();
    }

    private void UpdatePlanetSurfaceSunVisual()
    {
        if (_planetSurfaceSunVisual is null ||
            !GodotObject.IsInstanceValid(_planetSurfaceSunVisual))
        {
            return;
        }

        bool surfaceSceneOwnsSky = PlanetSurfaceWorldCompositionRuntime.ShouldRenderSurfaceSun(
            _surfaceRuntimeActive,
            WorldScenes.Current.Kind);
        _planetSurfaceSunVisual.Visible = surfaceSceneOwnsSky;
        if (!surfaceSceneOwnsSky || _player is null)
        {
            return;
        }

        // Follow the local floating-origin frame while preserving a fixed
        // planet-sky direction. Keep the disc close enough that atmospheric fog
        // cannot erase it, while its ~1.3 degree angular size still reads as a
        // celestial object rather than nearby scenery.
        _planetSurfaceSunVisual.GlobalPosition =
            _player.GlobalPosition +
            SurfaceLocalDirectionToWorld(_planetSurfaceSunDirection).Normalized() * 900.0f;
    }

    private void RebuildPlanetSurfaceClouds(PlanetSurfaceSkyProfile profile)
    {
        _ = profile;
        // TASK-190 supersedes the old local lobe-cloud placeholder with one or
        // two spherical noise-texture layers. Keep the root only as a stable
        // legacy scene anchor, but never repopulate it.
        RetireLegacyCloudClusters();
    }

    private void SuppressLegacyResourceFixtures()
    {
        foreach (SalvageResourceNode resource in _resourceNodes)
        {
            bool starterRepairResource =
                string.Equals(resource.ResourceNodeId, "salvage.alpha", StringComparison.Ordinal) ||
                string.Equals(resource.ResourceNodeId, "salvage.beta", StringComparison.Ordinal) ||
                string.Equals(resource.ResourceNodeId, "salvage.gamma", StringComparison.Ordinal);
            resource.SetRuntimeSuppressed(!starterRepairResource);
        }
    }

    private void UpdatePlanetSurfaceWorldComposition(double delta)
    {
        if (!_planetSurfaceWorldCompositionInitialized)
        {
            return;
        }

        UpdatePlanetSurfaceSunVisual();

        if (_planetSurfaceCloudRoot is not null)
        {
            // TASK-190 legacy cloud root remains empty and hidden.
            _planetSurfaceCloudRoot.Visible = false;
        }

        if (_planetSurfaceResourceRoot is not null)
        {
            _planetSurfaceResourceRoot.Visible = _surfaceRuntimeActive;
        }

        if (!_surfaceRuntimeActive ||
            _planetSurfaceStreamer is null ||
            !_planetSurfaceStreamer.IsStreamingSettled)
        {
            return;
        }

        RefreshStreamedSurfaceResources(force: false);
        UpdatePlanetaryPoiResidency();
    }

    private void RefreshStreamedSurfaceResources(bool force)
    {
        if (!_planetSurfaceWorldCompositionInitialized ||
            _planetSurfaceResourceRoot is null ||
            _planetSurfaceContentProfile is null ||
            _player is null)
        {
            return;
        }

        PlanetSurfaceLogicalPosition logicalPlayer =
            GetPlanetSurfaceLogicalPlayerPosition();
        PlanetSurfaceChunkCoordinate center =
            PlanetSurfaceStreamingRuntime.WorldToChunk(
                logicalPlayer.EastMeters,
                logicalPlayer.NorthMeters);
        if (!force && _lastSurfaceResourceCenter is { } previous &&
            previous == center)
        {
            return;
        }

        IReadOnlyList<PlanetSurfaceResourcePlacement> plan =
            PlanetSurfaceWorldCompositionRuntime.BuildResourceWindow(
                PlanetSurfaceContentProfile,
                ContentCatalog.Resources,
                center);
        HashSet<string> desired = plan
            .Select(placement => placement.ResourceNodeId)
            .ToHashSet(StringComparer.Ordinal);

        bool resourcesChanged = false;
        foreach (string existingId in _streamedSurfaceResources.Keys.ToArray())
        {
            if (desired.Contains(existingId))
            {
                continue;
            }
            SalvageResourceNode node = _streamedSurfaceResources[existingId];
            _streamedSurfaceResources.Remove(existingId);
            resourcesChanged = true;
            if (GodotObject.IsInstanceValid(node))
            {
                node.QueueFree();
            }
        }

        foreach (PlanetSurfaceResourcePlacement placement in plan)
        {
            if (_streamedSurfaceResources.ContainsKey(placement.ResourceNodeId) ||
                Session.CollectedNodeIds.Contains(
                    placement.ResourceNodeId,
                    StringComparer.Ordinal))
            {
                continue;
            }
            CreateStreamedSurfaceResource(placement);
            resourcesChanged = true;
        }

        if (resourcesChanged)
        {
            RefreshNpcNavigationObstacles();
            RefreshAerialNavigationEnvironment();
        }
        _lastSurfaceResourceCenter = center;
        if (!_planetSurfaceWorldCompositionReadyPrinted)
        {
            GD.Print(
                "TASK-160 planet surface world composition READY: " +
                $"planet={PlanetSurfaceContentProfile.PlanetId}; " +
                $"sky={( _planetSurfaceSkyProfile?.AtmosphereEnabled == true ? 1 : 0)}; " +
                $"sun=1; sunVisual={(_planetSurfaceSunVisual is not null && GodotObject.IsInstanceValid(_planetSurfaceSunVisual) ? 1 : 0)}; " +
                $"distantTerrain={(_planetSurfaceDistantTerrain is not null && GodotObject.IsInstanceValid(_planetSurfaceDistantTerrain) ? 1 : 0)}; " +
                $"clouds={_planetSurfaceSkyProfile?.CloudClusterCount ?? 0}; " +
                $"resourceWindow={plan.Count}; activeResources={_streamedSurfaceResources.Count}; " +
                $"center={center.X},{center.Z}; starterReserve={PlanetSurfaceWorldCompositionRuntime.StarterReserveRadiusMeters:0}m; " +
                "identity=planet+chunk+slot; persistence=seed+deltas; legacyFixtures=hidden.");
            _planetSurfaceWorldCompositionReadyPrinted = true;
        }
    }

    private void UpdatePlanetaryPoiResidency()
    {
        if (_player is null)
        {
            return;
        }
        PlanetSurfaceLogicalPosition logicalPlayer =
            GetPlanetSurfaceLogicalPlayerPosition();
        PlanetSurfaceChunkCoordinate center =
            PlanetSurfaceStreamingRuntime.WorldToChunk(
                logicalPlayer.EastMeters,
                logicalPlayer.NorthMeters);
        foreach (PlanetaryPoiNode poi in _planetaryPoiNodes)
        {
            Vector3 poiLogical =
                WorldToPlanetSurfaceLogicalPosition(poi.GlobalPosition);
            PlanetSurfaceChunkCoordinate poiChunk =
                PlanetSurfaceStreamingRuntime.WorldToChunk(
                    poiLogical.X,
                    poiLogical.Z);
            int distance = Math.Max(
                Math.Abs(poiChunk.X - center.X),
                Math.Abs(poiChunk.Z - center.Z));
            poi.SetRuntimeResident(
                _surfaceRuntimeActive &&
                distance <= PlanetSurfaceStreamingRuntime.ActiveRadius);
        }
    }

    private void CreateStreamedSurfaceResource(
        PlanetSurfaceResourcePlacement placement)
    {
        if (_planetSurfaceResourceRoot is null ||
            !ContentCatalog.Resources.TryGetValue(
                placement.ResourceDefinitionId,
                out GameResourceDefinition? definition) ||
            definition is null)
        {
            return;
        }

        SalvageResourceNode node = new()
        {
            Name = placement.ResourceNodeId.Replace('.', '_'),
            ResourceNodeId = placement.ResourceNodeId,
            ResourceDefinitionId = definition.ResourceId,
            Position = new Vector3(
                (float)placement.PositionX,
                (float)SamplePlanetSurfacePhysicalHeight(
                    placement.PositionX, placement.PositionZ) + 0.38f,
                (float)placement.PositionZ),
            RotationDegrees = new Vector3(
                0.0f,
                (float)placement.RotationDegrees,
                0.0f),
            Scale = Vector3.One * (float)placement.Scale
        };
        node.AddToGroup("interactable");
        node.AddToGroup("planet_surface_resource");

        MeshInstance3D meshInstance =
            ProceduralSurfaceVisualFactory.CreateResourceVisual(definition);
        CollisionShape3D collision = new()
        {
            Name = "CollisionShape3D",
            Shape = new SphereShape3D
            {
                Radius = 0.66f
            }
        };
        node.AddChild(meshInstance);
        node.AddChild(collision);
        node.ConfigureDefinition(definition);
        _planetSurfaceResourceRoot.AddChild(node);
        _streamedSurfaceResources[placement.ResourceNodeId] = node;
    }

    private void ClearStreamedSurfaceResources()
    {
        foreach (SalvageResourceNode resource in
                 _streamedSurfaceResources.Values.ToArray())
        {
            if (GodotObject.IsInstanceValid(resource))
            {
                resource.QueueFree();
            }
        }
        _streamedSurfaceResources.Clear();
    }

    private ResourceNodeBinding? ResolveDynamicResourceBinding(
        string nodeId,
        string itemDefinitionId)
    {
        if (!nodeId.StartsWith("surface_resource.", StringComparison.Ordinal))
        {
            return null;
        }

        GameResourceDefinition? definition = ContentCatalog.Resources.Values
            .FirstOrDefault(resource => string.Equals(
                resource.ItemDefinitionId,
                itemDefinitionId,
                StringComparison.Ordinal));
        return definition is null
            ? null
            : new ResourceNodeBinding(
                nodeId,
                itemDefinitionId,
                definition.GetDeterministicYield());
    }

    private string BuildPlanetSurfaceWorldCompositionHudLine()
    {
        if (_planetSurfaceWorldCompositionInitialized is false ||
            _planetSurfaceSkyProfile is null)
        {
            return L("ui.hud.world_composition.unavailable");
        }

        return LF(
            "ui.hud.world_composition.summary",
            ("sun", _planetSurfaceSkyProfile.StarType.ToString()),
            ("clouds", _planetSurfaceSkyProfile.CloudClusterCount),
            ("resources", _streamedSurfaceResources.Count),
            ("persistence", "seed+deltas"));
    }

    private void RunSurfacePresentationHotfixAcceptance()
    {
        PlanetSurfaceTerrainProfile? terrain = CurrentTerrainProfile;
        PlanetSurfaceSkyProfile? sky = _planetSurfaceSkyProfile;
        if (terrain is null || sky is null || _player is null)
        {
            _surfacePresentationHotfixAcceptanceHud = "FAIL unavailable";
            GD.PushError(
                "TASK-162.2 surface presentation acceptance FAIL: " +
                "reason=runtime-unavailable.");
            return;
        }

        PlanetSurfaceLogicalPosition logical =
            GetPlanetSurfaceLogicalPlayerPosition();
        double surfaceY = PlanetSurfaceTerrainRuntime.SampleHeight(
            terrain,
            logical.EastMeters,
            logical.NorthMeters);
        double clearance = logical.HeightMeters - surfaceY;
        bool relief = terrain.HeightAmplitude >= 5.0 &&
            terrain.BaseFrequency <= 0.030;
        bool distantTerrain = _planetSurfaceDistantTerrain is not null &&
            GodotObject.IsInstanceValid(_planetSurfaceDistantTerrain) &&
            _planetSurfaceDistantTerrain.Mesh is not null;
        bool visibleSun = _planetSurfaceSunVisual is not null &&
            GodotObject.IsInstanceValid(_planetSurfaceSunVisual) &&
            _planetSurfaceSunVisual.GetChildCount() >= 2;
        bool atmosphere = !sky.AtmosphereEnabled || sky.FogDensity >= 0.0045;
        const double minimumSafeClearanceMeters = 0.80;
        const double clearanceNumericToleranceMeters = 0.01;
        bool safeClearance = StageOneVoyage.Piloted ||
            clearance + clearanceNumericToleranceMeters >= minimumSafeClearanceMeters;
        bool passed = relief && distantTerrain && visibleSun &&
            atmosphere && safeClearance;

        _surfacePresentationHotfixAcceptanceHud = passed
            ? $"PASS relief={terrain.HeightAmplitude:0.0}m proxy=840m sun=1 fog={sky.FogDensity:0.0000} clearance={clearance:0.00}m"
            : $"FAIL relief={(relief ? 1 : 0)} proxy={(distantTerrain ? 1 : 0)} sun={(visibleSun ? 1 : 0)} atmosphere={(atmosphere ? 1 : 0)} clearance={clearance:0.00}m";
        string output =
            $"TASK-162.2 surface presentation acceptance {(passed ? "PASS" : "FAIL")}: " +
            $"reliefAmplitude={terrain.HeightAmplitude:0.00}m; " +
            $"baseFrequency={terrain.BaseFrequency:0.000}; " +
            $"distantProxy={(distantTerrain ? 1 : 0)}; " +
            $"proxyExtent={PlanetSurfaceDistantTerrainHalfExtentMeters * 2.0:0}m; " +
            $"streamerHole={PlanetSurfaceDistantTerrainInnerHalfExtentMeters * 2.0:0}m; " +
            $"sunVisual={(visibleSun ? 1 : 0)}; " +
            $"fogDensity={sky.FogDensity:0.0000}; " +
            $"clearance={clearance:0.00}m; " +
            $"clearanceMin={minimumSafeClearanceMeters:0.00}m; " +
            $"clearanceTol={clearanceNumericToleranceMeters:0.00}m; " +
            "boundedGameplayStreamer=25-chunks.";
        if (passed)
        {
            GD.Print(output);
        }
        else
        {
            GD.PushError(output);
        }
    }

    private static Color ToColor(PlanetEnvironmentColor color) => new(
        (float)color.R,
        (float)color.G,
        (float)color.B,
        1.0f);

    private void RunPlanetSurfaceWorldCompositionAcceptance()
    {
        PlanetSurfaceWorldCompositionAcceptanceReport report =
            PlanetSurfaceWorldCompositionAcceptanceRunner.Run(
                ContentCatalog,
                PlanetEnvironmentCatalog,
                EcologyCatalog,
                PlanetaryPoiCatalog,
                RepairRecipe,
                StationRecipes.ToArray());
        _planetSurfaceWorldCompositionAcceptanceHud = report.BuildHudLine();
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
