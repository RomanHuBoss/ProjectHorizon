using ProjectHorizon.Tests.Support;

namespace ProjectHorizon.Tests.Unit;

public sealed class BaseConstructionTests
{
    [Fact]
    public void PlacementPreflight_UsesTheSameRulesAsTryPlace()
    {
        BaseConstructionCatalog catalog = RepositoryFixture.BaseConstruction;
        BaseConstructionRuntime runtime = new(catalog);
        string anchorId = catalog.Modules.Values.Single(module => module.IsAnchor).ModuleId;
        string moduleId = catalog.Modules.Keys
            .Where(id => !string.Equals(id, anchorId, StringComparison.Ordinal))
            .OrderBy(id => id, StringComparer.Ordinal)
            .First();

        Assert.Equal(
            BasePlacementResult.AnchorRequired,
            runtime.EvaluatePlacement(moduleId, 0, 0, out _));
        Assert.Equal(
            BasePlacementResult.AnchorRequired,
            runtime.TryPlace(moduleId, 0, 0, 0, out _, out _));

        Assert.Equal(
            BasePlacementResult.Placed,
            runtime.EvaluatePlacement(anchorId, 0, 0, out _));
        Assert.Equal(
            BasePlacementResult.Placed,
            runtime.TryPlace(anchorId, 0, 0, 0, out _, out _));

        Assert.Equal(
            BasePlacementResult.Overlap,
            runtime.EvaluatePlacement(moduleId, 0, 0, out _));
        Assert.Equal(
            BasePlacementResult.Overlap,
            runtime.TryPlace(moduleId, 0, 0, 0, out _, out _));

        Assert.Equal(
            BasePlacementResult.NotSnapped,
            runtime.EvaluatePlacement(moduleId, 20, 20, out _));
        Assert.Equal(
            BasePlacementResult.NotSnapped,
            runtime.TryPlace(moduleId, 20, 20, 0, out _, out _));

        Assert.Equal(
            BasePlacementResult.Placed,
            runtime.EvaluatePlacement(moduleId, 1, 0, out _));
        Assert.Equal(
            BasePlacementResult.Placed,
            runtime.TryPlace(moduleId, 1, 0, 0, out _, out _));
    }

    [Fact]
    public void PlacementPreflight_RejectsTheSameInteractiveLimitAsTryPlace()
    {
        BaseConstructionCatalog catalog = RepositoryFixture.BaseConstruction;
        BaseConstructionLimits limits = catalog.Limits;
        string anchorId = catalog.Modules.Values.Single(module => module.IsAnchor).ModuleId;
        const string interactiveId = "module.solar_array";
        BaseConstructionStockSaveData[] stock = catalog.Modules.Values
            .OrderBy(module => module.ModuleId, StringComparer.Ordinal)
            .Select(module => new BaseConstructionStockSaveData(module.ModuleId, 1))
            .ToArray();
        BaseConstructionModuleSaveData[] modules = Enumerable.Range(
                0,
                limits.MaximumInteractiveDevices)
            .Select(index => new BaseConstructionModuleSaveData(
                $"base.limit.{index + 1:000000}",
                index == 0 ? anchorId : interactiveId,
                index,
                0,
                0,
                Enabled: true))
            .ToArray();
        BaseConstructionRuntime runtime = new(
            catalog,
            new BaseConstructionSaveData(
                "base.limit.unit",
                limits.MaximumInteractiveDevices + 1,
                0.0,
                stock,
                modules));

        BasePlacementResult preflight = runtime.EvaluatePlacement(
            interactiveId,
            limits.MaximumInteractiveDevices,
            0,
            out _);
        BasePlacementResult mutation = runtime.TryPlace(
            interactiveId,
            limits.MaximumInteractiveDevices,
            0,
            0,
            out _,
            out _);

        Assert.Equal(BasePlacementResult.LimitExceeded, preflight);
        Assert.Equal(preflight, mutation);
        Assert.Equal(limits.MaximumInteractiveDevices, runtime.Power.InteractiveDevices);
    }

    [Fact]
    public void DisabledBattery_IsRemovedFromAvailableNetworkCapacity()
    {
        BaseConstructionCatalog catalog = RepositoryFixture.BaseConstruction;
        BaseConstructionRuntime runtime = new(catalog);
        string anchorId = catalog.Modules.Values.Single(module => module.IsAnchor).ModuleId;
        Assert.Equal(
            BasePlacementResult.Placed,
            runtime.TryPlace(anchorId, 0, 0, 0, out _, out _));
        Assert.Equal(
            BasePlacementResult.Placed,
            runtime.TryPlace("module.solar_array", 1, 0, 0, out _, out _));
        Assert.Equal(
            BasePlacementResult.Placed,
            runtime.TryPlace("module.battery_bank", 2, 0, 0, out BaseModulePlacement? battery, out _));
        Assert.NotNull(battery);

        double installedCapacity = runtime.Power.BatteryCapacity;
        double bankCapacity = catalog.GetModule("module.battery_bank").BatteryCapacity;
        Assert.True(installedCapacity >= bankCapacity);
        Assert.True(runtime.TryToggle(battery!.InstanceId, out _));
        Assert.Equal(installedCapacity - bankCapacity, runtime.Power.BatteryCapacity, precision: 6);
        Assert.True(runtime.TryToggle(battery.InstanceId, out _));
        Assert.Equal(installedCapacity, runtime.Power.BatteryCapacity, precision: 6);
    }

    [Fact]
    public void Restore_RejectsNonFiniteAndOverCapacityEnergy()
    {
        BaseConstructionCatalog catalog = RepositoryFixture.BaseConstruction;
        BaseConstructionSaveData save = new BaseConstructionRuntime(catalog).CreateSaveData();

        Assert.Throws<InvalidOperationException>(() =>
            new BaseConstructionRuntime(catalog, save with { StoredEnergy = double.NaN }));
        Assert.Throws<InvalidOperationException>(() =>
            new BaseConstructionRuntime(catalog, save with { StoredEnergy = 1.0 }));
    }
}
