using System;
using System.Collections.Generic;
using Godot;

public partial class PlanetaryCaveExitNode : StaticBody3D, IInteractable
{
    public string CaveInstanceId { get; private set; } = string.Empty;

    public void Configure(string caveInstanceId, Color accent)
    {
        CaveInstanceId = caveInstanceId;
        Name = "CaveExitPortal";
        CollisionLayer = 1u;
        CollisionMask = 0u;
        AddToGroup("interactable");
        StandardMaterial3D material = new()
        {
            AlbedoColor = new Color(0.08f, 0.10f, 0.12f),
            EmissionEnabled = true,
            Emission = new Color(accent.R * 0.55f, accent.G * 0.55f, accent.B * 0.55f),
            EmissionEnergyMultiplier = 1.35f,
            Metallic = 0.15f,
            Roughness = 0.58f
        };
        AddChild(new MeshInstance3D
        {
            Name = "ExitMarker",
            Mesh = new CylinderMesh
            {
                Material = material,
                TopRadius = 0.72f,
                BottomRadius = 0.84f,
                Height = 0.18f,
                RadialSegments = 16
            }
        });
        AddChild(new CollisionShape3D
        {
            Name = "CollisionShape3D",
            Shape = new CylinderShape3D
            {
                Radius = 0.92f,
                Height = 1.8f
            },
            Position = new Vector3(0.0f, 0.82f, 0.0f)
        });
    }

    public void Interact(Node3D interactor)
    {
        if (GetTree().CurrentScene is SalvageRepairSlice slice)
        {
            slice.TryExitPlanetaryCave(CaveInstanceId, interactor);
        }
    }
}

public partial class PlanetaryCavePrefabNode : Node3D
{
    private readonly List<CollisionShape3D> _collisionShapes = new();
    private readonly List<SalvageResourceNode> _deposits = new();
    private PlanetaryCavePlan? _plan;
    private Node3D? _interiorRoot;
    private PlanetaryCaveExitNode? _exitPortal;

    public PlanetaryCavePlan Plan => _plan ??
        throw new InvalidOperationException("Cave prefab has not been configured.");

    public IReadOnlyList<SalvageResourceNode> Deposits => _deposits;

    public int CollisionShapeCount => _collisionShapes.Count;

    public bool EntryExitReady => _exitPortal is not null;

    public Vector3 EntryWorldPosition => _interiorRoot is null
        ? GlobalPosition
        : _interiorRoot.ToGlobal(new Vector3(
            (float)Plan.EntryLocalX,
            (float)Plan.EntryLocalY,
            (float)Plan.EntryLocalZ));

    public void Configure(
        PlanetaryCavePlan plan,
        IReadOnlyDictionary<string, GameResourceDefinition> resources)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(resources);
        if (!PlanetaryCaveRuntime.ValidatePlan(plan, resources, out string failure))
        {
            throw new InvalidOperationException(
                $"Invalid planetary cave plan: {failure}.");
        }
        _plan = plan;
        Name = plan.CaveInstanceId.Replace('.', '_');
        AddToGroup("planetary_cave_prefab");

        Color accent = new(
            (float)plan.Archetype.AccentR,
            (float)plan.Archetype.AccentG,
            (float)plan.Archetype.AccentB);
        StandardMaterial3D rock = new()
        {
            AlbedoColor = new Color(0.095f, 0.105f, 0.115f),
            Metallic = 0.04f,
            Roughness = 0.93f
        };
        StandardMaterial3D rockSecondary = new()
        {
            AlbedoColor = new Color(0.14f, 0.135f, 0.13f),
            Metallic = 0.07f,
            Roughness = 0.88f
        };
        StandardMaterial3D accentMaterial = new()
        {
            AlbedoColor = new Color(accent.R * 0.34f, accent.G * 0.34f, accent.B * 0.34f),
            EmissionEnabled = true,
            Emission = accent,
            EmissionEnergyMultiplier = 1.15f,
            Metallic = 0.08f,
            Roughness = 0.46f
        };

        Node3D interior = new()
        {
            Name = "PrefabInterior",
            Position = new Vector3(
                0.0f,
                -(float)plan.Archetype.InteriorDepthMeters,
                0.0f)
        };
        _interiorRoot = interior;
        AddChild(interior);
        BuildWalkableShell(interior, plan, rock, rockSecondary);
        AddArchetypeDetails(interior, plan, accentMaterial, rockSecondary);
        BuildLighting(interior, plan, accent);
        BuildDeposits(interior, plan, resources);

        _exitPortal = new PlanetaryCaveExitNode
        {
            Position = new Vector3(
                (float)plan.ExitLocalX,
                (float)plan.ExitLocalY,
                (float)plan.ExitLocalZ)
        };
        _exitPortal.Configure(plan.CaveInstanceId, accent);
        interior.AddChild(_exitPortal);
        SetRuntimeActive(false);
    }

    public void SetRuntimeActive(bool active)
    {
        Visible = active;
        ProcessMode = active
            ? ProcessModeEnum.Inherit
            : ProcessModeEnum.Disabled;
        foreach (CollisionShape3D collision in _collisionShapes)
        {
            collision.SetDeferred("disabled", !active);
        }
        if (_exitPortal?.GetNodeOrNull<CollisionShape3D>("CollisionShape3D") is { } exitCollision)
        {
            exitCollision.SetDeferred("disabled", !active);
        }
        foreach (SalvageResourceNode deposit in _deposits)
        {
            deposit.SetRuntimeSuppressed(!active);
        }
    }

    private void BuildWalkableShell(
        Node3D interior,
        PlanetaryCavePlan plan,
        Material rock,
        Material secondary)
    {
        float length = (float)plan.Archetype.CorridorLengthMeters;
        float centerZ = -length * 0.5f + 1.5f;
        AddRockBlock(interior, "Floor", new Vector3(0, -0.45f, centerZ), new Vector3(7.2f, 0.9f, length + 6.0f), secondary, true);
        AddRockBlock(interior, "Ceiling", new Vector3(0, 4.05f, centerZ), new Vector3(7.4f, 1.2f, length + 6.0f), rock, true);
        AddRockBlock(interior, "WallLeft", new Vector3(-3.55f, 1.72f, centerZ), new Vector3(0.9f, 4.7f, length + 6.0f), rock, true);
        AddRockBlock(interior, "WallRight", new Vector3(3.55f, 1.72f, centerZ), new Vector3(0.9f, 4.7f, length + 6.0f), rock, true);
        AddRockBlock(interior, "BackWall", new Vector3(0, 1.65f, -length - 1.2f), new Vector3(7.2f, 4.6f, 0.9f), rock, true);

        for (int index = 0; index < 6; index++)
        {
            float z = -3.0f - index * Math.Max(4.0f, length / 7.0f);
            float side = index % 2 == 0 ? -1.0f : 1.0f;
            AddRockBlock(
                interior,
                $"Rib{index}",
                new Vector3(side * 2.65f, 1.45f, z),
                new Vector3(1.15f, 3.6f, 1.35f),
                secondary,
                true,
                new Vector3(0.0f, side * 0.16f, side * 0.08f));
        }

        float chamberZ = -length + 4.8f;
        AddRockBlock(interior, "ChamberFloor", new Vector3(0, -0.42f, chamberZ), new Vector3(10.0f, 0.84f, 8.8f), secondary, true);
        AddRockBlock(interior, "ChamberCeiling", new Vector3(0, 5.15f, chamberZ), new Vector3(10.0f, 1.25f, 8.8f), rock, true);
        AddRockBlock(interior, "ChamberWallL", new Vector3(-4.8f, 2.0f, chamberZ), new Vector3(1.0f, 5.3f, 8.8f), rock, true);
        AddRockBlock(interior, "ChamberWallR", new Vector3(4.8f, 2.0f, chamberZ), new Vector3(1.0f, 5.3f, 8.8f), rock, true);
    }

    private void AddArchetypeDetails(
        Node3D interior,
        PlanetaryCavePlan plan,
        Material accent,
        Material rock)
    {
        float length = (float)plan.Archetype.CorridorLengthMeters;
        for (int index = 0; index < 7; index++)
        {
            float side = index % 2 == 0 ? -1.0f : 1.0f;
            Mesh mesh = plan.Archetype.ArchetypeId switch
            {
                "cave.crystal_grotto" => new CylinderMesh
                {
                    Material = accent,
                    TopRadius = 0.03f,
                    BottomRadius = 0.24f + index * 0.012f,
                    Height = 1.25f + (index % 3) * 0.28f,
                    RadialSegments = 6
                },
                "cave.hydrothermal_hollow" => new CylinderMesh
                {
                    Material = accent,
                    TopRadius = 0.12f,
                    BottomRadius = 0.28f,
                    Height = 1.1f + (index % 3) * 0.35f,
                    RadialSegments = 8
                },
                _ => new BoxMesh
                {
                    Material = rock,
                    Size = new Vector3(0.65f, 1.4f + (index % 2) * 0.4f, 0.65f)
                }
            };
            interior.AddChild(new MeshInstance3D
            {
                Name = $"ArchetypeDetail{index}",
                Mesh = mesh,
                Position = new Vector3(
                    side * (2.5f + (index % 3) * 0.35f),
                    0.35f,
                    -4.0f - index * Math.Max(3.2f, length / 9.0f)),
                Rotation = new Vector3(side * 0.12f, index * 0.37f, side * 0.08f)
            });
        }
    }

    private void BuildLighting(
        Node3D interior,
        PlanetaryCavePlan plan,
        Color accent)
    {
        float length = (float)plan.Archetype.CorridorLengthMeters;
        for (int index = 0; index < 3; index++)
        {
            interior.AddChild(new OmniLight3D
            {
                Name = $"CaveLight{index}",
                Position = new Vector3(
                    index == 1 ? 1.5f : -1.4f,
                    2.55f,
                    -5.5f - index * (length / 3.2f)),
                LightColor = accent.Lerp(new Color(0.70f, 0.76f, 0.82f), 0.48f),
                LightEnergy = 0.72f,
                OmniRange = 11.0f,
                ShadowEnabled = false
            });
        }
    }

    private void BuildDeposits(
        Node3D interior,
        PlanetaryCavePlan plan,
        IReadOnlyDictionary<string, GameResourceDefinition> resources)
    {
        foreach (PlanetaryCaveDepositPlan depositPlan in plan.Deposits)
        {
            GameResourceDefinition definition = resources[depositPlan.ResourceDefinitionId];
            SalvageResourceNode node = new()
            {
                Name = depositPlan.DepositId.Replace('.', '_'),
                ResourceNodeId = depositPlan.DepositId,
                ResourceDefinitionId = depositPlan.ResourceDefinitionId,
                Position = new Vector3(
                    (float)depositPlan.LocalX,
                    (float)depositPlan.LocalY,
                    (float)depositPlan.LocalZ),
                RotationDegrees = new Vector3(
                    0.0f,
                    (float)depositPlan.RotationDegrees,
                    0.0f)
            };
            node.AddChild(ProceduralSurfaceVisualFactory.CreateResourceVisual(definition));
            node.AddChild(new CollisionShape3D
            {
                Name = "CollisionShape3D",
                Shape = new SphereShape3D { Radius = 0.74f }
            });
            node.ConfigureDefinition(definition);
            interior.AddChild(node);
            node.AddToGroup("interactable");
            node.AddToGroup("cave_resource_deposit");
            _deposits.Add(node);
        }
    }

    private void AddRockBlock(
        Node3D parent,
        string name,
        Vector3 position,
        Vector3 size,
        Material material,
        bool collision,
        Vector3? rotation = null)
    {
        MeshInstance3D mesh = new()
        {
            Name = name + "Mesh",
            Mesh = new BoxMesh
            {
                Material = material,
                Size = size
            },
            Position = position,
            Rotation = rotation ?? Vector3.Zero
        };
        parent.AddChild(mesh);
        if (!collision)
        {
            return;
        }
        StaticBody3D body = new()
        {
            Name = name + "Body",
            Position = position,
            Rotation = rotation ?? Vector3.Zero,
            CollisionLayer = 1u,
            CollisionMask = 1u
        };
        CollisionShape3D shape = new()
        {
            Name = name + "Collision",
            Shape = new BoxShape3D { Size = size }
        };
        body.AddChild(shape);
        parent.AddChild(body);
        _collisionShapes.Add(shape);
    }
}
