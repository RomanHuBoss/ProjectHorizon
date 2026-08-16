using ProjectHorizon.Tests.Support;

namespace ProjectHorizon.Tests.Golden;

public sealed class GoldenSeedTests
{
    private static GoldenSeedManifest LoadManifest() =>
        GoldenSeedContract.LoadFromJson(File.ReadAllText(Path.Combine(
            RepositoryFixture.Root,
            "src",
            "Game.Client",
            "Testing",
            "golden-seeds.v1.json")));

    [Fact]
    public void GeneratorVersion_MatchesReviewedGoldenManifest()
    {
        GoldenSeedManifest manifest = LoadManifest();
        Assert.Equal(ProjectHorizonGenerator.Version, manifest.GeneratorVersion);
        Assert.Equal(ProjectHorizonGenerator.Version, GalaxyNavigationRuntime.GeneratorVersion);
    }

    [Fact]
    public void FixedSystemSeeds_MatchReviewedGoldenOutputsAndChecksums()
    {
        GoldenSeedManifest manifest = LoadManifest();
        Assert.True(manifest.SystemCases.Count >= 3);
        foreach (GoldenSystemCase testCase in manifest.SystemCases)
        {
            Assert.True(
                GoldenSeedContract.VerifySystemCase(testCase, out string mismatch),
                $"{testCase.UniverseSeed}@{testCase.SectorX},{testCase.SectorY},{testCase.SectorZ}: {mismatch}");
        }
    }

    [Fact]
    public void FixedPoiSeed_MatchesControlHeightsPositionsAndChecksum()
    {
        GoldenSeedManifest manifest = LoadManifest();
        Assert.True(
            GoldenSeedContract.VerifyPoiFixture(
                manifest.PoiFixture,
                RepositoryFixture.Pois,
                out string mismatch),
            mismatch);
        Assert.Equal(20, manifest.PoiFixture.ExpectedCount);
        Assert.Contains(manifest.PoiFixture.Placements, item => Math.Abs(item.ControlHeight) > 0.01);
        Assert.Contains(manifest.PoiFixture.Placements, item => Math.Abs(item.PositionX) >= 20.0);
        foreach (GoldenPoiExpectation item in manifest.PoiFixture.Placements)
        {
            PlanetaryPoiDefinition definition = RepositoryFixture.Pois.GetDefinition(item.PoiTypeId);
            double expectedY = item.ControlHeight + 0.1 + definition.Size.Y / 2.0;
            Assert.True(
                Math.Abs(item.PositionY - expectedY) <= 0.000_001,
                $"{item.InstanceId} golden Y must remain terrain-projected");
        }
    }

    [Fact]
    public void GoldenManifest_IsNotSelfUpdatingAndRequiresExplicitVersionBump()
    {
        GoldenSeedManifest manifest = LoadManifest();
        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal(ProjectHorizonGenerator.Version, manifest.GeneratorVersion);
        Assert.All(manifest.SystemCases, item => Assert.Matches("^[a-f0-9]{64}$", item.Checksum));
        Assert.Matches("^[a-f0-9]{64}$", manifest.PoiFixture.Checksum);
    }
}
