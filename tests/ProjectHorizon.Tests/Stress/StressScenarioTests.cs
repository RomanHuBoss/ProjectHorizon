using Godot;
using Microsoft.Data.Sqlite;
using ProjectHorizon.Tests.Support;

namespace ProjectHorizon.Tests.Stress;

public sealed class StressScenarioTests
{
    public StressScenarioTests()
    {
        SaveDatabase.RegisterKnownInventoryDefinitions(RepositoryFixture.Content.Items.Keys);
    }

    [Fact]
    public void TwoHourFlight_AnalyticSystemRemainsFiniteAndOrbitStable()
    {
        GalaxySystemDefinition system = new GalaxyNavigationRuntime(72_004_211L)
            .GenerateSystem(5, -4, 2);
        StarSystemSimulationRuntime runtime = new(system);
        StarSystemBodyDefinition[] orbiting = runtime.Definitions
            .Where(value => value.ParentBodyId is not null && value.OrbitRadius > 0.0)
            .ToArray();

        for (int second = 0; second <= 2 * 60 * 60; second++)
        {
            foreach (StarSystemBodyDefinition body in orbiting)
            {
                SystemDouble3 child = runtime.EvaluateBodyPosition(body.BodyId, second);
                SystemDouble3 parent = runtime.EvaluateBodyPosition(body.ParentBodyId!, second);
                double radius = (child - parent).Length();
                Assert.True(double.IsFinite(radius));
                Assert.InRange(Math.Abs(radius - body.OrbitRadius), 0.0, 1e-7);
            }
        }
    }

    [Fact]
    public void EightHourAutomaticMovement_StaysBoundedWithLocalSteeringGrid()
    {
        AerialSteeringRuntime runtime = new(10.0f);
        runtime.ReplaceEnvironment(
            new[]
            {
                new AerialObstacleSphere("obstacle.a", new Vector3(30, 10, 10), 8),
                new AerialObstacleSphere("obstacle.b", new Vector3(-20, 15, -12), 6)
            },
            new[]
            {
                new AerialPointOfInterest("poi.a", "stress", new Vector3(50, 16, 50), 2)
            });
        Vector3 position = new(0, 12, 0);
        Vector3 velocity = new(3, 0, 2);
        const float delta = 1.0f;
        const float speed = 7.0f;

        for (int second = 0; second < 8 * 60 * 60; second++)
        {
            Vector3 target = new(
                MathF.Sin(second / 900.0f) * 60.0f,
                15.0f,
                MathF.Cos(second / 900.0f) * 60.0f);
            Vector3 desired = runtime.Arrive(position, target, speed, 12.0f);
            desired += runtime.ComputeObstacleAvoidance(position, velocity, 1.0f, 1.0f, 9.0f);
            desired = runtime.ApplyAltitudeEnvelope(desired, position.Y, 6, 15, 25, 0.4f, 4.0f);
            Vector3 blended = velocity.Lerp(desired, 0.15f);
            velocity = blended.Length() > speed
                ? blended.Normalized() * speed
                : blended;
            position += velocity * delta;
            runtime.UpsertEntity("stress.agent", "stress", position, velocity, 1.0f);
            runtime.RecordFlyingFaunaSample();
            Assert.True(position.IsFinite());
            Assert.InRange(position.Y, 2.0f, 30.0f);
        }

        AerialSteeringSnapshot snapshot = runtime.CreateSnapshot();
        Assert.True(snapshot.GridQueries > 0);
        Assert.Equal(8 * 60 * 60, snapshot.FlyingFaunaSamples);
        Assert.True(snapshot.AltitudeCorrections > 0);
    }

    [Fact]
    public async Task OneHundredSequentialLandings_RoundTripAsValidVoyageState()
    {
        ShipSystemsRuntime ship = new(RepositoryFixture.Ships, commissioned: true);
        StageOneVoyageRuntime voyage = new();
        for (int cycle = 0; cycle < 100; cycle++)
        {
            ship.Refuel(100000.0);
            Assert.Equal(StageOneVoyageActionResult.Applied, voyage.TryBoard(ship, out _));
            Assert.Equal(StageOneVoyageActionResult.Applied, voyage.TryLaunch(ship, out _));
            voyage.UpdateFlightState(
                StageOneVoyageRuntime.StationDockPositionX,
                StageOneVoyageRuntime.StationDockPositionY,
                StageOneVoyageRuntime.StationDockPositionZ,
                0, 0, 0, 0, 0, 0);
            Assert.Equal(
                StageOneVoyageActionResult.Applied,
                voyage.TryDock(ship, 0.0, 0.0, out _));
            Assert.Equal(StageOneVoyageActionResult.Applied, voyage.TryUndock(ship, out _));
            voyage.UpdateFlightState(
                StageOneVoyageRuntime.SurfacePositionX,
                StageOneVoyageRuntime.LaunchPositionY,
                StageOneVoyageRuntime.SurfacePositionZ,
                0, 0, 0, 0, 0, 0);
            Assert.Equal(
                StageOneVoyageActionResult.Applied,
                voyage.TryLand(ship, 0.0, 0.0, out _));
            Assert.Equal(StageOneVoyageActionResult.Applied, voyage.TryDisembark(out _));
        }

        Assert.Equal(100, voyage.LandingCount);
        Assert.Equal(100, voyage.DockingCount);
        Assert.Equal(100, voyage.TakeoffCount);
        Assert.Equal(100, voyage.CompletedLoops);

        string path = RepositoryFixture.NewTempPath("landings-100.db");
        using SaveDatabase database = new(path);
        await database.InitializeAsync();
        await database.ResetSlotAsync("slot.test");
        SaveGameSnapshot snapshot = SaveDatabase.CreateAcceptanceSnapshot(
            "slot.test", 100, 3.0, 100, 1) with
        {
            ShipSystems = ship.CreateSaveData(),
            StageOneVoyage = voyage.CreateSaveData()
        };
        await database.SaveAsync(snapshot);
        SaveGameSnapshot? restored = await database.LoadAsync("slot.test");
        Assert.NotNull(restored);
        Assert.Equal(100, restored!.StageOneVoyage!.LandingCount);
        Assert.Equal(100, restored.StageOneVoyage.CompletedLoops);
    }

    [Fact]
    public async Task OneHundredHyperspaceDestinations_RemainDeterministicAndUnique()
    {
        string path = RepositoryFixture.NewTempPath("hyperspace-100.db");
        GalaxyNavigationAcceptanceReport report =
            await GalaxyNavigationAcceptanceRunner.RunAsync(
                path,
                "slot.test",
                RepositoryFixture.Ships,
                RepositoryFixture.Content.GetRecipe(StarterRepairContentIds.RecipeId),
                CancellationToken.None);

        Assert.True(report.Passed, report.Result);
        Assert.True(report.Stress100);
        Assert.True(report.DeterministicGeneration);
        Assert.True(report.HyperspaceJump);
        Assert.True(report.FuelDebited);
        Assert.True(report.VisitedPersistence);
        Assert.True(string.Equals(
            report.Diagnostics.IntegrityResult,
            "ok",
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FiveHundredModuleBase_RestoresAtConfiguredHardLimit()
    {
        BaseConstructionCatalog catalog = RepositoryFixture.BaseConstruction;
        BaseModuleDefinition anchor = catalog.Modules.Values.Single(value => value.IsAnchor);
        BaseModuleDefinition structural = catalog.GetModule("module.foundation_square");
        BaseConstructionStockSaveData[] stock = catalog.Modules.Values
            .OrderBy(value => value.ModuleId, StringComparer.Ordinal)
            .Select(value => new BaseConstructionStockSaveData(value.ModuleId, 0))
            .ToArray();
        BaseConstructionModuleSaveData[] modules = Enumerable.Range(1, 500)
            .Select(index => new BaseConstructionModuleSaveData(
                $"base.module.{index:000000}",
                index == 1 ? anchor.ModuleId : structural.ModuleId,
                index - 1,
                0,
                0,
                true))
            .ToArray();
        BaseConstructionRuntime runtime = new(
            catalog,
            new BaseConstructionSaveData(
                BaseConstructionRuntime.DefaultBaseId,
                501,
                0,
                stock,
                modules));

        Assert.Equal(500, runtime.ModuleCount);
        Assert.Equal(500, runtime.Power.Modules);
        Assert.Equal(1, runtime.Power.ConnectedComponents);
        Assert.False(runtime.Power.Modules > catalog.Limits.MaximumModules);
    }

    [Fact]
    public async Task TenThousandInventoryEntries_SaveLoadWithoutLoss()
    {
        string path = RepositoryFixture.NewTempPath("inventory-10000.db");
        string definitionId = RepositoryFixture.Content.Items.Keys
            .OrderBy(id => id, StringComparer.Ordinal)
            .First();
        InventoryItemSaveData[] inventory = Enumerable.Range(1, 10_000)
            .Select(index => new InventoryItemSaveData(
                $"item.stress.{index:000000}",
                definitionId,
                1,
                100.0))
            .ToArray();
        SaveGameSnapshot snapshot = SaveDatabase.CreateAcceptanceSnapshot(
            "slot.test", 10_000, 1.0, 1, 1) with { Inventory = inventory };
        using SaveDatabase database = new(path);
        await database.InitializeAsync();
        await database.ResetSlotAsync("slot.test");
        await database.SaveAsync(snapshot);
        SaveGameSnapshot? restored = await database.LoadAsync("slot.test");

        Assert.NotNull(restored);
        Assert.Equal(10_000, restored!.Inventory.Count);
        Assert.Equal(10_000, restored.Inventory.Select(item => item.ItemId).Distinct().Count());
    }

    [Fact]
    public void OneThousandVisitedSystems_RestoreWithoutIdentityDrift()
    {
        GalaxyNavigationRuntime runtime = new(135_790_246L);
        for (int x = 0; x < 10; x++)
        for (int y = 0; y < 10; y++)
        for (int z = 0; z < 10; z++)
        {
            runtime.LoadSystemForDeveloper(x, y, z);
        }

        Assert.Equal(1000, runtime.VisitedSystemIds.Count);
        GalaxyNavigationSaveData save = runtime.CreateSaveData();
        GalaxyNavigationRuntime restored = new(save);
        Assert.Equal(1000, restored.VisitedSystemIds.Count);
        Assert.Equal(save.CurrentSystemId, restored.CurrentSystem.SystemId);
    }

    [Fact]
    public async Task RepeatedAbnormalRecoveryCycles_KeepIntegrityAndBackupUsable()
    {
        for (int iteration = 0; iteration < 5; iteration++)
        {
            string path = RepositoryFixture.NewTempPath($"recovery-{iteration}.db");
            using SaveDatabase database = new(path);
            SaveRecoveryAcceptanceReport report = await database.RunRecoveryAcceptanceAsync("slot.test");
            Assert.True(report.Passed, $"cycle={iteration}: {report.Result}");
            Assert.True(report.BackupPreserved);
            Assert.True(string.Equals(report.Diagnostics.IntegrityResult, "ok", StringComparison.OrdinalIgnoreCase));
        }
    }

    [FullSoakFact]
    public void OneGigabyteSaveDatabase_CanBeCreatedAndIntegrityChecked()
    {
        string path = RepositoryFixture.NewTempPath("one-gib.db");
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private
        };
        using SqliteConnection connection = new(builder.ToString());
        connection.Open();
        using (SqliteCommand setup = connection.CreateCommand())
        {
            setup.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE load_payload(id INTEGER PRIMARY KEY, payload BLOB NOT NULL);";
            setup.ExecuteNonQuery();
        }
        using (SqliteTransaction transaction = connection.BeginTransaction())
        using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO load_payload(payload) VALUES(zeroblob(1048576));";
            for (int index = 0; index < 1024; index++)
            {
                insert.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        using SqliteCommand integrity = connection.CreateCommand();
        integrity.CommandText = "PRAGMA integrity_check;";
        Assert.Equal("ok", Convert.ToString(integrity.ExecuteScalar()));
        using (SqliteCommand checkpoint = connection.CreateCommand())
        {
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            checkpoint.ExecuteNonQuery();
        }
        connection.Close();
        Assert.True(new FileInfo(path).Length >= 1024L * 1024L * 1024L);
    }
}
