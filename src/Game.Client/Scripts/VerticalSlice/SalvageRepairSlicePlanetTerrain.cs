using System;
using System.Globalization;
using Godot;

public partial class SalvageRepairSlice
{
    private string _planetSurfaceTerrainAcceptanceHud = "READY";
    private string _planetSurfaceStreamingAcceptanceHud = "READY";
    private const int PlanetSurfaceDistantTerrainResolution = 49;
    private const double PlanetSurfaceDistantTerrainHalfExtentMeters = 420.0;
    private const double PlanetSurfaceDistantTerrainInnerHalfExtentMeters = 58.0;

    private TerrainChunkManager? _planetSurfaceStreamer;
    private MeshInstance3D? _planetSurfaceDistantTerrain;
    private PlanetSurfaceChunkCoordinate? _planetSurfaceDistantTerrainCenter;
    private bool _planetSurfaceFallbackRetired;
    private bool _planetSurfaceStreamingReadyPrinted;
    private bool _planetSurfaceFallbackBackfaceCollisionEnabled;
    private int _planetSurfaceStartupClearanceSamples;
    private double _planetSurfaceStartupMinimumClearanceMeters = double.PositiveInfinity;
    private string _planetSurfaceStartupSafetyAcceptanceHud = "READY";

    private PlanetSurfaceTerrainProfile? CurrentTerrainProfile =>
        _planetSurfaceContentProfile?.Terrain;

    private void ApplyPlanetSurfaceTerrain()
    {
        PlanetSurfaceTerrainProfile? profile = CurrentTerrainProfile;
        if (profile is null)
        {
            return;
        }

        MeshInstance3D? groundMesh = GetNodeOrNull<MeshInstance3D>(
            "GroundBody/MeshInstance3D");
        CollisionShape3D? groundCollision = GetNodeOrNull<CollisionShape3D>(
            "GroundBody/CollisionShape3D");
        if (groundMesh is null || groundCollision is null)
        {
            throw new InvalidOperationException(
                "TASK-156 requires GroundBody mesh and collision nodes.");
        }

        ArrayMesh mesh = BuildPlanetTerrainMesh(
            profile,
            PlanetSurfaceFrame.OriginEastMeters,
            PlanetSurfaceFrame.OriginNorthMeters);
        StandardMaterial3D material = new()
        {
            AlbedoColor = Colors.White,
            VertexColorUseAsAlbedo = true,
            Roughness = 0.94f,
            MetallicSpecular = 0.0f,
            EmissionEnabled = true,
            Emission = BuildGroundColor(profile.Archetype).Darkened(0.60f),
            EmissionEnergyMultiplier = 0.14f
        };
        groundMesh.Mesh = mesh;
        groundMesh.MaterialOverride = material;
        groundMesh.Visible = true;
        ConcavePolygonShape3D fallbackShape = mesh.CreateTrimeshShape();
        fallbackShape.BackfaceCollision = true;
        groundCollision.Shape = fallbackShape;
        groundCollision.Disabled = false;
        _planetSurfaceFallbackBackfaceCollisionEnabled = true;
        _planetSurfaceFallbackRetired = false;
        _planetSurfaceStreamingReadyPrinted = false;
        if (_planetSurfaceStreamer is not null &&
            GodotObject.IsInstanceValid(_planetSurfaceStreamer))
        {
            _planetSurfaceStreamer.Visible = false;
        }

        // TASK-174.1: cold start used to leave the authored Player Y=1.05
        // untouched. With macro relief / curved terrain that position can be
        // inside the synchronous fallback mesh, so radial gravity can pull the
        // player through the one-sided side before the async streamer settles.
        // Snap the body above the exact semantic terrain immediately, while the
        // backface-safe curved fallback collider is already live.
        ApplyPlanetSurfaceStartupClearanceGuard("terrain-bootstrap");

        EnsurePlanetSurfaceStreaming(profile);
        _planetSurfaceStreamer?.SetRuntimeCollisionEnabled(false);
        EnsurePlanetSurfaceDistantTerrain(profile, force: true);
        _npcNavigationSurface?.SetTerrainProfile(profile);
        RepositionSurfaceBoundObjects();

        (double minimum, double maximum, double maximumSlope, double walkable) =
            MeasureTerrain(profile);
        int vertices = profile.Resolution * profile.Resolution;
        int triangles = (profile.Resolution - 1) *
            (profile.Resolution - 1) * 2;
        GD.Print(
            "TASK-156 planet terrain READY: " +
            $"planet={profile.PlanetId}; archetype={profile.Archetype}; " +
            $"seed={profile.WorldSeed}; resolution={profile.Resolution}x{profile.Resolution}; " +
            $"amplitude={profile.HeightAmplitude.ToString("0.00", CultureInfo.InvariantCulture)}m; " +
            $"height={minimum.ToString("0.00", CultureInfo.InvariantCulture)}..{maximum.ToString("0.00", CultureInfo.InvariantCulture)}m; " +
            $"maxSlope={maximumSlope.ToString("0.0", CultureInfo.InvariantCulture)}deg; " +
            $"walkable={walkable.ToString("0.0", CultureInfo.InvariantCulture)}%; " +
            $"waterBasins={(profile.WaterBasinsEnabled ? 1 : 0)}; " +
            $"vertices={vertices}; triangles={triangles}; collision=trimesh; nav=heightfield.");
    }

    private void ActivateCurvedSurfaceFallbackBridge(
        double originEastMeters,
        double originNorthMeters)
    {
        PlanetSurfaceTerrainProfile? profile = CurrentTerrainProfile;
        MeshInstance3D? groundMesh = GetNodeOrNull<MeshInstance3D>(
            "GroundBody/MeshInstance3D");
        CollisionShape3D? groundCollision = GetNodeOrNull<CollisionShape3D>(
            "GroundBody/CollisionShape3D");
        if (profile is null || groundMesh is null || groundCollision is null)
        {
            return;
        }

        ArrayMesh bridgeMesh = BuildPlanetTerrainMesh(
            profile,
            originEastMeters,
            originNorthMeters);
        groundMesh.Mesh = bridgeMesh;
        groundMesh.Visible = true;
        ConcavePolygonShape3D bridgeShape = bridgeMesh.CreateTrimeshShape();
        bridgeShape.BackfaceCollision = true;
        groundCollision.Shape = bridgeShape;
        groundCollision.Disabled = false;
        _planetSurfaceFallbackBackfaceCollisionEnabled = true;
        _planetSurfaceStreamer?.SetRuntimeCollisionEnabled(false);
        _planetSurfaceFallbackRetired = false;
        _planetSurfaceStreamingReadyPrinted = false;
        _planetCurvedSurfaceReadyPrinted = false;
    }

    private void EnsurePlanetSurfaceStreaming(
        PlanetSurfaceTerrainProfile profile)
    {
        Color baseColor = BuildGroundColor(profile.Archetype);
        if (_planetSurfaceStreamer is null ||
            !GodotObject.IsInstanceValid(_planetSurfaceStreamer))
        {
            _planetSurfaceStreamer = new TerrainChunkManager
            {
                Name = "PlanetSurfaceStreamer",
                ActiveRadius = PlanetSurfaceStreamingRuntime.ActiveRadius,
                HighDetailRadius =
                    PlanetSurfaceStreamingRuntime.HighDetailRadius,
                CollisionRadius =
                    PlanetSurfaceStreamingRuntime.CollisionRadius,
                HighDetailResolution =
                    PlanetSurfaceStreamingRuntime.HighDetailResolution,
                LowDetailResolution =
                    PlanetSurfaceStreamingRuntime.LowDetailResolution,
                CollisionResolution =
                    PlanetSurfaceStreamingRuntime.CollisionResolution,
                ChunkSize =
                    (float)PlanetSurfaceStreamingRuntime.ChunkSizeMeters,
                SkirtDepth =
                    (float)PlanetSurfaceStreamingRuntime.SkirtDepthMeters,
                ChunkSwitchHysteresis =
                    (float)PlanetSurfaceStreamingRuntime.SwitchHysteresisMeters,
                OperationIntervalSeconds = 0.035f,
                MaxOperationsPerStep = 2,
                MainThreadBudgetMilliseconds = 2.0f,
                ForcedPreloadBudgetMilliseconds = 5.0f,
                LoadingScreenBudgetMilliseconds = 10.0f,
                RuntimeRefreshCoalescingEnabled = true,
                MaxCoalescedCenterLagChunks = 1,
                PlayerPath = new NodePath("../Player"),
                EnablePrototypeControls = false,
                EnablePrototypeHud = false,
                ShowWorldGrid = false,
                ShowWireframe = false,
                ShowChunkBorders = false,
                DebugViewMode = TerrainDebugViewMode.HeightAndSlope,
                VerboseGenerationLogging = false,
                Visible = false
            };
            _planetSurfaceStreamer.ConfigurePlanetSurface(profile, baseColor);
            _planetSurfaceStreamer.ConfigurePlanetSurfaceCurvature(
                PlanetSurfaceContentProfile.Environment.RadiusKm);
            _planetSurfaceStreamer.SetLogicalSurfaceOrigin(
                PlanetSurfaceFrame.OriginEastMeters,
                PlanetSurfaceFrame.OriginNorthMeters);
            AddChild(_planetSurfaceStreamer);
        }
        else
        {
            _planetSurfaceStreamer.ConfigurePlanetSurface(profile, baseColor);
            _planetSurfaceStreamer.ConfigurePlanetSurfaceCurvature(
                PlanetSurfaceContentProfile.Environment.RadiusKm);
            _planetSurfaceStreamer.SetLogicalSurfaceOrigin(
                PlanetSurfaceFrame.OriginEastMeters,
                PlanetSurfaceFrame.OriginNorthMeters);
        }
    }

    private void UpdatePlanetSurfaceStreamingObserver()
    {
        if (_planetSurfaceStreamer is null ||
            !GodotObject.IsInstanceValid(_planetSurfaceStreamer))
        {
            return;
        }

        // TASK-182: when PlanetRuntime is suspended there is no useful terrain
        // observer. Switching back to the on-foot player during the same frame
        // previously scheduled a pointless 25-create/25-remove revision just
        // before the streamer was disabled (queued=50 in the owner log).
        if (!_surfaceRuntimeActive)
        {
            return;
        }

        CharacterBody3D? observer = StageOneVoyage.Piloted &&
            _voyageShip is not null
                ? _voyageShip
                : _player;
        if (observer is not null && GodotObject.IsInstanceValid(observer))
        {
            _planetSurfaceStreamer.SetRuntimeObserver(observer);
        }
    }

    private void UpdatePlanetSurfaceStreaming()
    {
        UpdatePlanetSurfaceStreamingObserver();

        if (_surfaceRuntimeActive && !_planetSurfaceFallbackRetired)
        {
            // Keep the body on the safe side of the synchronous collider for
            // the short async handoff window as an additional cold-load/rebase
            // guard. This is idempotent once clearance is healthy.
            ApplyPlanetSurfaceStartupClearanceGuard("streamer-handoff");
        }

        if (!_surfaceRuntimeActive ||
            _planetSurfaceStreamer is null ||
            !GodotObject.IsInstanceValid(_planetSurfaceStreamer) ||
            !_planetSurfaceStreamer.IsStreamingSettled)
        {
            return;
        }

        if (!_planetSurfaceFallbackRetired)
        {
            _planetSurfaceStreamer.SetRuntimeCollisionEnabled(true);
            _planetSurfaceStreamer.Visible = true;
            MeshInstance3D? groundMesh = GetNodeOrNull<MeshInstance3D>(
                "GroundBody/MeshInstance3D");
            CollisionShape3D? groundCollision = GetNodeOrNull<CollisionShape3D>(
                "GroundBody/CollisionShape3D");
            if (groundMesh is not null)
            {
                groundMesh.Visible = false;
            }
            if (groundCollision is not null)
            {
                groundCollision.Disabled = true;
            }
            _planetSurfaceFallbackRetired = true;
        }

        if (_planetSurfaceStreamingReadyPrinted)
        {
            return;
        }

        TerrainChunkProfilerSnapshot snapshot =
            _planetSurfaceStreamer.CaptureProfilerSnapshot();
        PlanetSurfaceTerrainProfile? profile = CurrentTerrainProfile;
        if (profile is null)
        {
            return;
        }
        EnsurePlanetSurfaceDistantTerrain(profile, force: false);
        PlanetSurfaceLogicalPosition logicalPlayer =
            GetPlanetSurfaceLogicalPlayerPosition();
        PlanetSurfaceGeodesicAddress address =
            PlanetSurfaceStreamingRuntime.BuildGeodesicAddress(
                PlanetSurfaceContentProfile.Environment.RadiusKm,
                logicalPlayer.EastMeters,
                logicalPlayer.NorthMeters);
        GD.Print(
            "TASK-158 planet surface streaming READY: " +
            $"planet={profile.PlanetId}; archetype={profile.Archetype}; " +
            $"active={snapshot.LoadedChunks}/{PlanetSurfaceStreamingRuntime.ExpectedActiveChunks}; " +
            $"collisions={snapshot.Collisions}/{PlanetSurfaceStreamingRuntime.ExpectedCollisionChunks}; " +
            $"center={_planetSurfaceStreamer.CurrentChunk.X},{_planetSurfaceStreamer.CurrentChunk.Y}; " +
            $"chunk={PlanetSurfaceStreamingRuntime.ChunkSizeMeters:0}m; " +
            $"window={(PlanetSurfaceStreamingRuntime.ActiveRadius * 2 + 1)}x{(PlanetSurfaceStreamingRuntime.ActiveRadius * 2 + 1)}; " +
            $"vertices={snapshot.Vertices}; queue={snapshot.QueuedWork}; workers={snapshot.ActiveWorkers}; " +
            $"lat={address.LatitudeDegrees:0.0000}; lon={address.LongitudeDegrees:0.0000}; " +
            "lod=33/17; async=1; cancellation=1; safeUnload=1; fallback=retired.");
        _planetSurfaceStreamingReadyPrinted = true;
    }

    private void EnsurePlanetSurfaceDistantTerrain(
        PlanetSurfaceTerrainProfile profile,
        bool force)
    {
        Node3D parent = GetNodeOrNull<Node3D>("Gameplay") ?? this;
        if (_planetSurfaceDistantTerrain is null ||
            !GodotObject.IsInstanceValid(_planetSurfaceDistantTerrain))
        {
            _planetSurfaceDistantTerrain = new MeshInstance3D
            {
                Name = "PlanetSurfaceDistantTerrain"
            };
            parent.AddChild(_planetSurfaceDistantTerrain);
            _planetSurfaceDistantTerrainCenter = null;
        }

        PlanetSurfaceLogicalPosition logical =
            GetPlanetSurfaceLogicalPlayerPosition();
        PlanetSurfaceChunkCoordinate center =
            _planetSurfaceStreamer is not null &&
            GodotObject.IsInstanceValid(_planetSurfaceStreamer)
                ? new PlanetSurfaceChunkCoordinate(
                    _planetSurfaceStreamer.CurrentChunk.X,
                    _planetSurfaceStreamer.CurrentChunk.Y)
                : PlanetSurfaceStreamingRuntime.WorldToChunk(
                    logical.EastMeters,
                    logical.NorthMeters);
        if (!force && _planetSurfaceDistantTerrainCenter is { } previous &&
            previous == center)
        {
            _planetSurfaceDistantTerrain.Visible = _surfaceRuntimeActive;
            return;
        }

        double logicalCenterEast =
            center.X * PlanetSurfaceStreamingRuntime.ChunkSizeMeters;
        double logicalCenterNorth =
            center.Z * PlanetSurfaceStreamingRuntime.ChunkSizeMeters;
        _planetSurfaceDistantTerrain.Mesh = BuildPlanetDistantTerrainMesh(
            profile,
            logicalCenterEast,
            logicalCenterNorth);
        Color baseColor = BuildGroundColor(profile.Archetype);
        _planetSurfaceDistantTerrain.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            VertexColorUseAsAlbedo = true,
            Roughness = 0.96f,
            MetallicSpecular = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            EmissionEnabled = true,
            Emission = baseColor.Darkened(0.64f),
            EmissionEnergyMultiplier = 0.11f
        };
        // Child of Gameplay: logical X/Z are converted to local world space by
        // the TASK-162 Gameplay floating-origin transform automatically.
        _planetSurfaceDistantTerrain.Position = new Vector3(
            (float)logicalCenterEast,
            -0.08f,
            (float)logicalCenterNorth);
        _planetSurfaceDistantTerrain.Visible = _surfaceRuntimeActive;
        _planetSurfaceDistantTerrainCenter = center;
    }

    private ArrayMesh BuildPlanetDistantTerrainMesh(
        PlanetSurfaceTerrainProfile profile,
        double logicalCenterEast,
        double logicalCenterNorth)
    {
        const int resolution = PlanetSurfaceDistantTerrainResolution;
        double half = PlanetSurfaceDistantTerrainHalfExtentMeters;
        double step = (half * 2.0) / (resolution - 1);
        SurfaceTool surfaceTool = new();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);
        double radiusKm = _planetSurfaceContentProfile?.Environment.RadiusKm ?? 44.0;
        PlanetSurfaceCurvedPatchDescriptor curvedPatch = new(
            Math.Max(PlanetSurfaceTopologyRuntime.MinimumRadiusMeters, radiusKm * 1000.0),
            PlanetSurfaceFrame.OriginEastMeters,
            PlanetSurfaceFrame.OriginNorthMeters);

        for (int zIndex = 0; zIndex < resolution; zIndex++)
        {
            double localZ = -half + zIndex * step;
            for (int xIndex = 0; xIndex < resolution; xIndex++)
            {
                double localX = -half + xIndex * step;
                double logicalX = logicalCenterEast + localX;
                double logicalZ = logicalCenterNorth + localZ;
                PlanetSurfaceTerrainSample sample =
                    PlanetSurfaceTerrainRuntime.Sample(profile, logicalX, logicalZ);
                Vector3 curvedNormal = curvedPatch.TerrainNormalLocal(
                    profile,
                    logicalX,
                    logicalZ);
                surfaceTool.SetNormal(curvedNormal);
                surfaceTool.SetColor(TerrainVertexColor(
                    profile,
                    sample,
                    logicalX,
                    logicalZ));
                double curvatureSag = curvedPatch.TangentSagMeters(
                    logicalX,
                    logicalZ);
                surfaceTool.AddVertex(new Vector3(
                    (float)localX,
                    (float)(sample.Height - curvatureSag),
                    (float)localZ));
            }
        }

        for (int zIndex = 0; zIndex < resolution - 1; zIndex++)
        {
            double cellZ = -half + (zIndex + 0.5) * step;
            for (int xIndex = 0; xIndex < resolution - 1; xIndex++)
            {
                double cellX = -half + (xIndex + 0.5) * step;
                if (Math.Max(Math.Abs(cellX), Math.Abs(cellZ)) <
                    PlanetSurfaceDistantTerrainInnerHalfExtentMeters)
                {
                    continue;
                }

                int v00 = zIndex * resolution + xIndex;
                int v10 = v00 + 1;
                int v01 = v00 + resolution;
                int v11 = v01 + 1;
                surfaceTool.AddIndex(v00);
                surfaceTool.AddIndex(v01);
                surfaceTool.AddIndex(v11);
                surfaceTool.AddIndex(v00);
                surfaceTool.AddIndex(v11);
                surfaceTool.AddIndex(v10);
            }
        }

        return surfaceTool.Commit();
    }

    private ArrayMesh BuildPlanetTerrainMesh(
        PlanetSurfaceTerrainProfile profile,
        double logicalCenterEastMeters,
        double logicalCenterNorthMeters)
    {
        int resolution = profile.Resolution;
        double size = profile.HalfExtent * 2.0;
        double step = size / (resolution - 1);
        SurfaceTool surfaceTool = new();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);
        double radiusKm = _planetSurfaceContentProfile?.Environment.RadiusKm ?? 44.0;
        PlanetSurfaceCurvedPatchDescriptor curvedPatch = new(
            Math.Max(PlanetSurfaceTopologyRuntime.MinimumRadiusMeters, radiusKm * 1000.0),
            logicalCenterEastMeters,
            logicalCenterNorthMeters);

        for (int zIndex = 0; zIndex < resolution; zIndex++)
        {
            double z = -profile.HalfExtent + zIndex * step;
            for (int xIndex = 0; xIndex < resolution; xIndex++)
            {
                double x = -profile.HalfExtent + xIndex * step;
                double logicalX = logicalCenterEastMeters + x;
                double logicalZ = logicalCenterNorthMeters + z;
                PlanetSurfaceTerrainSample sample =
                    PlanetSurfaceTerrainRuntime.Sample(profile, logicalX, logicalZ);
                Vector3 normal = curvedPatch.TerrainNormalLocal(
                    profile,
                    logicalX,
                    logicalZ);
                surfaceTool.SetNormal(normal);
                surfaceTool.SetUV(new Vector2(
                    xIndex / (float)(resolution - 1),
                    zIndex / (float)(resolution - 1)));
                surfaceTool.SetColor(TerrainVertexColor(
                    profile,
                    sample,
                    logicalX,
                    logicalZ));
                surfaceTool.AddVertex(new Vector3(
                    (float)x,
                    (float)(sample.Height - curvedPatch.TangentSagMeters(
                        logicalX,
                        logicalZ)),
                    (float)z));
            }
        }

        for (int zIndex = 0; zIndex < resolution - 1; zIndex++)
        {
            for (int xIndex = 0; xIndex < resolution - 1; xIndex++)
            {
                int v00 = zIndex * resolution + xIndex;
                int v10 = v00 + 1;
                int v01 = v00 + resolution;
                int v11 = v01 + 1;
                surfaceTool.AddIndex(v00);
                surfaceTool.AddIndex(v01);
                surfaceTool.AddIndex(v11);
                surfaceTool.AddIndex(v00);
                surfaceTool.AddIndex(v11);
                surfaceTool.AddIndex(v10);
            }
        }

        return surfaceTool.Commit();
    }

    private static Vector3 SampleTerrainNormal(
        PlanetSurfaceTerrainProfile profile,
        double x,
        double z)
    {
        const double sampleStep = 0.35;
        double left = PlanetSurfaceTerrainRuntime.SampleHeight(
            profile, x - sampleStep, z);
        double right = PlanetSurfaceTerrainRuntime.SampleHeight(
            profile, x + sampleStep, z);
        double back = PlanetSurfaceTerrainRuntime.SampleHeight(
            profile, x, z - sampleStep);
        double front = PlanetSurfaceTerrainRuntime.SampleHeight(
            profile, x, z + sampleStep);
        return new Vector3(
            (float)(left - right),
            (float)(2.0 * sampleStep),
            (float)(back - front)).Normalized();
    }

    private static Color TerrainVertexColor(
        PlanetSurfaceTerrainProfile profile,
        PlanetSurfaceTerrainSample sample,
        double logicalX,
        double logicalZ)
    {
        Color baseColor = BuildGroundColor(profile.Archetype);
        float heightFactor = (float)((sample.NormalizedHeight - 0.5) * 0.26);
        float slopeFactor = (float)Math.Clamp(sample.SlopeDegrees / 60.0, 0.0, 1.0);
        double broad = Math.Sin(logicalX * 0.037 + Math.Cos(logicalZ * 0.029) * 1.7);
        double detail = Math.Sin(logicalX * 0.19 + logicalZ * 0.13) *
            Math.Cos(logicalZ * 0.11 - logicalX * 0.07);
        float proceduralTexture = (float)(broad * 0.055 + detail * 0.035);
        float multiplier = Math.Clamp(
            1.0f + heightFactor + proceduralTexture - slopeFactor * 0.11f,
            0.58f,
            1.32f);
        Color color = new(
            Math.Clamp(baseColor.R * multiplier, 0.0f, 1.0f),
            Math.Clamp(baseColor.G * multiplier, 0.0f, 1.0f),
            Math.Clamp(baseColor.B * multiplier, 0.0f, 1.0f),
            1.0f);
        Color mineral = new Color(0.34f, 0.32f, 0.30f, 1.0f)
            .Lerp(baseColor, 0.34f);
        float mineralBlend = Mathf.SmoothStep(0.18f, 0.62f, slopeFactor);
        return color.Lerp(mineral, mineralBlend * 0.58f);
    }

    private double ApplyPlanetSurfaceStartupClearanceGuard(string source)
    {
        double clearance = EnsurePlayerAbovePlanetSurfaceFloor();
        if (!double.IsFinite(clearance))
        {
            return clearance;
        }

        _planetSurfaceStartupClearanceSamples++;
        _planetSurfaceStartupMinimumClearanceMeters = Math.Min(
            _planetSurfaceStartupMinimumClearanceMeters,
            clearance);
        if (_planetSurfaceStartupClearanceSamples == 1)
        {
            GD.Print(
                "TASK-174.1 curved surface cold-start guard READY: " +
                $"source={source}; clearance={clearance.ToString("0.000", CultureInfo.InvariantCulture)}m; " +
                $"fallbackBackface={(_planetSurfaceFallbackBackfaceCollisionEnabled ? 1 : 0)}; " +
                "release=after-synchronous-curved-collider.");
        }
        return clearance;
    }

    private void RunPlanetSurfaceStartupSafetyAcceptance()
    {
        double clearance = EnsurePlayerAbovePlanetSurfaceFloor();
        bool currentSafe = double.IsNaN(clearance) || clearance >= 0.79;
        bool guarded = _planetSurfaceStartupClearanceSamples > 0 &&
            double.IsFinite(_planetSurfaceStartupMinimumClearanceMeters) &&
            _planetSurfaceStartupMinimumClearanceMeters >= 0.79;
        TerrainChunkProfilerSnapshot? snapshot =
            _planetSurfaceStreamer is not null && GodotObject.IsInstanceValid(_planetSurfaceStreamer)
                ? _planetSurfaceStreamer.CaptureProfilerSnapshot()
                : null;
        bool streamerSafe = snapshot is not null &&
            snapshot.LoadedChunks == PlanetSurfaceStreamingRuntime.ExpectedActiveChunks &&
            snapshot.Collisions == PlanetSurfaceStreamingRuntime.ExpectedCollisionChunks;
        bool passed = _planetSurfaceFallbackBackfaceCollisionEnabled &&
            guarded && currentSafe && streamerSafe;

        _planetSurfaceStartupSafetyAcceptanceHud = passed
            ? "PASS cold-start collider/clearance"
            : "FAIL cold-start safety";
        string output =
            $"TASK-174.1 curved surface cold-start safety acceptance {(passed ? "PASS" : "FAIL")}: " +
            $"fallbackBackface={(_planetSurfaceFallbackBackfaceCollisionEnabled ? 1 : 0)}; " +
            $"guardSamples={_planetSurfaceStartupClearanceSamples}; " +
            $"minGuardClearance={(_planetSurfaceStartupClearanceSamples > 0 ? _planetSurfaceStartupMinimumClearanceMeters.ToString("0.000", CultureInfo.InvariantCulture) : "n/a")}m; " +
            $"currentClearance={(double.IsNaN(clearance) ? "piloted" : clearance.ToString("0.000", CultureInfo.InvariantCulture))}m; " +
            $"streamer={(streamerSafe ? "25/9" : "not-ready")}; " +
            "coldStart=spawn-after-curved-fallback.";
        if (passed) GD.Print(output); else GD.PushError(output);
    }

    private double EnsurePlayerAbovePlanetSurfaceFloor()
    {
        PlanetSurfaceTerrainProfile? profile = CurrentTerrainProfile;
        if (_player is null || profile is null || StageOneVoyage.Piloted)
        {
            return double.NaN;
        }

        PlanetSurfaceLogicalPosition logical =
            GetPlanetSurfaceLogicalPlayerPosition();
        double terrainHeight = PlanetSurfaceTerrainRuntime.SampleHeight(
            profile,
            logical.EastMeters,
            logical.NorthMeters);
        double minimumY = PlanetSurfaceSpawnSafetyRuntime.RequiredSemanticHeight(
            profile,
            logical.EastMeters,
            logical.NorthMeters,
            logical.HeightMeters);
        if (logical.HeightMeters < minimumY)
        {
            _player.GlobalPosition = SurfaceLogicalToLocalPosition(
                logical.EastMeters,
                minimumY,
                logical.NorthMeters);
            Vector3 localVelocity = SurfaceWorldDirectionToLocal(_player.Velocity);
            localVelocity.Y = Math.Max(0.0f, localVelocity.Y);
            _player.Velocity = SurfaceLocalDirectionToWorld(localVelocity);
            logical = logical with { HeightMeters = minimumY };
        }

        return logical.HeightMeters - terrainHeight;
    }

    private double SamplePlanetSurfaceHeight(double x, double z)
    {
        PlanetSurfaceTerrainProfile? profile = CurrentTerrainProfile;
        return profile is null
            ? 0.0
            : PlanetSurfaceTerrainRuntime.SampleHeight(profile, x, z);
    }

    private double SamplePlanetSurfacePhysicalHeight(double x, double z)
    {
        double semanticHeight = SamplePlanetSurfaceHeight(x, z);
        double sag = CurrentPlanetSurfaceCurvedPatch?.TangentSagMeters(x, z) ?? 0.0;
        return semanticHeight - sag;
    }

    private double SamplePlanetSurfaceSlope(double x, double z)
    {
        PlanetSurfaceTerrainProfile? profile = CurrentTerrainProfile;
        return profile is null
            ? 0.0
            : PlanetSurfaceTerrainRuntime.Sample(profile, x, z).SlopeDegrees;
    }

    private PlanetaryPoiPlacement ProjectPoiPlacementToTerrain(
        PlanetaryPoiRuntimeState state)
    {
        PlanetaryPoiPlacement placement = state.Placement;
        double worldX = placement.PositionX;
        double worldZ = placement.PositionZ;
        if (_planetSurfaceWorldCompositionInitialized &&
            _planetSurfaceContentProfile is not null)
        {
            (worldX, worldZ) =
                PlanetSurfaceWorldCompositionRuntime.BuildPoiPresentationPosition(
                    PlanetSurfaceContentProfile,
                    placement.InstanceId);
        }
        double surfaceY = SamplePlanetSurfaceHeight(worldX, worldZ);
        return placement with
        {
            PositionX = worldX,
            PositionY = SamplePlanetSurfacePhysicalHeight(worldX, worldZ) +
                0.1 + state.Definition.Size.Y / 2.0,
            PositionZ = worldZ,
            Environment = placement.Environment with
            {
                Height = surfaceY,
                SlopeDegrees = SamplePlanetSurfaceSlope(worldX, worldZ)
            }
        };
    }

    private double FloraSurfaceY(EcologyFloraPlacement placement) =>
        SamplePlanetSurfacePhysicalHeight(placement.PositionX, placement.PositionZ) + 0.55;

    private void RepositionSurfaceBoundObjects()
    {
        if (CurrentTerrainProfile is null)
        {
            return;
        }

        foreach (Node node in GetTree().GetNodesInGroup("vertical_slice_resource"))
        {
            if (node is Node3D resource)
            {
                Vector3 position = resource.Position;
                position.Y = (float)SamplePlanetSurfacePhysicalHeight(position.X, position.Z) + 0.70f;
                resource.Position = position;
            }
        }
    }

    private static (double Minimum, double Maximum, double MaximumSlope, double WalkablePercent)
        MeasureTerrain(PlanetSurfaceTerrainProfile profile)
    {
        double minimum = double.PositiveInfinity;
        double maximum = double.NegativeInfinity;
        double maximumSlope = 0.0;
        int walkable = 0;
        int samples = 0;
        for (double z = -profile.HalfExtent; z <= profile.HalfExtent; z += 2.5)
        {
            for (double x = -profile.HalfExtent; x <= profile.HalfExtent; x += 2.5)
            {
                PlanetSurfaceTerrainSample sample = PlanetSurfaceTerrainRuntime.Sample(profile, x, z);
                minimum = Math.Min(minimum, sample.Height);
                maximum = Math.Max(maximum, sample.Height);
                maximumSlope = Math.Max(maximumSlope, sample.SlopeDegrees);
                if (sample.SlopeDegrees <= profile.MaximumWalkableSlopeDegrees)
                {
                    walkable++;
                }
                samples++;
            }
        }
        return (minimum, maximum, maximumSlope, samples == 0 ? 0.0 : walkable * 100.0 / samples);
    }

    private string BuildPlanetTerrainHudLine()
    {
        PlanetSurfaceTerrainProfile? profile = CurrentTerrainProfile;
        if (profile is null)
        {
            return L("ui.hud.planet_terrain.unavailable");
        }
        PlanetSurfaceLogicalPosition logicalPlayer =
            GetPlanetSurfaceLogicalPlayerPosition();
        PlanetSurfaceTerrainSample sample = PlanetSurfaceTerrainRuntime.Sample(
            profile,
            logicalPlayer.EastMeters,
            logicalPlayer.NorthMeters);
        return LF(
            "ui.hud.planet_terrain.summary",
            ("archetype", LocalizeGalaxyPlanetArchetype(profile.Archetype)),
            ("height", sample.Height.ToString("0.0", CultureInfo.InvariantCulture)),
            ("slope", sample.SlopeDegrees.ToString("0.0", CultureInfo.InvariantCulture)),
            ("resolution", profile.Resolution));
    }

    private string BuildPlanetSurfaceStreamingHudLine()
    {
        if (_planetSurfaceStreamer is null ||
            CurrentTerrainProfile is null)
        {
            return L("ui.hud.planet_streaming.unavailable");
        }

        TerrainChunkProfilerSnapshot snapshot =
            _planetSurfaceStreamer.CaptureProfilerSnapshot();
        PlanetSurfaceLogicalPosition logicalPlayer =
            GetPlanetSurfaceLogicalPlayerPosition();
        PlanetSurfaceGeodesicAddress address =
            PlanetSurfaceStreamingRuntime.BuildGeodesicAddress(
                PlanetSurfaceContentProfile.Environment.RadiusKm,
                logicalPlayer.EastMeters,
                logicalPlayer.NorthMeters);
        return LF(
            "ui.hud.planet_streaming.summary",
            ("loaded", snapshot.LoadedChunks),
            ("target", PlanetSurfaceStreamingRuntime.ExpectedActiveChunks),
            ("collisions", snapshot.Collisions),
            ("queue", snapshot.QueuedWork),
            ("chunkX", _planetSurfaceStreamer.CurrentChunk.X),
            ("chunkZ", _planetSurfaceStreamer.CurrentChunk.Y),
            ("lat", address.LatitudeDegrees.ToString("0.000", CultureInfo.InvariantCulture)),
            ("lon", address.LongitudeDegrees.ToString("0.000", CultureInfo.InvariantCulture)));
    }

    private void RunPlanetSurfaceStreamingAcceptance()
    {
        PlanetSurfaceStreamingAcceptanceReport report =
            PlanetSurfaceStreamingAcceptanceRunner.Run(
                PlanetEnvironmentCatalog,
                EcologyCatalog,
                PlanetaryPoiCatalog);
        _planetSurfaceStreamingAcceptanceHud = report.BuildHudLine();
        if (report.Passed)
        {
            GD.Print(report.BuildOutputLine());
        }
        else
        {
            GD.PushError(report.BuildOutputLine());
        }
    }

    private void RunPlanetSurfaceTerrainAcceptance()
    {
        PlanetSurfaceTerrainAcceptanceReport report =
            PlanetSurfaceTerrainAcceptanceRunner.Run(
                PlanetEnvironmentCatalog,
                EcologyCatalog,
                PlanetaryPoiCatalog);
        _planetSurfaceTerrainAcceptanceHud = report.BuildHudLine();
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
