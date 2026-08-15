using ProjectHorizon.Tests.Support;

namespace ProjectHorizon.Tests.Unit;

public sealed class DomainTests
{
    [Fact]
    public void StableIds_AcceptCanonicalIds_RejectMutableNames()
    {
        Assert.True(GameContentCatalog.IsStableId("resource.iron_ore"));
        Assert.True(GameContentCatalog.IsStableId("quest.proc.01.visitlocation"));
        Assert.False(GameContentCatalog.IsStableId("Iron Ore"));
        Assert.False(GameContentCatalog.IsStableId("Resource.Iron"));
        Assert.False(GameContentCatalog.IsStableId("resource"));
        Assert.False(GameContentCatalog.IsStableId("resource.iron-ore"));
    }

    [Fact]
    public void IndustryCatalog_IsAcyclicReachableAndStationCompatible()
    {
        GameContentCatalog catalog = RepositoryFixture.Content;
        IndustryCatalogAnalysis analysis = catalog.AnalyzeIndustry();

        Assert.Equal(174, analysis.ItemCount);
        Assert.Equal(42, analysis.ResourceCount);
        Assert.Equal(128, analysis.RecipeCount);
        Assert.Equal(15, analysis.StationCount);
        Assert.Equal(32, analysis.TechnologyCount);
        Assert.Equal(0, analysis.DependencyCycles);
        Assert.Equal(0, analysis.UnreachableRecipes);

        foreach (CraftingRecipeDefinition recipe in catalog.Recipes.Values)
        {
            Assert.True(GameContentCatalog.IsStableId(recipe.RecipeId));
            Assert.NotEmpty(recipe.Outputs);
            Assert.All(recipe.Inputs, input => Assert.True(catalog.Items.ContainsKey(input.DefinitionId)));
            Assert.All(recipe.Outputs, output => Assert.True(catalog.Items.ContainsKey(output.DefinitionId)));

            if (string.IsNullOrWhiteSpace(recipe.RequiredStation))
            {
                continue;
            }

            CraftingStationDefinition station = catalog.GetStation(recipe.RequiredStation);
            Assert.True(
                station.SupportedCategories.Contains(recipe.Category, StringComparer.Ordinal),
                $"{recipe.RecipeId} category {recipe.Category} is not supported by {station.StationId}.");
        }
    }

    [Fact]
    public void Inventory_GrantsAggregateAndSaveAsStableStacks()
    {
        GameContentCatalog catalog = RepositoryFixture.Content;
        StarterRepairSession session = new(catalog.GetRecipe(StarterRepairContentIds.RecipeId));
        string definitionId = catalog.Resources.Values.First().ItemDefinitionId;

        session.GrantInventory(definitionId, 3);
        session.GrantInventory(definitionId, 7);

        Assert.Equal(10, session.GetAvailableQuantity(definitionId));
        Assert.Single(session.AvailableInventory.Where(stack => stack.DefinitionId == definitionId));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.GrantInventory(definitionId, 0));
        Assert.Throws<ArgumentException>(() => session.GrantInventory("not stable", 1));
    }

    [Fact]
    public void TechnologyGraph_AutoUnlocksRootsAndToleratesRemovedTechnology()
    {
        GameContentCatalog catalog = RepositoryFixture.Content;
        string existing = catalog.Technologies.Keys.OrderBy(id => id, StringComparer.Ordinal).First();
        TechnologyProgression progression = new(
            catalog.Technologies,
            researchPoints: 5000,
            unlockedTechnologyIds: new[] { existing, "technology.removed.prototype" });

        Assert.True(progression.IsUnlocked(existing));
        Assert.Contains("technology.removed.prototype", progression.IgnoredUnknownTechnologyIds);
        Assert.DoesNotContain("technology.removed.prototype", progression.ToSaveData().UnlockedTechnologyIds);

        foreach (TechnologyDefinition technology in catalog.Technologies.Values)
        {
            Assert.All(technology.Prerequisites, prerequisite =>
                Assert.True(catalog.Technologies.ContainsKey(prerequisite)));
        }
    }

    [Fact]
    public void EconomyQuotes_AreDeterministicForSameDayAndConserveSpread()
    {
        GameContentCatalog content = RepositoryFixture.Content;
        StationServicesCatalog services = RepositoryFixture.StationServices;
        string npcId = services.Npcs.Keys.OrderBy(id => id, StringComparer.Ordinal).First();
        const long now = 1_800_000_000L;
        StationServicesRuntime first = new(content, services, npcId, nowUnixSeconds: now);
        StationServicesRuntime second = new(content, services, npcId, nowUnixSeconds: now);
        string itemId = content.Items.Keys.OrderBy(id => id, StringComparer.Ordinal).First();

        MarketPriceQuote left = first.Quote(itemId);
        MarketPriceQuote right = second.Quote(itemId);

        Assert.Equal(left, right);
        Assert.True(left.BuyPrice >= left.SellPrice);
        Assert.True(left.BuyPrice > 0);
        Assert.True(left.SellPrice >= 0);

        long advancedDays = first.RefreshEconomy(now + StationServicesRuntime.EconomyDaySeconds * 3);
        Assert.Equal(3, advancedDays);
        Assert.Equal(3, first.DayIndex);
    }

    [Fact]
    public void ProceduralQuestBoard_CoversAllTypesAndIsFeasible()
    {
        ProceduralQuestCatalog catalog = RepositoryFixture.ProceduralQuests;
        string[] target = { "target.valid" };
        string[] npc = { "npc.trader.ilia_voss" };
        ProceduralQuestCapabilities capabilities = new(
            target, target, target, target, target, target, target, target, target,
            target, target, target, target, target, npc,
            LandingAvailable: true,
            InventoryCapacityAvailable: true,
            EquipmentTier: 2);

        IReadOnlyList<ProceduralQuestDefinition> first = ProceduralQuestGenerator.Generate(catalog, capabilities);
        IReadOnlyList<ProceduralQuestDefinition> second = ProceduralQuestGenerator.Generate(catalog, capabilities);

        Assert.Equal(ProceduralQuestCatalog.ExpectedBoardSize, first.Count);
        Assert.Equal(first, second);
        Assert.Equal(
            Enum.GetValues<ProceduralQuestObjectiveType>().Length,
            first.Select(quest => quest.ObjectiveType).Distinct().Count());
        Assert.All(first, quest =>
        {
            Assert.True(
                ProceduralQuestGenerator.ValidateFeasibility(quest, capabilities, out string reason),
                reason);
        });
    }

    [Fact]
    public void ShipStats_AreCalculatedFromClassAndInstalledModules()
    {
        ShipSystemsCatalog ships = RepositoryFixture.Ships;
        ShipClassDefinition shipClass = ships.GetClass(ships.StarterClassId);
        ShipModuleDefinition module = ships.Modules.Values
            .First(candidate =>
                candidate.Effects.Hull > 0.0 || candidate.Effects.Shield > 0.0 ||
                candidate.Effects.CargoCapacity > 0 || candidate.Effects.FuelCapacity > 0.0 ||
                candidate.Effects.Acceleration > 0.0 || candidate.Effects.MaxSpeed > 0.0 ||
                candidate.Effects.Maneuverability > 0.0 || candidate.Effects.HyperdriveRange > 0.0 ||
                candidate.Effects.AtmosphericEfficiency > 0.0);
        ShipSystemsSaveData save = new(
            shipClass.ShipClassId,
            shipClass.BaseStats.FuelCapacity,
            new[] { new ShipModuleInstallationSaveData(module.ModuleId, module.SlotType, 0) },
            ships.Systems.Keys.Select(id => new ShipSystemHealthSaveData(id, 100.0)).ToArray(),
            Commissioned: true);
        ShipSystemsRuntime runtime = new(ships, save, commissioned: true);
        ShipEffectiveStats stats = runtime.GetEffectiveStats();

        Assert.True(stats.Hull >= shipClass.BaseStats.Hull);
        Assert.True(stats.Shield >= shipClass.BaseStats.Shield);
        Assert.True(stats.CargoCapacity >= shipClass.BaseStats.CargoCapacity);
        Assert.True(stats.FuelCapacity >= shipClass.BaseStats.FuelCapacity);
        Assert.True(stats.HyperdriveRange >= shipClass.BaseStats.HyperdriveRange);
        Assert.Equal(1, runtime.InstalledModuleCount);
    }
}
