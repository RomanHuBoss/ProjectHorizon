using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

public sealed partial class SaveDatabase
{
    public const string UnknownItemPlaceholderId = "content.unknown.item";
    public const string UnknownShipPlaceholderId = "content.unknown.ship";

    private static readonly object InventoryDefinitionRegistryGate = new();

    private static readonly HashSet<string> KnownInventoryDefinitions = new(
        StringComparer.Ordinal)
    {
        "resource.iron_ore",
        "resource.salvage_alloy",
        "component.starter_hull_patch",
        "resource.conductive_crystal",
        "component.ship.launch_capacitor",
        "resource.phase_fiber",
        "component.ship.navigation_array",
        "component.energy_cell",
        "consumable.repair_kit"
    };

    private static readonly Dictionary<string, string> InventoryDefinitionAliases = new(
        StringComparer.Ordinal)
    {
        ["resource.iron"] = "resource.iron_ore"
    };

    public static void RegisterKnownInventoryDefinitions(
        IEnumerable<string> definitionIds)
    {
        ArgumentNullException.ThrowIfNull(definitionIds);
        lock (InventoryDefinitionRegistryGate)
        {
            foreach (string definitionId in definitionIds)
            {
                if (string.IsNullOrWhiteSpace(definitionId))
                {
                    throw new ArgumentException(
                        "Inventory definition ID must not be empty.",
                        nameof(definitionIds));
                }

                KnownInventoryDefinitions.Add(definitionId);
            }
        }
    }

    private static bool IsKnownInventoryDefinitionCore(string definitionId)
    {
        lock (InventoryDefinitionRegistryGate)
        {
            return KnownInventoryDefinitions.Contains(definitionId);
        }
    }

    private static readonly HashSet<string> KnownShipTemplates = new(
        StringComparer.Ordinal)
    {
        "ship.arcade.prototype",
        "ship.starter.repairable"
    };

    private readonly object _schemaPreparationGate = new();

    public SaveMigrationReport? LastMigrationReport { get; private set; }

    public string MigrationLogPath
    {
        get
        {
            string directory = Path.GetDirectoryName(_databasePath) ??
                throw new InvalidOperationException(
                    "Database parent directory could not be resolved.");
            return Path.Combine(
                directory,
                "logs",
                $"{Path.GetFileNameWithoutExtension(_databasePath)}.migration.log");
        }
    }

    private string MigrationCandidatePath => _databasePath + ".migration-candidate";

    private string MigrationFailedPath => _databasePath + ".migration-failed";

    private void PrepareDatabaseCore(string? slotId)
    {
        EnsureParentDirectory();
        lock (_schemaPreparationGate)
        {
            RecoverPrimaryIfCorruptCore(slotId);
            try
            {
                SaveMigrationReport? report = EnsureCurrentSchemaCopyCore(slotId);
                if (report is not null)
                {
                    LastMigrationReport = report;
                }
            }
            catch (Exception exception)
            {
                try
                {
                    AppendMigrationLogCore(
                        "MIGRATION_FAILED",
                        $"error={exception.GetType().Name}: {exception.Message}");
                }
                catch
                {
                    // Preserve the migration exception if logging is unavailable.
                }

                throw;
            }
        }
    }

    private SaveMigrationReport? EnsureCurrentSchemaCopyCore(string? slotId)
    {
        if (!File.Exists(_databasePath))
        {
            return null;
        }

        int fromSchemaVersion = ReadSchemaVersionFromFileCore(_databasePath);
        if (fromSchemaVersion > CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Save schema {fromSchemaVersion} is newer than supported " +
                $"schema {CurrentSchemaVersion}.");
        }

        if (fromSchemaVersion == CurrentSchemaVersion)
        {
            return null;
        }

        if (fromSchemaVersion < 1)
        {
            throw new InvalidDataException(
                $"Save schema {fromSchemaVersion} cannot be migrated safely.");
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        string? validationSlotId = string.IsNullOrWhiteSpace(slotId)
            ? ReadFirstSlotIdFromFileCore(_databasePath)
            : slotId;
        int fromContentVersion = ReadContentVersionFromFileCore(
            _databasePath,
            validationSlotId);

        // SQLite online backup reads a consistent logical snapshot, including
        // committed WAL pages. Do not checkpoint or otherwise rewrite the legacy
        // source before its immutable copy has been preserved.
        string sourceSha256 = ComputeSha256Core(_databasePath);
        DeleteFileFamilyCore(MigrationCandidatePath);
        DeleteFileFamilyCore(MigrationFailedPath);

        using (SqliteConnection sourceConnection = OpenConnectionForPathCore(
                   _databasePath,
                   readOnly: true))
        using (SqliteConnection destinationConnection = OpenConnectionForPathCore(
                   MigrationCandidatePath,
                   readOnly: false))
        {
            sourceConnection.BackupDatabase(destinationConnection);
        }

        MigrationTransformSummary transform;
        using (SqliteConnection candidateConnection = OpenConnectionForPathCore(
                   MigrationCandidatePath,
                   readOnly: false))
        {
            transform = ApplyMigrations(candidateConnection);
            ExecutePragma(candidateConnection, "PRAGMA wal_checkpoint(TRUNCATE);");
        }

        DeleteSidecarsCore(MigrationCandidatePath);
        bool requireSnapshot = !string.IsNullOrWhiteSpace(validationSlotId);
        SaveFileInspection candidate = InspectDatabaseFileCore(
            MigrationCandidatePath,
            validationSlotId,
            requireSnapshot);
        if (!candidate.IsValid || candidate.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Migrated candidate is invalid: {candidate.Error}");
        }

        string sourceHashBeforeInstall = ComputeSha256Core(_databasePath);
        if (!string.Equals(
                sourceSha256,
                sourceHashBeforeInstall,
                StringComparison.Ordinal))
        {
            throw new IOException(
                "Legacy source changed while the migration candidate was being prepared.");
        }

        string preservedSourcePath = ResolvePreservedSourcePathCore(
            fromSchemaVersion);
        MoveSidecarsCore(_databasePath, preservedSourcePath);
        bool atomicReplacement = File.Exists(_databasePath);

        try
        {
            if (atomicReplacement)
            {
                File.Replace(
                    MigrationCandidatePath,
                    _databasePath,
                    preservedSourcePath,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(MigrationCandidatePath, _databasePath);
            }

            DeleteSidecarsCore(_databasePath);
            SaveFileInspection installed = InspectDatabaseFileCore(
                _databasePath,
                validationSlotId,
                requireSnapshot);
            if (!installed.IsValid ||
                installed.SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Installed migrated database is invalid: {installed.Error}");
            }
        }
        catch
        {
            TryRollbackMigrationCore(preservedSourcePath);
            throw;
        }

        SaveFileInspection preservedSource = InspectDatabaseFileCore(
            preservedSourcePath,
            validationSlotId,
            requireSnapshot);
        bool sourcePreserved = atomicReplacement &&
            preservedSource.IsValid &&
            preservedSource.SchemaVersion == fromSchemaVersion;
        string preservedSha256 = sourcePreserved
            ? ComputeSha256Core(preservedSourcePath)
            : string.Empty;
        if (!sourcePreserved ||
            !string.Equals(sourceSha256, preservedSha256, StringComparison.Ordinal))
        {
            TryRollbackMigrationCore(preservedSourcePath);
            throw new InvalidDataException(
                "Migration completed but the immutable legacy source copy was not preserved.");
        }

        stopwatch.Stop();
        SaveMigrationReport report = new(
            true,
            true,
            "legacy save migrated on a validated copy; original preserved",
            fromSchemaVersion,
            CurrentSchemaVersion,
            fromContentVersion,
            CurrentContentVersion,
            preservedSourcePath,
            true,
            atomicReplacement,
            transform.AliasedReferences,
            transform.PlaceholderReferences,
            sourceSha256,
            preservedSha256,
            stopwatch.Elapsed.TotalMilliseconds);

        AppendMigrationLogCore(
            "MIGRATION_COMPLETED",
            $"fromSchema={report.FromSchemaVersion}; " +
            $"toSchema={report.ToSchemaVersion}; " +
            $"fromContent={report.FromContentVersion}; " +
            $"toContent={report.ToContentVersion}; " +
            $"aliases={report.AliasedReferences}; " +
            $"placeholders={report.PlaceholderReferences}; " +
            $"atomic={(report.AtomicReplacementUsed ? 1 : 0)}; " +
            $"preserved={report.PreservedSourcePath}; " +
            $"sourceSha256={report.SourceSha256}");
        return report;
    }

    public async Task<SaveMigrationAcceptanceReport> RunMigrationAcceptanceAsync(
        string slotId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slotId))
        {
            throw new ArgumentException(
                "Slot ID must not be empty.",
                nameof(slotId));
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        string testPath = BuildSiblingPath(".migration-test.db");
        using SaveDatabase testDatabase = new(testPath);
        int exactContentChecks = 0;

        try
        {
            await Task.Run(
                () =>
                {
                    testDatabase.DeleteDatabaseFamilyCore();
                    DeleteIfExistsCore(testDatabase.MigrationLogPath);
                    testDatabase.CreateLegacySchema1FixtureCore(slotId);
                },
                cancellationToken).ConfigureAwait(false);

            CheckpointDatabaseCore(testDatabase.DatabasePath);
            string legacySourceHash = ComputeSha256Core(testDatabase.DatabasePath);
            await testDatabase.InitializeAsync(cancellationToken).ConfigureAwait(false);
            SaveMigrationReport migration = testDatabase.LastMigrationReport ??
                throw new InvalidOperationException(
                    "The legacy fixture did not trigger a migration report.");

            SaveGameSnapshot? migratedSnapshot = await testDatabase.LoadAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);
            if (migratedSnapshot is null)
            {
                throw new InvalidDataException(
                    "Migrated snapshot is missing.");
            }

            bool legacySourceUnchanged =
                migration.SourcePreserved &&
                File.Exists(migration.PreservedSourcePath) &&
                string.Equals(
                    legacySourceHash,
                    ComputeSha256Core(migration.PreservedSourcePath),
                    StringComparison.Ordinal) &&
                string.Equals(
                    migration.SourceSha256,
                    migration.PreservedSha256,
                    StringComparison.Ordinal);
            if (legacySourceUnchanged)
            {
                exactContentChecks++;
            }

            InventoryItemSaveData aliasItem = migratedSnapshot.Inventory.Single(
                item => item.ItemId == "item.alias");
            bool aliasResolved =
                aliasItem.DefinitionId == "resource.iron_ore" &&
                aliasItem.OriginalDefinitionId == "resource.iron" &&
                aliasItem.Resolution == ContentResolutionState.Aliased &&
                aliasItem.Quantity == 7 &&
                NearlyEqual(aliasItem.Durability, 0.88);
            if (aliasResolved)
            {
                exactContentChecks++;
            }

            InventoryItemSaveData unknownItem = migratedSnapshot.Inventory.Single(
                item => item.ItemId == "item.unknown");
            bool unknownItemPreserved =
                unknownItem.DefinitionId == UnknownItemPlaceholderId &&
                unknownItem.OriginalDefinitionId == "item.removed.prototype" &&
                unknownItem.Resolution == ContentResolutionState.Placeholder &&
                unknownItem.Quantity == 3 &&
                NearlyEqual(unknownItem.Durability, 0.41);
            if (unknownItemPreserved)
            {
                exactContentChecks++;
            }

            bool unknownShipPreserved =
                migratedSnapshot.Ship.TemplateId == UnknownShipPlaceholderId &&
                migratedSnapshot.Ship.OriginalTemplateId == "ship.removed.prototype" &&
                migratedSnapshot.Ship.TemplateResolution ==
                    ContentResolutionState.Placeholder &&
                NearlyEqual(migratedSnapshot.Ship.Health, 73.5) &&
                NearlyEqual(migratedSnapshot.Ship.Fuel, 48.25);
            if (unknownShipPreserved)
            {
                exactContentChecks++;
            }

            bool versionsUpdated =
                migratedSnapshot.ContentVersion == CurrentContentVersion &&
                migration.FromSchemaVersion == 1 &&
                migration.ToSchemaVersion == CurrentSchemaVersion &&
                migration.FromContentVersion == 1 &&
                migration.ToContentVersion == CurrentContentVersion &&
                migration.AliasedReferences == 1 &&
                migration.PlaceholderReferences == 2;
            if (versionsUpdated)
            {
                exactContentChecks++;
            }

            SaveGameSnapshot roundTripSource = migratedSnapshot with
            {
                Revision = migratedSnapshot.Revision + 1,
                UpdatedUtc = DateTimeOffset.UtcNow.ToString(
                    "O",
                    CultureInfo.InvariantCulture)
            };
            await testDatabase.SaveAsync(
                roundTripSource,
                cancellationToken).ConfigureAwait(false);
            SaveGameSnapshot? roundTripSnapshot = await testDatabase.LoadAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);

            bool roundTripPreserved = roundTripSnapshot is not null &&
                roundTripSnapshot.Revision == roundTripSource.Revision &&
                roundTripSnapshot.Inventory.Single(
                    item => item.ItemId == "item.alias").OriginalDefinitionId ==
                    "resource.iron" &&
                roundTripSnapshot.Inventory.Single(
                    item => item.ItemId == "item.unknown").OriginalDefinitionId ==
                    "item.removed.prototype" &&
                roundTripSnapshot.Ship.OriginalTemplateId ==
                    "ship.removed.prototype";
            if (roundTripPreserved)
            {
                exactContentChecks++;
            }

            SaveDatabaseDiagnostics diagnostics =
                await testDatabase.ReadDiagnosticsAsync(
                    slotId,
                    cancellationToken).ConfigureAwait(false);
            bool passed =
                legacySourceUnchanged &&
                aliasResolved &&
                unknownItemPreserved &&
                unknownShipPreserved &&
                versionsUpdated &&
                roundTripPreserved &&
                migration.Succeeded &&
                migration.AtomicReplacementUsed &&
                diagnostics.SchemaVersion == CurrentSchemaVersion &&
                string.Equals(
                    diagnostics.IntegrityResult,
                    "ok",
                    StringComparison.OrdinalIgnoreCase) &&
                exactContentChecks == 6;

            stopwatch.Stop();
            SaveMigrationAcceptanceReport report = new(
                passed,
                passed
                    ? "copy migration preserved the legacy source and resolved unknown content safely"
                    : "one or more migration/content compatibility criteria failed",
                roundTripSnapshot,
                diagnostics,
                migration,
                legacySourceUnchanged,
                aliasResolved,
                unknownItemPreserved,
                unknownShipPreserved,
                roundTripPreserved,
                exactContentChecks,
                stopwatch.Elapsed.TotalMilliseconds);

            await Task.Run(
                testDatabase.DeleteDatabaseFamilyCore,
                CancellationToken.None).ConfigureAwait(false);
            return report;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            try
            {
                await Task.Run(
                    testDatabase.DeleteDatabaseFamilyCore,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Preserve the original acceptance exception.
            }

            return new SaveMigrationAcceptanceReport(
                false,
                $"{exception.GetType().Name}: {exception.Message}",
                null,
                testDatabase.EmptyDiagnostics(),
                testDatabase.LastMigrationReport ?? EmptyMigrationReport(
                    exception.Message,
                    stopwatch.Elapsed.TotalMilliseconds),
                false,
                false,
                false,
                false,
                false,
                exactContentChecks,
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private void CreateLegacySchema1FixtureCore(string slotId)
    {
        EnsureParentDirectory();
        using SqliteConnection connection = OpenConnectionForPathCore(
            _databasePath,
            readOnly: false);
        using (SqliteCommand bootstrap = connection.CreateCommand())
        {
            bootstrap.CommandText =
                "CREATE TABLE schema_migrations (" +
                "version INTEGER PRIMARY KEY, applied_utc TEXT NOT NULL);";
            bootstrap.ExecuteNonQuery();
        }

        ApplyMigration1(connection);
        string updatedUtc = DateTimeOffset.UtcNow.ToString(
            "O",
            CultureInfo.InvariantCulture);
        using SqliteTransaction transaction = connection.BeginTransaction();

        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO save_meta(" +
            "slot_id, schema_version, save_revision, generator_version, " +
            "content_version, created_utc, updated_utc) " +
            "VALUES($slot_id, 1, 20, 1, 1, $utc, $utc);",
            ("$slot_id", slotId),
            ("$utc", updatedUtc));
        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO player_state(" +
            "slot_id, player_id, pos_x, pos_y, pos_z, current_planet_id) " +
            "VALUES($slot_id, 'player.legacy', 11.5, 22.5, -7.25, 'planet.prototype');",
            ("$slot_id", slotId));
        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO ships(" +
            "ship_id, slot_id, template_id, display_name, health, fuel, " +
            "pos_x, pos_y, pos_z) VALUES(" +
            "'ship.legacy', $slot_id, 'ship.removed.prototype', " +
            "'Legacy Horizon', 73.5, 48.25, 15.0, 30.0, -9.0);",
            ("$slot_id", slotId));

        string containerId = $"{slotId}.player_inventory";
        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO containers(container_id, slot_id, owner_type, owner_id, capacity) " +
            "VALUES($container_id, $slot_id, 'player', 'player.legacy', 64);",
            ("$container_id", containerId),
            ("$slot_id", slotId));
        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO inventory_items(" +
            "container_id, item_id, definition_id, quantity, durability) " +
            "VALUES($container_id, 'item.known', 'resource.iron_ore', 12, 1.0);",
            ("$container_id", containerId));
        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO inventory_items(" +
            "container_id, item_id, definition_id, quantity, durability) " +
            "VALUES($container_id, 'item.alias', 'resource.iron', 7, 0.88);",
            ("$container_id", containerId));
        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO inventory_items(" +
            "container_id, item_id, definition_id, quantity, durability) " +
            "VALUES($container_id, 'item.unknown', 'item.removed.prototype', 3, 0.41);",
            ("$container_id", containerId));
        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO visited_planets(" +
            "slot_id, planet_id, system_id, first_visited_utc, visit_count) " +
            "VALUES($slot_id, 'planet.prototype', 'system.prototype', $utc, 2);",
            ("$slot_id", slotId),
            ("$utc", updatedUtc));
        transaction.Commit();
        ExecutePragma(connection, "PRAGMA wal_checkpoint(TRUNCATE);");
    }

    private static ContentResolution ResolveInventoryDefinitionCore(
        string persistedDefinitionId,
        string? originalDefinitionId)
    {
        string original = string.IsNullOrWhiteSpace(originalDefinitionId)
            ? string.Empty
            : originalDefinitionId;

        if (persistedDefinitionId == UnknownItemPlaceholderId &&
            !string.IsNullOrWhiteSpace(original))
        {
            return new ContentResolution(
                UnknownItemPlaceholderId,
                original,
                ContentResolutionState.Placeholder);
        }

        if (IsKnownInventoryDefinitionCore(persistedDefinitionId))
        {
            if (!string.IsNullOrWhiteSpace(original) &&
                InventoryDefinitionAliases.TryGetValue(
                    original,
                    out string? target) &&
                string.Equals(
                    target,
                    persistedDefinitionId,
                    StringComparison.Ordinal))
            {
                return new ContentResolution(
                    persistedDefinitionId,
                    original,
                    ContentResolutionState.Aliased);
            }

            return new ContentResolution(
                persistedDefinitionId,
                string.Empty,
                ContentResolutionState.Known);
        }

        if (InventoryDefinitionAliases.TryGetValue(
                persistedDefinitionId,
                out string? aliasedDefinition) &&
            !string.IsNullOrWhiteSpace(aliasedDefinition))
        {
            return new ContentResolution(
                aliasedDefinition,
                persistedDefinitionId,
                ContentResolutionState.Aliased);
        }

        return new ContentResolution(
            UnknownItemPlaceholderId,
            string.IsNullOrWhiteSpace(original)
                ? persistedDefinitionId
                : original,
            ContentResolutionState.Placeholder);
    }

    private static ContentResolution ResolveShipTemplateCore(
        string persistedTemplateId,
        string? originalTemplateId)
    {
        string original = string.IsNullOrWhiteSpace(originalTemplateId)
            ? string.Empty
            : originalTemplateId;

        if (persistedTemplateId == UnknownShipPlaceholderId &&
            !string.IsNullOrWhiteSpace(original))
        {
            return new ContentResolution(
                UnknownShipPlaceholderId,
                original,
                ContentResolutionState.Placeholder);
        }

        if (KnownShipTemplates.Contains(persistedTemplateId))
        {
            return new ContentResolution(
                persistedTemplateId,
                string.Empty,
                ContentResolutionState.Known);
        }

        return new ContentResolution(
            UnknownShipPlaceholderId,
            string.IsNullOrWhiteSpace(original)
                ? persistedTemplateId
                : original,
            ContentResolutionState.Placeholder);
    }

    private static string PersistedInventoryDefinitionCore(
        InventoryItemSaveData item)
    {
        return item.Resolution == ContentResolutionState.Placeholder
            ? UnknownItemPlaceholderId
            : item.DefinitionId;
    }

    private static object PersistedInventoryOriginalCore(
        InventoryItemSaveData item)
    {
        return item.Resolution == ContentResolutionState.Known ||
            string.IsNullOrWhiteSpace(item.OriginalDefinitionId)
            ? DBNull.Value
            : item.OriginalDefinitionId;
    }

    private static string PersistedShipTemplateCore(ShipSaveData ship)
    {
        return ship.TemplateResolution == ContentResolutionState.Placeholder
            ? UnknownShipPlaceholderId
            : ship.TemplateId;
    }

    private static object PersistedShipOriginalCore(ShipSaveData ship)
    {
        return ship.TemplateResolution == ContentResolutionState.Known ||
            string.IsNullOrWhiteSpace(ship.OriginalTemplateId)
            ? DBNull.Value
            : ship.OriginalTemplateId;
    }

    private static int ReadSchemaVersionFromFileCore(string path)
    {
        using SqliteConnection connection = OpenConnectionForPathCore(
            path,
            readOnly: true);
        return ExecuteScalarInt(
            connection,
            "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;");
    }

    private static int ReadContentVersionFromFileCore(
        string path,
        string? slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId))
        {
            return 0;
        }

        using SqliteConnection connection = OpenConnectionForPathCore(
            path,
            readOnly: true);
        return ExecuteScalarInt(
            connection,
            "SELECT COALESCE(content_version, 0) FROM save_meta " +
            "WHERE slot_id = $slot_id;",
            ("$slot_id", slotId));
    }

    private static string? ReadFirstSlotIdFromFileCore(string path)
    {
        using SqliteConnection connection = OpenConnectionForPathCore(
            path,
            readOnly: true);
        string slotId = ExecuteScalarString(
            connection,
            "SELECT slot_id FROM save_meta ORDER BY slot_id LIMIT 1;");
        return string.IsNullOrWhiteSpace(slotId) ? null : slotId;
    }

    private static void CheckpointDatabaseCore(string path)
    {
        using SqliteConnection connection = OpenConnectionForPathCore(
            path,
            readOnly: false);
        ExecutePragma(connection, "PRAGMA wal_checkpoint(TRUNCATE);");
    }

    private static SqliteConnection OpenConnectionForPathCore(
        string path,
        bool readOnly)
    {
        SqliteConnection connection = new(
            $"Data Source={path};Mode={(readOnly ? "ReadOnly" : "ReadWriteCreate")};" +
            "Cache=Private;Pooling=False");
        connection.Open();
        ExecutePragma(
            connection,
            $"PRAGMA busy_timeout = {BusyTimeoutMilliseconds};");
        if (!readOnly)
        {
            ExecutePragma(connection, "PRAGMA journal_mode = WAL;");
            ExecutePragma(connection, "PRAGMA foreign_keys = ON;");
            ExecutePragma(connection, "PRAGMA synchronous = NORMAL;");
        }

        return connection;
    }

    private string ResolvePreservedSourcePathCore(int fromSchemaVersion)
    {
        string basePath = BuildSiblingPath(
            $".pre-migration.v{fromSchemaVersion}.db");
        if (!File.Exists(basePath))
        {
            return basePath;
        }

        string timestamp = DateTimeOffset.UtcNow.ToString(
            "yyyyMMddTHHmmssfffZ",
            CultureInfo.InvariantCulture);
        return BuildSiblingPath(
            $".pre-migration.v{fromSchemaVersion}.{timestamp}.db");
    }

    private void TryRollbackMigrationCore(string preservedSourcePath)
    {
        try
        {
            if (File.Exists(preservedSourcePath))
            {
                DeleteFileFamilyCore(MigrationFailedPath);
                DeleteSidecarsCore(_databasePath);
                if (File.Exists(_databasePath))
                {
                    File.Replace(
                        preservedSourcePath,
                        _databasePath,
                        MigrationFailedPath,
                        ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(preservedSourcePath, _databasePath);
                }
            }

            // Sidecars are moved before File.Replace. Restore them even when
            // File.Replace fails before the preserved main database is created.
            MoveSidecarsCore(preservedSourcePath, _databasePath);
        }
        catch (Exception rollbackException)
        {
            AppendMigrationLogCore(
                "MIGRATION_ROLLBACK_FAILED",
                $"error={rollbackException.GetType().Name}: {rollbackException.Message}");
        }
    }

    private static void MoveSidecarsCore(string sourcePath, string destinationPath)
    {
        MoveIfExistsCore(sourcePath + "-wal", destinationPath + "-wal");
        MoveIfExistsCore(sourcePath + "-shm", destinationPath + "-shm");
    }

    private void AppendMigrationLogCore(string eventName, string details)
    {
        string? directory = Path.GetDirectoryName(MigrationLogPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "Migration log directory could not be resolved.");
        }

        Directory.CreateDirectory(directory);
        string line =
            $"{DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)} " +
            $"{eventName} {details}{Environment.NewLine}";
        File.AppendAllText(MigrationLogPath, line);
    }

    private void DeleteMigrationArtifactsCore()
    {
        DeleteFileFamilyCore(MigrationCandidatePath);
        DeleteFileFamilyCore(MigrationFailedPath);

        string directory = Path.GetDirectoryName(_databasePath) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        string stem = Path.GetFileNameWithoutExtension(_databasePath);
        foreach (string path in Directory.EnumerateFiles(
                     directory,
                     $"{stem}.pre-migration.*")
                 .Where(path =>
                     !path.EndsWith("-wal", StringComparison.Ordinal) &&
                     !path.EndsWith("-shm", StringComparison.Ordinal))
                 .ToArray())
        {
            DeleteFileFamilyCore(path);
        }
    }

    private static SaveMigrationReport EmptyMigrationReport(
        string result,
        double elapsedMilliseconds)
    {
        return new SaveMigrationReport(
            false,
            false,
            result,
            0,
            CurrentSchemaVersion,
            0,
            CurrentContentVersion,
            string.Empty,
            false,
            false,
            0,
            0,
            string.Empty,
            string.Empty,
            elapsedMilliseconds);
    }

    private sealed record ContentResolution(
        string EffectiveId,
        string OriginalId,
        ContentResolutionState State);

    private sealed record MigrationTransformSummary(
        int AliasedReferences,
        int PlaceholderReferences)
    {
        public static MigrationTransformSummary Empty { get; } = new(0, 0);
    }
}
