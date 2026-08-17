using System;

public enum TextureBudgetClass
{
    PlayerCharacter = 0,
    LargeShip = 1,
    Npc = 2,
    LargeBuilding = 3,
    OrdinaryObject = 4,
    Plant = 5,
    UiIcon = 6,
    TiledSurface = 7
}

public sealed record LightingResidencySettings(
    int MaximumLocalLights,
    int MaximumShadowedLocalLights,
    double MaximumLocalLightDistanceMeters);

/// <summary>
/// Executable presentation limits for Technical Specification sections 26.2 and 26.3.
/// The policy deliberately contains no Godot objects so it can be unit-tested and used
/// by content/build validators as the single source of truth.
/// </summary>
public static class TextureLightingBudgetPolicy
{
    public const int PlayerCharacterMaxTextureDimension = 2048;
    public const int LargeShipMaxTextureDimension = 2048;
    public const int NpcMaxTextureDimension = 2048;
    public const int LargeBuildingMaxTextureDimension = 2048;
    public const int OrdinaryObjectMaxTextureDimension = 1024;
    public const int PlantMaxTextureDimension = 1024;
    public const int UiIconMaxTextureDimension = 512;
    public const int TiledSurfaceMaxTextureDimension = 2048;

    public const bool TextureAtlasesRequired = true;
    public const bool ReusableMaterialsRequired = true;
    public const int MaximumProductionMaterialsPerAsset = 5;

    // Surface lighting: one directional star is authoritative. Local lights are a
    // bounded accent budget and never gain their own dynamic shadows in v1.
    public const int SurfaceMaximumLocalLights = 6;
    public const int SurfaceMaximumShadowedLocalLights = 0;
    public const double SurfaceLocalLightDistanceMeters = 96.0;

    // Cave prefabs are isolated surface interiors and intentionally cheaper.
    public const int CaveMaximumLocalLights = 4;
    public const int CaveMaximumShadowedLocalLights = 0;
    public const double CaveLocalLightDistanceMeters = 56.0;

    // Interior shells may use a few authored dynamic accents over a static/baked
    // lighting baseline. Only a bounded subset may cast shadows.
    public const int InteriorMaximumLocalLights = 8;
    public const int InteriorMaximumShadowedLocalLights = 2;
    public const double InteriorLocalLightDistanceMeters = 140.0;

    // Orbit/transit local lights are presentation aids only (ship/station/dock).
    public const int OrbitMaximumLocalLights = 4;
    public const int OrbitMaximumShadowedLocalLights = 0;
    public const double OrbitLocalLightDistanceMeters = 420.0;

    public const bool SurfaceSingleDirectionalStarRequired = true;
    public const bool SurfaceAmbientLightingRequired = true;
    public const bool InteriorBakedBaselineRequired = true;
    public const bool DistantLightingSimplificationRequired = true;

    public static int GetMaximumTextureDimension(TextureBudgetClass textureClass) =>
        textureClass switch
        {
            TextureBudgetClass.PlayerCharacter => PlayerCharacterMaxTextureDimension,
            TextureBudgetClass.LargeShip => LargeShipMaxTextureDimension,
            TextureBudgetClass.Npc => NpcMaxTextureDimension,
            TextureBudgetClass.LargeBuilding => LargeBuildingMaxTextureDimension,
            TextureBudgetClass.OrdinaryObject => OrdinaryObjectMaxTextureDimension,
            TextureBudgetClass.Plant => PlantMaxTextureDimension,
            TextureBudgetClass.UiIcon => UiIconMaxTextureDimension,
            TextureBudgetClass.TiledSurface => TiledSurfaceMaxTextureDimension,
            _ => throw new ArgumentOutOfRangeException(nameof(textureClass), textureClass, null)
        };

    public static LightingResidencySettings ResolveLighting(
        WorldSceneKind worldKind,
        bool insideCave) =>
        insideCave
            ? new LightingResidencySettings(
                CaveMaximumLocalLights,
                CaveMaximumShadowedLocalLights,
                CaveLocalLightDistanceMeters)
            : worldKind switch
            {
                WorldSceneKind.Surface => new LightingResidencySettings(
                    SurfaceMaximumLocalLights,
                    SurfaceMaximumShadowedLocalLights,
                    SurfaceLocalLightDistanceMeters),
                WorldSceneKind.StationInterior => new LightingResidencySettings(
                    InteriorMaximumLocalLights,
                    InteriorMaximumShadowedLocalLights,
                    InteriorLocalLightDistanceMeters),
                WorldSceneKind.Orbit or WorldSceneKind.InterplanetaryTransit or WorldSceneKind.HyperspaceTransit =>
                    new LightingResidencySettings(
                        OrbitMaximumLocalLights,
                        OrbitMaximumShadowedLocalLights,
                        OrbitLocalLightDistanceMeters),
                _ => throw new ArgumentOutOfRangeException(nameof(worldKind), worldKind, null)
            };
}
