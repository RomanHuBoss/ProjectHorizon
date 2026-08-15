using Microsoft.Data.Sqlite;
using System.Text.Json;
using ProjectHorizon.Tests.Support;

namespace ProjectHorizon.Tests.Persistence;

public sealed class PersistenceTests
{
    public PersistenceTests()
    {
        SaveDatabase.RegisterKnownInventoryDefinitions(RepositoryFixture.Content.Items.Keys);
    }

    [Fact]
    public async Task NormalSave_RoundTripsExactlyAndSerializesWriters()
    {
        string path = RepositoryFixture.NewTempPath("normal.db");
        using SaveDatabase database = new(path);
        SavePrototypeAcceptanceReport report = await database.RunAcceptanceAsync("slot.test");

        Assert.True(report.Passed, report.Result);
        Assert.Equal(2, report.ExactComparisons);
        Assert.Equal(1, report.Diagnostics.MaximumConcurrentWriters);
        Assert.True(string.Equals(report.Diagnostics.IntegrityResult, "ok", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BackupRecovery_CorruptPrimaryRestoresProtectedSnapshot()
    {
        string path = RepositoryFixture.NewTempPath("recovery.db");
        using SaveDatabase database = new(path);
        SaveRecoveryAcceptanceReport report = await database.RunRecoveryAcceptanceAsync("slot.test");

        Assert.True(report.Passed, report.Result);
        Assert.True(report.CorruptionDetected);
        Assert.True(report.BackupPreserved);
        Assert.True(report.AtomicReplacementUsed);
        Assert.True(report.QuarantinePreserved);
        Assert.True(string.Equals(report.Diagnostics.IntegrityResult, "ok", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LegacyMigration_PreservesSourceAliasesUnknownContentAndRoundTrip()
    {
        string path = RepositoryFixture.NewTempPath("migration.db");
        using SaveDatabase database = new(path);
        SaveMigrationAcceptanceReport report = await database.RunMigrationAcceptanceAsync("slot.test");

        Assert.True(report.Passed, report.Result);
        Assert.True(report.LegacySourceUnchanged);
        Assert.True(report.AliasResolved);
        Assert.True(report.UnknownItemPreserved);
        Assert.True(report.UnknownShipPreserved);
        Assert.True(report.RoundTripPreserved);
        Assert.Equal(SaveDatabase.CurrentSchemaVersion, report.Migration.ToSchemaVersion);
        Assert.Equal(SaveDatabase.CurrentContentVersion, report.Migration.ToContentVersion);
    }

    [Fact]
    public async Task Autosave_CoversAllTriggersAndGracefulExitFlush()
    {
        string path = RepositoryFixture.NewTempPath("autosave.db");
        using SaveDatabase database = new(path);
        SaveAutosaveAcceptanceReport report = await database.RunAutosaveAcceptanceAsync("slot.test");

        Assert.True(report.Passed, report.Result);
        Assert.True(report.PeriodicTriggered);
        Assert.True(report.GracefulExitFlushed);
        Assert.True(report.ExactRoundTrip);
        Assert.True(report.LogWritten);
        Assert.Equal(Enum.GetValues<AutosaveTrigger>().Length, report.TriggerTypesCovered);
    }

    [Fact]
    public async Task ShutdownDuringSave_UncommittedTransactionCannotReplaceLastCommittedRevision()
    {
        string path = RepositoryFixture.NewTempPath("interrupted.db");
        SaveGameSnapshot baseline = SaveDatabase.CreateAcceptanceSnapshot(
            "slot.test", 10, 1.0, 12, 1);

        using (SaveDatabase database = new(path))
        {
            await database.InitializeAsync();
            await database.ResetSlotAsync("slot.test");
            await database.SaveAsync(baseline);
        }

        // Model abrupt termination after SQLite has accepted writes inside a
        // transaction but before the save can commit. Disposing the connection
        // without Commit must roll the transaction back atomically.
        using (SqliteConnection connection = new($"Data Source={path};Mode=ReadWrite"))
        {
            connection.Open();
            using SqliteTransaction transaction = connection.BeginTransaction();
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "UPDATE save_meta SET save_revision=999 WHERE slot_id=$slot_id;";
            command.Parameters.AddWithValue("$slot_id", "slot.test");
            Assert.Equal(1, command.ExecuteNonQuery());
            // Intentionally no Commit(): this is the simulated abnormal shutdown.
        }

        using SaveDatabase restoredDatabase = new(path);
        await restoredDatabase.InitializeAsync();
        SaveGameSnapshot? loaded = await restoredDatabase.LoadAsync("slot.test");
        Assert.NotNull(loaded);
        Assert.Equal(10, loaded!.Revision);
        Assert.True(SaveDatabase.SnapshotsEqual(baseline, loaded, out string mismatch), mismatch);
        SaveDatabaseDiagnostics diagnostics = await restoredDatabase.ReadDiagnosticsAsync("slot.test");
        Assert.True(string.Equals(
            diagnostics.IntegrityResult,
            "ok",
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Serialization_RoundTripsCompleteSnapshotWithoutLosingOptionalState()
    {
        SaveGameSnapshot source = SaveDatabase.CreateAcceptanceSnapshot(
            "slot.test", 42, 12.5, 77, 9) with
        {
            TechnologyProgress = new TechnologyProgressSaveData(
                1234,
                new[] { "technology.basic_refining" }),
            GalaxyNavigation = new GalaxyNavigationRuntime(7_777_777L).CreateSaveData(),
            PlayerSurvival = new PlayerSurvivalSaveData(
                91, 42, 83, 74, 61, 88, 47, 39,
                "Mining",
                Array.Empty<string>(),
                Array.Empty<string>())
        };

        string json = JsonSerializer.Serialize(source);
        SaveGameSnapshot? restored = JsonSerializer.Deserialize<SaveGameSnapshot>(json);

        Assert.NotNull(restored);
        Assert.True(SaveDatabase.SnapshotsEqual(source, restored, out string mismatch), mismatch);
    }

    [Fact]
    public void RemovedTechnology_DoesNotInvalidateContentChangedSave()
    {
        GameContentCatalog content = RepositoryFixture.Content;
        TechnologyProgressSaveData legacy = new(
            100,
            new[] { "technology.basic_refining", "technology.removed.prototype" });

        TechnologyProgression restored = TechnologyProgression.FromSaveData(
            content.Technologies,
            legacy,
            defaultResearchPoints: 0);

        Assert.Contains("technology.removed.prototype", restored.IgnoredUnknownTechnologyIds);
        Assert.DoesNotContain("technology.removed.prototype", restored.ToSaveData().UnlockedTechnologyIds);
    }
}
