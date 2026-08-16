using System;
using System.Globalization;
using Godot;

public partial class SalvageRepairSlice
{
    private string _planetSurfaceTerrainAcceptanceHud = "READY";

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

        ArrayMesh mesh = BuildPlanetTerrainMesh(profile);
        StandardMaterial3D material = new()
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 0.93f
        };
        groundMesh.Mesh = mesh;
        groundMesh.MaterialOverride = material;
        groundCollision.Shape = mesh.CreateTrimeshShape();

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

    private ArrayMesh BuildPlanetTerrainMesh(PlanetSurfaceTerrainProfile profile)
    {
        int resolution = profile.Resolution;
        double size = profile.HalfExtent * 2.0;
        double step = size / (resolution - 1);
        SurfaceTool surfaceTool = new();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        for (int zIndex = 0; zIndex < resolution; zIndex++)
        {
            double z = -profile.HalfExtent + zIndex * step;
            for (int xIndex = 0; xIndex < resolution; xIndex++)
            {
                double x = -profile.HalfExtent + xIndex * step;
                PlanetSurfaceTerrainSample sample =
                    PlanetSurfaceTerrainRuntime.Sample(profile, x, z);
                Vector3 normal = SampleTerrainNormal(profile, x, z);
                surfaceTool.SetNormal(normal);
                surfaceTool.SetUV(new Vector2(
                    xIndex / (float)(resolution - 1),
                    zIndex / (float)(resolution - 1)));
                surfaceTool.SetColor(TerrainVertexColor(profile, sample));
                surfaceTool.AddVertex(new Vector3(
                    (float)x,
                    (float)sample.Height,
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
        PlanetSurfaceTerrainSample sample)
    {
        Color baseColor = BuildGroundColor(profile.Archetype);
        float heightFactor = (float)((sample.NormalizedHeight - 0.5) * 0.22);
        float slopeFactor = (float)Math.Clamp(sample.SlopeDegrees / 60.0, 0.0, 1.0);
        float multiplier = Math.Clamp(1.0f + heightFactor - slopeFactor * 0.10f, 0.65f, 1.25f);
        return new Color(
            Math.Clamp(baseColor.R * multiplier, 0.0f, 1.0f),
            Math.Clamp(baseColor.G * multiplier, 0.0f, 1.0f),
            Math.Clamp(baseColor.B * multiplier, 0.0f, 1.0f),
            1.0f);
    }

    private double SamplePlanetSurfaceHeight(double x, double z)
    {
        PlanetSurfaceTerrainProfile? profile = CurrentTerrainProfile;
        return profile is null
            ? 0.0
            : PlanetSurfaceTerrainRuntime.SampleHeight(profile, x, z);
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
        double surfaceY = SamplePlanetSurfaceHeight(
            placement.PositionX,
            placement.PositionZ);
        return placement with
        {
            PositionY = surfaceY + 0.1 + state.Definition.Size.Y / 2.0,
            Environment = placement.Environment with
            {
                Height = surfaceY,
                SlopeDegrees = SamplePlanetSurfaceSlope(
                    placement.PositionX,
                    placement.PositionZ)
            }
        };
    }

    private double FloraSurfaceY(EcologyFloraPlacement placement) =>
        SamplePlanetSurfaceHeight(placement.PositionX, placement.PositionZ) + 0.55;

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
                position.Y = (float)SamplePlanetSurfaceHeight(position.X, position.Z) + 0.70f;
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
        PlanetSurfaceTerrainSample sample = PlanetSurfaceTerrainRuntime.Sample(
            profile,
            _player?.GlobalPosition.X ?? 0.0,
            _player?.GlobalPosition.Z ?? 0.0);
        return LF(
            "ui.hud.planet_terrain.summary",
            ("archetype", LocalizeGalaxyPlanetArchetype(profile.Archetype)),
            ("height", sample.Height.ToString("0.0", CultureInfo.InvariantCulture)),
            ("slope", sample.SlopeDegrees.ToString("0.0", CultureInfo.InvariantCulture)),
            ("resolution", profile.Resolution));
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
