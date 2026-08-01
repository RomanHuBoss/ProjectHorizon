using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

public sealed partial class SaveDatabase
{
    public string BackupPath => BuildSiblingPath(".backup.db");

    public string RecoveryLogPath
    {
        get
        {
            string directory = Path.GetDirectoryName(_databasePath) ??
                throw new InvalidOperationException(
                    "Database parent directory could not be resolved.");
            return Path.Combine(
                directory,
                "logs",
                $"{Path.GetFileNameWithoutExtension(_databasePath)}.recovery.log");
        }
    }

    private string BackupCandidatePath => BackupPath + ".candidate";

    private string BackupPreviousPath => BackupPath + ".previous";

    private string BackupFailedPath => BackupPath + ".failed";

    private string RecoveryCandidatePath => _databasePath + ".recovery-candidate";

    private string RecoveryQuarantinePath => BuildSiblingPath(".quarantine.last.db");

    public Task<SaveBackupReport> CreateBackupAsync(
        string slotId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slotId))
        {
            throw new ArgumentException(
                "Slot ID must not be empty.",
                nameof(slotId));
        }

        return EnqueueWriteAsync(
            () =>
            {
                PrepareDatabaseCore(slotId);
                using SqliteConnection connection = OpenConnection();
                ApplyMigrations(connection);
                if (!TryLoadSnapshotCore(connection, slotId, out SaveGameSnapshot? snapshot) ||
                    snapshot is null)
                {
                    throw new InvalidOperationException(
                        $"Slot {slotId} is empty; there is nothing to back up.");
                }

                Stopwatch stopwatch = Stopwatch.StartNew();
                bool atomicReplacement = CreateValidatedBackupCore(
                    connection,
                    slotId);
                stopwatch.Stop();
                SaveFileInspection backup = InspectDatabaseFileCore(
                    BackupPath,
                    slotId,
                    requireSnapshot: true);
                return new SaveBackupReport(
                    true,
                    "validated backup created",
                    BackupPath,
                    backup.Snapshot,
                    backup.IntegrityResult,
                    backup.Bytes,
                    atomicReplacement,
                    ComputeSha256Core(BackupPath),
                    stopwatch.Elapsed.TotalMilliseconds);
            },
            cancellationToken);
    }

    public Task<SaveRecoveryReport> RestoreBackupAsync(
        string slotId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slotId))
        {
            throw new ArgumentException(
                "Slot ID must not be empty.",
                nameof(slotId));
        }

        return EnqueueWriteAsync(
            () => RestoreBackupCore(
                slotId,
                force: true,
                reason: "manual previous-copy restore"),
            cancellationToken);
    }

    public async Task<SaveRecoveryAcceptanceReport> RunRecoveryAcceptanceAsync(
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
        string testPath = BuildSiblingPath(".recovery-test.db");
        using SaveDatabase testDatabase = new(testPath);
        int exactComparisons = 0;

        try
        {
            await Task.Run(
                () =>
                {
                    testDatabase.DeleteDatabaseFamilyCore();
                    DeleteIfExistsCore(testDatabase.RecoveryLogPath);
                },
                cancellationToken).ConfigureAwait(false);
            await testDatabase.InitializeAsync(cancellationToken).ConfigureAwait(false);

            SaveGameSnapshot protectedSnapshot = CreateAcceptanceSnapshot(
                slotId,
                revision: 10,
                playerOffset: 4.0,
                oreQuantity: 31,
                visitCount: 2);
            await testDatabase.SaveAsync(
                protectedSnapshot,
                cancellationToken).ConfigureAwait(false);

            SaveGameSnapshot newerSnapshot = CreateAcceptanceSnapshot(
                slotId,
                revision: 11,
                playerOffset: 9.0,
                oreQuantity: 44,
                visitCount: 4);
            await testDatabase.SaveAsync(
                newerSnapshot,
                cancellationToken).ConfigureAwait(false);

            SaveFileInspection protectedBackup =
                testDatabase.InspectDatabaseFileCore(
                    testDatabase.BackupPath,
                    slotId,
                    requireSnapshot: true);
            if (!protectedBackup.IsValid)
            {
                return testDatabase.BuildRecoveryAcceptanceFailure(
                    $"protected backup invalid: {protectedBackup.Error}",
                    stopwatch,
                    exactComparisons);
            }

            if (!SnapshotsEqual(
                    protectedSnapshot,
                    protectedBackup.Snapshot,
                    out string backupMismatch))
            {
                return testDatabase.BuildRecoveryAcceptanceFailure(
                    $"protected backup mismatch: {backupMismatch}",
                    stopwatch,
                    exactComparisons);
            }

            exactComparisons++;
            string backupHashBefore = ComputeSha256Core(testDatabase.BackupPath);

            bool candidateRejected = false;
            await Task.Run(
                () =>
                {
                    testDatabase.DeleteFileFamilyCore(
                        testDatabase.BackupCandidatePath);
                    File.WriteAllBytes(
                        testDatabase.BackupCandidatePath,
                        Encoding.UTF8.GetBytes(
                            "PROJECT_HORIZON_INVALID_BACKUP_CANDIDATE"));
                    try
                    {
                        testDatabase.InstallValidatedBackupCandidateCore(
                            slotId,
                            requireSnapshot: true);
                    }
                    catch (InvalidDataException)
                    {
                        candidateRejected = true;
                    }
                },
                cancellationToken).ConfigureAwait(false);

            string backupHashAfterRejectedCandidate =
                ComputeSha256Core(testDatabase.BackupPath);
            bool backupPreservedAfterRejection =
                string.Equals(
                    backupHashBefore,
                    backupHashAfterRejectedCandidate,
                    StringComparison.Ordinal);

            await Task.Run(
                testDatabase.CorruptPrimaryForAcceptanceCore,
                cancellationToken).ConfigureAwait(false);
            SaveFileInspection corruptPrimary =
                testDatabase.InspectDatabaseFileCore(
                    testDatabase.DatabasePath,
                    slotId,
                    requireSnapshot: true);
            bool corruptionDetected = !corruptPrimary.IsValid;

            SaveRecoveryReport recovery = await testDatabase.RestoreBackupAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);
            SaveGameSnapshot? restoredSnapshot = await testDatabase.LoadAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);
            if (!SnapshotsEqual(
                    protectedSnapshot,
                    restoredSnapshot,
                    out string restoredMismatch))
            {
                return testDatabase.BuildRecoveryAcceptanceFailure(
                    $"restored snapshot mismatch: {restoredMismatch}",
                    stopwatch,
                    exactComparisons);
            }

            exactComparisons++;
            string backupHashAfterRecovery =
                ComputeSha256Core(testDatabase.BackupPath);
            bool backupPreserved =
                backupPreservedAfterRejection &&
                string.Equals(
                    backupHashBefore,
                    backupHashAfterRecovery,
                    StringComparison.Ordinal);
            bool quarantinePreserved =
                recovery.AtomicReplacementUsed &&
                !string.IsNullOrWhiteSpace(recovery.QuarantinePath) &&
                File.Exists(recovery.QuarantinePath);
            bool recoveryLogWritten = File.Exists(testDatabase.RecoveryLogPath) &&
                File.ReadAllText(testDatabase.RecoveryLogPath)
                    .Contains("RECOVERY_COMPLETED", StringComparison.Ordinal);
            SaveDatabaseDiagnostics diagnostics =
                await testDatabase.ReadDiagnosticsAsync(
                    slotId,
                    cancellationToken).ConfigureAwait(false);

            bool passed =
                candidateRejected &&
                backupPreserved &&
                corruptionDetected &&
                recovery.Recovered &&
                recovery.AtomicReplacementUsed &&
                quarantinePreserved &&
                recoveryLogWritten &&
                string.Equals(
                    diagnostics.IntegrityResult,
                    "ok",
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    diagnostics.BackupIntegrityResult,
                    "ok",
                    StringComparison.OrdinalIgnoreCase) &&
                restoredSnapshot?.Revision == protectedSnapshot.Revision &&
                newerSnapshot.Revision > protectedSnapshot.Revision &&
                exactComparisons == 2;

            stopwatch.Stop();
            SaveRecoveryAcceptanceReport report = new(
                passed,
                passed
                    ? "previous-copy backup survived rejection and restored the corrupted primary"
                    : "one or more backup/recovery criteria failed",
                restoredSnapshot,
                diagnostics,
                protectedSnapshot.Revision,
                newerSnapshot.Revision,
                candidateRejected,
                backupPreserved,
                corruptionDetected,
                recovery.AtomicReplacementUsed,
                quarantinePreserved,
                recoveryLogWritten,
                exactComparisons,
                backupHashBefore,
                backupHashAfterRecovery,
                stopwatch.Elapsed.TotalMilliseconds);

            await Task.Run(
                testDatabase.DeleteDatabaseFamilyCore,
                cancellationToken).ConfigureAwait(false);
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
                testDatabase.DeleteDatabaseFamilyCore();
            }
            catch (Exception cleanupException)
            {
                return new SaveRecoveryAcceptanceReport(
                    false,
                    $"{exception.GetType().Name}: {exception.Message}; " +
                    $"cleanup failed: {cleanupException.Message}",
                    null,
                    EmptyDiagnostics(),
                    10,
                    11,
                    false,
                    false,
                    false,
                    false,
                    false,
                    false,
                    exactComparisons,
                    string.Empty,
                    string.Empty,
                    stopwatch.Elapsed.TotalMilliseconds);
            }

            return new SaveRecoveryAcceptanceReport(
                false,
                $"{exception.GetType().Name}: {exception.Message}",
                null,
                EmptyDiagnostics(),
                10,
                11,
                false,
                false,
                false,
                false,
                false,
                false,
                exactComparisons,
                string.Empty,
                string.Empty,
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private bool CreateValidatedBackupCore(
        SqliteConnection sourceConnection,
        string slotId)
    {
        DeleteFileFamilyCore(BackupCandidatePath);
        using (SqliteConnection destinationConnection = new(
            $"Data Source={BackupCandidatePath};Mode=ReadWriteCreate;" +
            "Cache=Private;Pooling=False"))
        {
            destinationConnection.Open();
            sourceConnection.BackupDatabase(destinationConnection);
        }

        return InstallValidatedBackupCandidateCore(
            slotId,
            requireSnapshot: true);
    }

    private bool InstallValidatedBackupCandidateCore(
        string? slotId,
        bool requireSnapshot)
    {
        SaveFileInspection candidate = InspectDatabaseFileCore(
            BackupCandidatePath,
            slotId,
            requireSnapshot);
        if (!candidate.IsValid)
        {
            throw new InvalidDataException(
                $"Backup candidate is invalid: {candidate.Error}");
        }

        bool atomicReplacement = File.Exists(BackupPath);
        DeleteFileFamilyCore(BackupPreviousPath);
        DeleteFileFamilyCore(BackupFailedPath);
        DeleteSidecarsCore(BackupPath);

        if (atomicReplacement)
        {
            File.Replace(
                BackupCandidatePath,
                BackupPath,
                BackupPreviousPath,
                ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(BackupCandidatePath, BackupPath);
        }

        SaveFileInspection installed = InspectDatabaseFileCore(
            BackupPath,
            slotId,
            requireSnapshot);
        if (!installed.IsValid)
        {
            if (atomicReplacement && File.Exists(BackupPreviousPath))
            {
                File.Replace(
                    BackupPreviousPath,
                    BackupPath,
                    BackupFailedPath,
                    ignoreMetadataErrors: true);
            }

            throw new InvalidDataException(
                $"Installed backup failed validation: {installed.Error}");
        }

        DeleteFileFamilyCore(BackupPreviousPath);
        DeleteFileFamilyCore(BackupFailedPath);
        return atomicReplacement;
    }

    private void RecoverPrimaryIfCorruptCore(string? slotId)
    {
        if (!File.Exists(_databasePath))
        {
            return;
        }

        SaveFileInspection primary = InspectDatabaseFileCore(
            _databasePath,
            slotId,
            requireSnapshot: false);
        if (primary.IsValid)
        {
            return;
        }

        AppendRecoveryLogCore(
            "CORRUPTION_DETECTED",
            $"primary={_databasePath}; error={primary.Error}");
        _ = RestoreBackupCore(
            slotId,
            force: false,
            reason: "automatic startup recovery");
    }

    private SaveRecoveryReport RestoreBackupCore(
        string? slotId,
        bool force,
        string reason)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        bool requireSnapshot = !string.IsNullOrWhiteSpace(slotId);
        SaveFileInspection primary = InspectDatabaseFileCore(
            _databasePath,
            slotId,
            requireSnapshot: false);
        if (primary.IsValid && !force)
        {
            stopwatch.Stop();
            return new SaveRecoveryReport(
                false,
                true,
                "primary database is valid; recovery was not needed",
                primary.Snapshot,
                primary.IntegrityResult,
                InspectDatabaseFileCore(
                    BackupPath,
                    slotId,
                    requireSnapshot: false).IntegrityResult,
                false,
                string.Empty,
                stopwatch.Elapsed.TotalMilliseconds);
        }

        SaveFileInspection backup = InspectDatabaseFileCore(
            BackupPath,
            slotId,
            requireSnapshot);
        if (!backup.IsValid)
        {
            AppendRecoveryLogCore(
                "RECOVERY_BLOCKED",
                $"reason={reason}; backupError={backup.Error}");
            throw new InvalidDataException(
                "Recovery is blocked because the backup is missing or invalid: " +
                backup.Error);
        }

        DeleteFileFamilyCore(RecoveryCandidatePath);
        File.Copy(BackupPath, RecoveryCandidatePath, overwrite: true);
        SaveFileInspection candidate = InspectDatabaseFileCore(
            RecoveryCandidatePath,
            slotId,
            requireSnapshot);
        if (!candidate.IsValid)
        {
            throw new InvalidDataException(
                $"Recovery candidate is invalid: {candidate.Error}");
        }

        AppendRecoveryLogCore(
            "RECOVERY_STARTED",
            $"reason={reason}; primaryError={primary.Error}; " +
            $"backupSha256={ComputeSha256Core(BackupPath)}");

        DeleteFileFamilyCore(RecoveryQuarantinePath);
        MoveSidecarsToQuarantineCore();
        bool atomicReplacement = File.Exists(_databasePath);
        if (atomicReplacement)
        {
            try
            {
                File.Replace(
                    RecoveryCandidatePath,
                    _databasePath,
                    RecoveryQuarantinePath,
                    ignoreMetadataErrors: true);
            }
            catch
            {
                RestoreQuarantinedSidecarsToPrimaryCore();
                throw;
            }
        }
        else
        {
            File.Move(RecoveryCandidatePath, _databasePath);
        }

        DeleteSidecarsCore(_databasePath);
        SaveFileInspection restored = InspectDatabaseFileCore(
            _databasePath,
            slotId,
            requireSnapshot);
        if (!restored.IsValid)
        {
            AppendRecoveryLogCore(
                "RECOVERY_FAILED",
                $"restoredError={restored.Error}");
            throw new InvalidDataException(
                $"Restored primary failed validation: {restored.Error}");
        }

        AppendRecoveryLogCore(
            "RECOVERY_COMPLETED",
            $"reason={reason}; atomic={(atomicReplacement ? 1 : 0)}; " +
            $"revision={restored.Snapshot?.Revision ?? 0}; " +
            $"quarantine={RecoveryQuarantinePath}");
        stopwatch.Stop();
        return new SaveRecoveryReport(
            true,
            primary.IsValid,
            "validated backup restored; previous primary preserved in quarantine",
            restored.Snapshot,
            restored.IntegrityResult,
            backup.IntegrityResult,
            atomicReplacement,
            atomicReplacement ? RecoveryQuarantinePath : string.Empty,
            stopwatch.Elapsed.TotalMilliseconds);
    }

    private SaveFileInspection InspectDatabaseFileCore(
        string path,
        string? slotId,
        bool requireSnapshot)
    {
        if (!File.Exists(path))
        {
            return new SaveFileInspection(
                false,
                false,
                0,
                0,
                "missing",
                "file does not exist",
                null);
        }

        long bytes = new FileInfo(path).Length;
        if (bytes <= 0)
        {
            return new SaveFileInspection(
                true,
                false,
                bytes,
                0,
                "invalid",
                "file is empty",
                null);
        }

        try
        {
            using SqliteConnection connection = new(
                $"Data Source={path};Mode=ReadOnly;Cache=Private;Pooling=False");
            connection.Open();
            ExecutePragma(
                connection,
                $"PRAGMA busy_timeout = {BusyTimeoutMilliseconds};");
            string integrity = ExecuteScalarString(
                connection,
                "PRAGMA integrity_check;");
            if (!string.Equals(
                    integrity,
                    "ok",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new SaveFileInspection(
                    true,
                    false,
                    bytes,
                    0,
                    integrity,
                    $"integrity_check={integrity}",
                    null);
            }

            int schemaVersion = ExecuteScalarInt(
                connection,
                "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;");
            if (schemaVersion < 1 || schemaVersion > CurrentSchemaVersion)
            {
                return new SaveFileInspection(
                    true,
                    false,
                    bytes,
                    schemaVersion,
                    integrity,
                    $"unsupported schema version {schemaVersion}",
                    null);
            }

            SaveGameSnapshot? snapshot = null;
            if (!string.IsNullOrWhiteSpace(slotId))
            {
                snapshot = LoadSnapshotCore(connection, slotId, schemaVersion);
                if (requireSnapshot && snapshot is null)
                {
                    return new SaveFileInspection(
                        true,
                        false,
                        bytes,
                        schemaVersion,
                        integrity,
                        $"slot {slotId} is missing",
                        null);
                }
            }

            return new SaveFileInspection(
                true,
                true,
                bytes,
                schemaVersion,
                integrity,
                string.Empty,
                snapshot);
        }
        catch (Exception exception) when (
            exception is SqliteException ||
            exception is InvalidDataException ||
            exception is InvalidOperationException)
        {
            return new SaveFileInspection(
                true,
                false,
                bytes,
                0,
                "invalid",
                $"{exception.GetType().Name}: {exception.Message}",
                null);
        }
    }

    private static bool TryLoadSnapshotCore(
        SqliteConnection connection,
        string slotId,
        out SaveGameSnapshot? snapshot)
    {
        snapshot = LoadSnapshotCore(connection, slotId);
        return snapshot is not null;
    }

    private static void ValidateExpectedSnapshotCore(
        SqliteConnection connection,
        SaveGameSnapshot expected)
    {
        string integrity = ExecuteScalarString(
            connection,
            "PRAGMA integrity_check;");
        if (!string.Equals(
                integrity,
                "ok",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Primary database integrity_check failed: {integrity}");
        }

        SaveGameSnapshot? actual = LoadSnapshotCore(
            connection,
            expected.SlotId);
        if (!SnapshotsEqual(expected, actual, out string mismatch))
        {
            throw new InvalidDataException(
                $"Primary snapshot validation failed: {mismatch}");
        }
    }

    private SaveRecoveryAcceptanceReport BuildRecoveryAcceptanceFailure(
        string result,
        Stopwatch stopwatch,
        int exactComparisons)
    {
        stopwatch.Stop();
        SaveDatabaseDiagnostics diagnostics;
        try
        {
            using SqliteConnection connection = OpenConnection();
            diagnostics = ReadDiagnosticsCore(connection, string.Empty);
        }
        catch (Exception exception)
        {
            diagnostics = EmptyDiagnostics();
            result += $"; diagnostics unavailable: {exception.Message}";
        }

        try
        {
            DeleteDatabaseFamilyCore();
        }
        catch (Exception cleanupException)
        {
            result += $"; cleanup failed: {cleanupException.Message}";
        }

        return new SaveRecoveryAcceptanceReport(
            false,
            result,
            null,
            diagnostics,
            10,
            11,
            false,
            false,
            false,
            false,
            false,
            false,
            exactComparisons,
            string.Empty,
            string.Empty,
            stopwatch.Elapsed.TotalMilliseconds);
    }

    private void CorruptPrimaryForAcceptanceCore()
    {
        DeleteSidecarsCore(_databasePath);
        File.WriteAllBytes(
            _databasePath,
            Encoding.UTF8.GetBytes(
                "PROJECT_HORIZON_INTENTIONAL_PRIMARY_CORRUPTION"));
    }

    private void AppendRecoveryLogCore(string eventName, string details)
    {
        string? directory = Path.GetDirectoryName(RecoveryLogPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "Recovery log directory could not be resolved.");
        }

        Directory.CreateDirectory(directory);
        string line =
            $"{DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)} " +
            $"{eventName} {details}{Environment.NewLine}";
        File.AppendAllText(RecoveryLogPath, line, Encoding.UTF8);
    }

    private void MoveSidecarsToQuarantineCore()
    {
        MoveIfExistsCore(
            _databasePath + "-wal",
            RecoveryQuarantinePath + "-wal");
        MoveIfExistsCore(
            _databasePath + "-shm",
            RecoveryQuarantinePath + "-shm");
    }

    private void RestoreQuarantinedSidecarsToPrimaryCore()
    {
        MoveIfExistsCore(
            RecoveryQuarantinePath + "-wal",
            _databasePath + "-wal");
        MoveIfExistsCore(
            RecoveryQuarantinePath + "-shm",
            _databasePath + "-shm");
    }

    private static void MoveIfExistsCore(string source, string destination)
    {
        if (!File.Exists(source))
        {
            return;
        }

        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        File.Move(source, destination);
    }

    private void DeleteDatabaseFamilyCore()
    {
        DeleteFileFamilyCore(_databasePath);
        DeleteFileFamilyCore(BackupPath);
        DeleteFileFamilyCore(BackupCandidatePath);
        DeleteFileFamilyCore(BackupPreviousPath);
        DeleteFileFamilyCore(BackupFailedPath);
        DeleteFileFamilyCore(RecoveryCandidatePath);
        DeleteFileFamilyCore(RecoveryQuarantinePath);
        DeleteMigrationArtifactsCore();
        DeleteIfExistsCore(MigrationLogPath);
    }

    private void DeleteFileFamilyCore(string path)
    {
        DeleteIfExistsCore(path);
        DeleteSidecarsCore(path);
    }

    private static void DeleteSidecarsCore(string path)
    {
        DeleteIfExistsCore(path + "-wal");
        DeleteIfExistsCore(path + "-shm");
    }

    private static void DeleteIfExistsCore(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string BuildSiblingPath(string suffix)
    {
        string directory = Path.GetDirectoryName(_databasePath) ??
            throw new InvalidOperationException(
                "Database parent directory could not be resolved.");
        string stem = Path.GetFileNameWithoutExtension(_databasePath);
        return Path.Combine(directory, stem + suffix);
    }

    private static string ComputeSha256Core(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed record SaveFileInspection(
        bool Exists,
        bool IsValid,
        long Bytes,
        int SchemaVersion,
        string IntegrityResult,
        string Error,
        SaveGameSnapshot? Snapshot);
}
