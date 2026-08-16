using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private bool _planetSurfaceWorldCompositionInitialized;
    private PlanetSurfaceSkyProfile? _planetSurfaceSkyProfile;
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
        ClearStreamedSurfaceResources();
        _lastSurfaceResourceCenter = null;
        _planetSurfaceWorldCompositionReadyPrinted = false;
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
        skyMaterial.Set("sky_top_color", top);
        skyMaterial.Set("sky_horizon_color", horizon);
        skyMaterial.Set("ground_bottom_color", groundHorizon.Darkened(0.62f));
        skyMaterial.Set("ground_horizon_color", groundHorizon);
        skyMaterial.Set("sun_angle_max", 7.0f);
        skyMaterial.Set("sun_curve", 0.045f);
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
        environment.Set("fog_height", 18.0f);
        environment.Set("fog_height_density", 0.025f);

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
        sun.ShadowEnabled = true;
        sun.Set("sky_mode", 0); // LIGHT_AND_SKY: expose the star to ProceduralSkyMaterial.
        sun.Set("light_angular_distance", (float)profile.SunAngularDiameterDegrees);
        sun.Set("directional_shadow_max_distance", 320.0f);
        sun.Set("directional_shadow_fade_start", 0.82f);
        Vector3 lightRay = -towardSun;
        sun.Position = Vector3.Zero;
        sun.LookAt(lightRay, Vector3.Up);
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
            Scale = Vector3.One * 2.0f
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
            Scale = Vector3.One * 3.6f
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

        _planetSurfaceSunVisual.Visible = _surfaceRuntimeActive;
        if (!_surfaceRuntimeActive || _player is null)
        {
            return;
        }

        // Follow the local floating-origin frame while preserving a fixed
        // planet-sky direction. Keep the disc close enough that atmospheric fog
        // cannot erase it, while its ~1.3 degree angular size still reads as a
        // celestial object rather than nearby scenery.
        _planetSurfaceSunVisual.GlobalPosition =
            _player.GlobalPosition + _planetSurfaceSunDirection * 180.0f;
    }

    private void RebuildPlanetSurfaceClouds(PlanetSurfaceSkyProfile profile)
    {
        if (_planetSurfaceCloudRoot is null)
        {
            return;
        }

        foreach (Node child in _planetSurfaceCloudRoot.GetChildren())
        {
            child.QueueFree();
        }
        _planetSurfaceCloudRoot.Visible =
            _surfaceRuntimeActive && profile.CloudClusterCount > 0;
        if (profile.CloudClusterCount <= 0)
        {
            return;
        }

        RandomNumberGenerator random = new()
        {
            Seed = unchecked((ulong)profile.Seed) ^ 0xC10D5EEDUL
        };
        Color cloudColor = ToColor(profile.SkyHorizonColor)
            .Lightened(0.62f);
        StandardMaterial3D cloudMaterial = new()
        {
            AlbedoColor = new Color(
                cloudColor.R,
                cloudColor.G,
                cloudColor.B,
                (float)profile.CloudOpacity),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NoDepthTest = false
        };

        for (int clusterIndex = 0;
             clusterIndex < profile.CloudClusterCount;
             clusterIndex++)
        {
            Node3D cluster = new()
            {
                Name = $"CloudCluster_{clusterIndex:00}",
                Position = new Vector3(
                    random.RandfRange(-260.0f, 260.0f),
                    random.RandfRange(105.0f, 165.0f),
                    random.RandfRange(-260.0f, 260.0f)),
                RotationDegrees = new Vector3(
                    0.0f,
                    random.RandfRange(0.0f, 360.0f),
                    0.0f)
            };
            _planetSurfaceCloudRoot.AddChild(cluster);
            int lobes = random.RandiRange(3, 6);
            for (int lobeIndex = 0; lobeIndex < lobes; lobeIndex++)
            {
                SphereMesh mesh = new()
                {
                    Radius = 1.0f,
                    Height = 2.0f,
                    RadialSegments = 12,
                    Rings = 6
                };
                MeshInstance3D lobe = new()
                {
                    Name = $"Lobe_{lobeIndex:00}",
                    Mesh = mesh,
                    MaterialOverride = cloudMaterial,
                    Position = new Vector3(
                        random.RandfRange(-20.0f, 20.0f),
                        random.RandfRange(-1.4f, 1.4f),
                        random.RandfRange(-10.0f, 10.0f)),
                    Scale = new Vector3(
                        random.RandfRange(12.0f, 28.0f),
                        random.RandfRange(1.4f, 3.2f),
                        random.RandfRange(8.0f, 20.0f))
                };
                cluster.AddChild(lobe);
            }
        }
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
            _planetSurfaceCloudRoot.Visible =
                _surfaceRuntimeActive &&
                (_planetSurfaceSkyProfile?.CloudClusterCount ?? 0) > 0;
            if (_surfaceRuntimeActive && _player is not null)
            {
                _planetSurfaceCloudDrift += delta * 0.55;
                _planetSurfaceCloudRoot.GlobalPosition = new Vector3(
                    _player.GlobalPosition.X +
                        (float)Math.Sin(_planetSurfaceCloudDrift * 0.007) * 8.0f,
                    0.0f,
                    _player.GlobalPosition.Z +
                        (float)Math.Cos(_planetSurfaceCloudDrift * 0.006) * 6.0f);
            }
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
                out GameResourceDefinition definition))
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
                (float)placement.PositionY + 0.38f,
                (float)placement.PositionZ),
            RotationDegrees = new Vector3(
                0.0f,
                (float)placement.RotationDegrees,
                0.0f),
            Scale = Vector3.One * (float)placement.Scale
        };
        node.AddToGroup("interactable");
        node.AddToGroup("planet_surface_resource");

        MeshInstance3D meshInstance = new()
        {
            Name = "MeshInstance3D",
            Mesh = new SphereMesh
            {
                Radius = 0.62f,
                Height = 1.10f,
                RadialSegments = 10,
                Rings = 6
            },
            Scale = new Vector3(1.0f, 0.76f, 0.88f)
        };
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
        double clearance = _player.GlobalPosition.Y - surfaceY;
        bool relief = terrain.HeightAmplitude >= 5.0 &&
            terrain.BaseFrequency <= 0.030;
        bool distantTerrain = _planetSurfaceDistantTerrain is not null &&
            GodotObject.IsInstanceValid(_planetSurfaceDistantTerrain) &&
            _planetSurfaceDistantTerrain.Mesh is not null;
        bool visibleSun = _planetSurfaceSunVisual is not null &&
            GodotObject.IsInstanceValid(_planetSurfaceSunVisual) &&
            _planetSurfaceSunVisual.GetChildCount() >= 2;
        bool atmosphere = !sky.AtmosphereEnabled || sky.FogDensity >= 0.0045;
        bool safeClearance = StageOneVoyage.Piloted || clearance >= 0.80;
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
