using Godot;

public sealed record TextureLightingBudgetAcceptanceReport(
    bool TextureClasses,
    bool AtlasMaterialPolicy,
    bool SurfaceLightingPolicy,
    bool InteriorLightingPolicy,
    bool DistantSimplification,
    bool LiveDirectionalStar,
    bool LiveAmbientEnvironment,
    bool LiveLocalBudget,
    bool ShadowBudget,
    int LocalLightsFound,
    int LocalLightsActive,
    int ShadowedLocalLights,
    WorldSceneKind WorldKind)
{
    public bool Passed =>
        TextureClasses &&
        AtlasMaterialPolicy &&
        SurfaceLightingPolicy &&
        InteriorLightingPolicy &&
        DistantSimplification &&
        LiveDirectionalStar &&
        LiveAmbientEnvironment &&
        LiveLocalBudget &&
        ShadowBudget;

    public string BuildOutputLine() =>
        $"TASK-208 texture/material/lighting acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"textures={(TextureClasses ? 1 : 0)}; atlasMaterials={(AtlasMaterialPolicy ? 1 : 0)}; " +
        $"surface={(SurfaceLightingPolicy ? 1 : 0)}; interior={(InteriorLightingPolicy ? 1 : 0)}; " +
        $"distant={(DistantSimplification ? 1 : 0)}; star={(LiveDirectionalStar ? 1 : 0)}; " +
        $"ambient={(LiveAmbientEnvironment ? 1 : 0)}; localBudget={(LiveLocalBudget ? 1 : 0)}; " +
        $"shadowBudget={(ShadowBudget ? 1 : 0)}; local={LocalLightsActive}/{LocalLightsFound}; " +
        $"shadowedLocal={ShadowedLocalLights}; world={WorldKind}; " +
        "result=section-26.2-texture-material-budgets-and-26.3-bounded-lighting-residency.";
}

public static class TextureLightingBudgetAcceptanceRunner
{
    public static TextureLightingBudgetAcceptanceReport Evaluate(
        WorldSceneKind worldKind,
        bool insideCave,
        DirectionalLight3D? directionalStar,
        WorldEnvironment? worldEnvironment,
        int localLightsFound,
        int localLightsActive,
        int shadowedLocalLights)
    {
        bool textures =
            TextureLightingBudgetPolicy.GetMaximumTextureDimension(TextureBudgetClass.PlayerCharacter) == 2048 &&
            TextureLightingBudgetPolicy.GetMaximumTextureDimension(TextureBudgetClass.LargeShip) == 2048 &&
            TextureLightingBudgetPolicy.GetMaximumTextureDimension(TextureBudgetClass.Npc) == 2048 &&
            TextureLightingBudgetPolicy.GetMaximumTextureDimension(TextureBudgetClass.LargeBuilding) == 2048 &&
            TextureLightingBudgetPolicy.GetMaximumTextureDimension(TextureBudgetClass.OrdinaryObject) == 1024 &&
            TextureLightingBudgetPolicy.GetMaximumTextureDimension(TextureBudgetClass.Plant) == 1024 &&
            TextureLightingBudgetPolicy.GetMaximumTextureDimension(TextureBudgetClass.UiIcon) == 512 &&
            TextureLightingBudgetPolicy.GetMaximumTextureDimension(TextureBudgetClass.TiledSurface) == 2048;

        bool atlasMaterials = TextureLightingBudgetPolicy.TextureAtlasesRequired &&
            TextureLightingBudgetPolicy.ReusableMaterialsRequired &&
            TextureLightingBudgetPolicy.MaximumProductionMaterialsPerAsset <= 5;

        LightingResidencySettings settings =
            TextureLightingBudgetPolicy.ResolveLighting(worldKind, insideCave);
        bool surface =
            TextureLightingBudgetPolicy.SurfaceSingleDirectionalStarRequired &&
            TextureLightingBudgetPolicy.SurfaceAmbientLightingRequired &&
            TextureLightingBudgetPolicy.SurfaceMaximumLocalLights <= 6 &&
            TextureLightingBudgetPolicy.SurfaceMaximumShadowedLocalLights == 0;
        bool interior = TextureLightingBudgetPolicy.InteriorBakedBaselineRequired &&
            TextureLightingBudgetPolicy.InteriorMaximumLocalLights <= 8 &&
            TextureLightingBudgetPolicy.InteriorMaximumShadowedLocalLights <= 2;
        bool distant = TextureLightingBudgetPolicy.DistantLightingSimplificationRequired &&
            settings.MaximumLocalLightDistanceMeters > 0.0;
        bool liveStar = worldKind != WorldSceneKind.Surface || directionalStar is not null;
        bool liveAmbient = worldKind != WorldSceneKind.Surface ||
            worldEnvironment?.Environment is not null;
        bool localBudget = localLightsActive <= settings.MaximumLocalLights &&
            localLightsActive <= localLightsFound;
        bool shadowBudget = shadowedLocalLights <= settings.MaximumShadowedLocalLights;

        return new TextureLightingBudgetAcceptanceReport(
            textures,
            atlasMaterials,
            surface,
            interior,
            distant,
            liveStar,
            liveAmbient,
            localBudget,
            shadowBudget,
            localLightsFound,
            localLightsActive,
            shadowedLocalLights,
            worldKind);
    }
}
