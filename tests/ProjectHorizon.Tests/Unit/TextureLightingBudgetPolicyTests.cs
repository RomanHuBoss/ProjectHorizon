namespace ProjectHorizon.Tests.Unit;

public sealed class TextureLightingBudgetPolicyTests
{
    [Theory]
    [InlineData(TextureBudgetClass.PlayerCharacter, 2048)]
    [InlineData(TextureBudgetClass.LargeShip, 2048)]
    [InlineData(TextureBudgetClass.Npc, 2048)]
    [InlineData(TextureBudgetClass.LargeBuilding, 2048)]
    [InlineData(TextureBudgetClass.OrdinaryObject, 1024)]
    [InlineData(TextureBudgetClass.Plant, 1024)]
    [InlineData(TextureBudgetClass.UiIcon, 512)]
    [InlineData(TextureBudgetClass.TiledSurface, 2048)]
    public void TextureClassMaximumsMatchSection262(TextureBudgetClass value, int expected)
    {
        Assert.Equal(expected, TextureLightingBudgetPolicy.GetMaximumTextureDimension(value));
    }

    [Fact]
    public void SurfaceUsesOneStarAndBoundedUnshadowedLocalLights()
    {
        LightingResidencySettings value = TextureLightingBudgetPolicy.ResolveLighting(
            WorldSceneKind.Surface,
            insideCave: false);
        Assert.True(TextureLightingBudgetPolicy.SurfaceSingleDirectionalStarRequired);
        Assert.True(TextureLightingBudgetPolicy.SurfaceAmbientLightingRequired);
        Assert.Equal(6, value.MaximumLocalLights);
        Assert.Equal(0, value.MaximumShadowedLocalLights);
    }

    [Fact]
    public void CaveBudgetIsStricterThanSurface()
    {
        LightingResidencySettings surface = TextureLightingBudgetPolicy.ResolveLighting(
            WorldSceneKind.Surface,
            insideCave: false);
        LightingResidencySettings cave = TextureLightingBudgetPolicy.ResolveLighting(
            WorldSceneKind.Surface,
            insideCave: true);
        Assert.True(cave.MaximumLocalLights < surface.MaximumLocalLights);
        Assert.True(cave.MaximumLocalLightDistanceMeters < surface.MaximumLocalLightDistanceMeters);
    }

    [Fact]
    public void InteriorAllowsOnlyBoundedShadowedDynamicLights()
    {
        LightingResidencySettings value = TextureLightingBudgetPolicy.ResolveLighting(
            WorldSceneKind.StationInterior,
            insideCave: false);
        Assert.True(TextureLightingBudgetPolicy.InteriorBakedBaselineRequired);
        Assert.Equal(8, value.MaximumLocalLights);
        Assert.InRange(value.MaximumShadowedLocalLights, 0, 2);
    }

    [Fact]
    public void AtlasesAndReusableMaterialsAreNormative()
    {
        Assert.True(TextureLightingBudgetPolicy.TextureAtlasesRequired);
        Assert.True(TextureLightingBudgetPolicy.ReusableMaterialsRequired);
        Assert.InRange(TextureLightingBudgetPolicy.MaximumProductionMaterialsPerAsset, 1, 5);
    }
}
