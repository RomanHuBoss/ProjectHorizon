using System;
using Godot;

public partial class PlanetaryPoiNode : StaticBody3D, IInteractable
{
    private PlanetaryPoiDefinition? _definition;
    private MeshInstance3D? _meshInstance;
    private StandardMaterial3D? _material;
    private CollisionShape3D? _collisionShape;

    public string InstanceId { get; private set; } = string.Empty;

    public string PoiTypeId { get; private set; } = string.Empty;

    public double ScanRange => _definition?.ScanRange ?? 0.0;

    public void Configure(
        PlanetaryPoiDefinition definition,
        PlanetaryPoiPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(placement);
        if (!string.Equals(
            definition.PoiTypeId,
            placement.PoiTypeId,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "POI definition does not match placement type.");
        }

        _definition = definition;
        InstanceId = placement.InstanceId;
        PoiTypeId = placement.PoiTypeId;
        Name = placement.InstanceId.Replace('.', '_');
        Position = new Vector3(
            (float)placement.PositionX,
            (float)placement.PositionY,
            (float)placement.PositionZ);
        Rotation = new Vector3(
            0.0f,
            Mathf.DegToRad((float)placement.RotationDegrees),
            0.0f);
        CollisionLayer = 1u;
        CollisionMask = 0u;
        AddToGroup("interactable");
        AddToGroup("planetary_poi");

        StandardMaterial3D material = new()
        {
            AlbedoColor = new Color(
                (float)definition.Color.R * 0.34f,
                (float)definition.Color.G * 0.34f,
                (float)definition.Color.B * 0.34f),
            EmissionEnabled = true,
            Emission = new Color(
                (float)definition.Color.R * 0.08f,
                (float)definition.Color.G * 0.08f,
                (float)definition.Color.B * 0.08f),
            EmissionEnergyMultiplier = 0.6f,
            Metallic = 0.22f,
            Roughness = 0.72f
        };
        MeshInstance3D meshInstance = new()
        {
            Name = "Mesh"
        };
        _material = material;
        _meshInstance = meshInstance;
        CollisionShape3D collisionShape = new()
        {
            Name = "Collision"
        };
        if (string.Equals(definition.Shape, "Cylinder", StringComparison.Ordinal))
        {
            float radius = (float)(Math.Max(
                definition.Size.X,
                definition.Size.Z) * 0.5);
            meshInstance.Mesh = new CylinderMesh
            {
                Material = material,
                TopRadius = radius * 0.88f,
                BottomRadius = radius,
                Height = (float)definition.Size.Y,
                RadialSegments = 16
            };
            collisionShape.Shape = new CylinderShape3D
            {
                Radius = radius,
                Height = (float)definition.Size.Y
            };
        }
        else
        {
            Vector3 size = new(
                (float)definition.Size.X,
                (float)definition.Size.Y,
                (float)definition.Size.Z);
            meshInstance.Mesh = new BoxMesh
            {
                Material = material,
                Size = size
            };
            collisionShape.Shape = new BoxShape3D
            {
                Size = size
            };
            if (string.Equals(definition.Category, "Cave", StringComparison.Ordinal))
            {
                // Cave presentation is authored by AddCaveEntranceDetails. Keep
                // only a shallow interaction/collision portal instead of the
                // generic solid POI box obscuring the entrance.
                meshInstance.Visible = false;
                collisionShape.Shape = new BoxShape3D
                {
                    Size = new Vector3(
                        size.X * 0.78f,
                        size.Y * 0.78f,
                        Math.Min(0.38f, Math.Max(0.18f, size.Z * 0.22f)))
                };
                collisionShape.Position = new Vector3(
                    0.0f,
                    0.0f,
                    -size.Z * 0.46f);
            }
        }

        _collisionShape = collisionShape;
        AddChild(meshInstance);
        AddVisualDetails(definition, material);
        AddChild(collisionShape);
        OmniLight3D marker = new()
        {
            Name = "DiscoveryMarker",
            Position = new Vector3(
                0.0f,
                (float)(definition.Size.Y * 0.55 + 0.5),
                0.0f),
            LightColor = new Color(
                (float)definition.Color.R,
                (float)definition.Color.G,
                (float)definition.Color.B),
            LightEnergy = 0.35f,
            OmniRange = 3.0f,
            ShadowEnabled = false
        };
        AddChild(marker);
    }

    private void AddVisualDetails(
        PlanetaryPoiDefinition definition,
        StandardMaterial3D material)
    {
        Node3D details = new()
        {
            Name = "VisualDetails"
        };
        AddChild(details);

        float width = (float)definition.Size.X;
        float height = (float)definition.Size.Y;
        float depth = (float)definition.Size.Z;
        bool vertical = definition.Category is "Signal" or "Science" or
            "Ancient" or "Infrastructure" or "Monument" or
            "Probe" or "Observatory";
        bool industrial = definition.Category is "Settlement" or "Commerce" or
            "Industry" or "Hostile" or "Shelter" or "Vault";

        if (string.Equals(definition.Category, "Cave", StringComparison.Ordinal))
        {
            AddCaveEntranceDetails(details, definition, material);
        }
        else if (string.Equals(definition.Category, "Landing", StringComparison.Ordinal))
        {
            details.AddChild(new MeshInstance3D
            {
                Name = "PadInset",
                Mesh = new CylinderMesh
                {
                    Material = material,
                    TopRadius = width * 0.34f,
                    BottomRadius = width * 0.34f,
                    Height = 0.08f,
                    RadialSegments = 24
                },
                Position = new Vector3(0.0f, height * 0.62f, 0.0f)
            });
            for (int i = 0; i < 4; i++)
            {
                float angle = i * Mathf.Pi * 0.5f;
                details.AddChild(new MeshInstance3D
                {
                    Name = $"PadBeacon{i}",
                    Mesh = new CylinderMesh
                    {
                        Material = material,
                        TopRadius = 0.06f,
                        BottomRadius = 0.09f,
                        Height = 0.35f,
                        RadialSegments = 8
                    },
                    Position = new Vector3(
                        Mathf.Cos(angle) * width * 0.38f,
                        0.22f,
                        Mathf.Sin(angle) * width * 0.38f)
                });
            }
        }
        else if (vertical)
        {
            details.AddChild(new MeshInstance3D
            {
                Name = "Spire",
                Mesh = new CylinderMesh
                {
                    Material = material,
                    TopRadius = Math.Max(0.04f, width * 0.06f),
                    BottomRadius = Math.Max(0.08f, width * 0.12f),
                    Height = Math.Max(0.6f, height * 0.55f),
                    RadialSegments = 8
                },
                Position = new Vector3(0.0f, height * 0.62f, 0.0f)
            });
            details.AddChild(new MeshInstance3D
            {
                Name = "SensorCrown",
                Mesh = new SphereMesh
                {
                    Material = material,
                    Radius = Math.Max(0.12f, width * 0.15f),
                    Height = Math.Max(0.22f, width * 0.28f),
                    RadialSegments = 10,
                    Rings = 5
                },
                Position = new Vector3(0.0f, height * 0.94f, 0.0f)
            });
        }
        else if (industrial)
        {
            for (int i = -1; i <= 1; i += 2)
            {
                details.AddChild(new MeshInstance3D
                {
                    Name = i < 0 ? "SideModuleL" : "SideModuleR",
                    Mesh = new BoxMesh
                    {
                        Material = material,
                        Size = new Vector3(width * 0.28f, height * 0.52f, depth * 0.46f)
                    },
                    Position = new Vector3(i * width * 0.44f, height * 0.12f, 0.0f)
                });
            }
            details.AddChild(new MeshInstance3D
            {
                Name = "RoofUnit",
                Mesh = new BoxMesh
                {
                    Material = material,
                    Size = new Vector3(width * 0.45f, Math.Max(0.15f, height * 0.16f), depth * 0.34f)
                },
                Position = new Vector3(0.0f, height * 0.57f, 0.0f)
            });
        }
        else
        {
            details.AddChild(new MeshInstance3D
            {
                Name = "AccentMass",
                Mesh = new BoxMesh
                {
                    Material = material,
                    Size = new Vector3(width * 0.54f, Math.Max(0.18f, height * 0.35f), depth * 0.58f)
                },
                Position = new Vector3(width * 0.16f, height * 0.18f, -depth * 0.12f),
                Rotation = new Vector3(0.0f, 0.22f, 0.0f)
            });
        }

        details.SetMeta("surface_visual_parts", details.GetChildCount());
    }

    private static void AddCaveEntranceDetails(
        Node3D details,
        PlanetaryPoiDefinition definition,
        StandardMaterial3D material)
    {
        float width = (float)definition.Size.X;
        float height = (float)definition.Size.Y;
        float depth = (float)definition.Size.Z;
        StandardMaterial3D mouth = new()
        {
            AlbedoColor = new Color(0.008f, 0.010f, 0.014f),
            EmissionEnabled = false,
            Metallic = 0.0f,
            Roughness = 1.0f
        };
        details.AddChild(new MeshInstance3D
        {
            Name = "CaveMouth",
            Mesh = new BoxMesh
            {
                Material = mouth,
                Size = new Vector3(width * 0.72f, height * 0.78f, 0.18f)
            },
            Position = new Vector3(0.0f, -height * 0.02f, -depth * 0.54f)
        });
        for (int index = 0; index < 7; index++)
        {
            float t = index / 6.0f;
            float angle = Mathf.Lerp(Mathf.Pi, 0.0f, t);
            float x = Mathf.Cos(angle) * width * 0.48f;
            float y = Mathf.Sin(angle) * height * 0.55f - height * 0.13f;
            details.AddChild(new MeshInstance3D
            {
                Name = $"RockArch{index}",
                Mesh = new BoxMesh
                {
                    Material = material,
                    Size = new Vector3(0.72f, 0.74f, depth * 0.82f)
                },
                Position = new Vector3(x, y, 0.0f),
                Rotation = new Vector3(0.12f * (index % 2), index * 0.31f, 0.16f * Mathf.Cos(angle))
            });
        }
        details.AddChild(new MeshInstance3D
        {
            Name = "CaveLintel",
            Mesh = new BoxMesh
            {
                Material = material,
                Size = new Vector3(width * 0.80f, 0.45f, depth * 0.95f)
            },
            Position = new Vector3(0.0f, height * 0.40f, 0.0f),
            Rotation = new Vector3(0.06f, -0.12f, 0.03f)
        });
    }

    public void ApplyState(bool discovered, bool resolved)
    {
        if (_definition is not { } definition ||
            _material is not { } material)
        {
            return;
        }

        float multiplier = resolved ? 0.48f : discovered ? 0.92f : 0.34f;
        material.AlbedoColor = new Color(
            (float)definition.Color.R * multiplier,
            (float)definition.Color.G * multiplier,
            (float)definition.Color.B * multiplier,
            resolved ? 0.72f : 1.0f);
        material.Emission = new Color(
            (float)definition.Color.R * (discovered ? 0.32f : 0.08f),
            (float)definition.Color.G * (discovered ? 0.32f : 0.08f),
            (float)definition.Color.B * (discovered ? 0.32f : 0.08f));
        material.EmissionEnergyMultiplier = resolved
            ? 0.35f
            : discovered ? 1.45f : 0.6f;
        material.Transparency = resolved
            ? BaseMaterial3D.TransparencyEnum.Alpha
            : BaseMaterial3D.TransparencyEnum.Disabled;
        if (GetNodeOrNull<OmniLight3D>("DiscoveryMarker") is { } marker)
        {
            marker.LightEnergy = resolved ? 0.1f : discovered ? 0.85f : 0.35f;
        }
    }

    public void SetRuntimeResident(bool resident)
    {
        Visible = resident;
        CollisionLayer = resident ? 1u : 0u;
        _collisionShape?.SetDeferred("disabled", !resident);
    }

    public void Interact(Node3D interactor)
    {
        if (GetTree().CurrentScene is not SalvageRepairSlice slice)
        {
            GD.PushError($"Planetary POI {InstanceId} has no controller.");
            return;
        }

        slice.TryInteractPlanetaryPoi(this, interactor);
    }
}
