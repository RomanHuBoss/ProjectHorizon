using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;
using Microsoft.Data.Sqlite;

public partial class DeveloperWorkbenchController : Control
{
    private VBoxContainer? _content;
    private Label? _status;
    private LineEdit? _seedInput;
    private SpinBox? _sectorX;
    private SpinBox? _sectorY;
    private SpinBox? _sectorZ;
    private SpinBox? _planetIndex;
    private SpinBox? _previewLod;
    private CheckButton? _showGrid;
    private CheckButton? _showBiomes;
    private CheckButton? _showHeight;
    private CheckButton? _showResources;
    private RichTextLabel? _report;
    private LineEdit? _savePathInput;
    private GalaxySystemDefinition? _selectedSystem;
    private long _selectedSeed = GalaxyNavigationRuntime.DefaultUniverseSeed;
    private SaveGameSnapshot? _inspectedSnapshot;
    private SaveDatabaseDiagnostics? _inspectedDiagnostics;
    private string _inspectedDatabasePath = GameProfilePaths.PrimaryDatabasePath;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        if (!DeveloperToolContext.IsDeveloperModeAllowed())
        {
            GetTree().ChangeSceneToFile("res://Scenes/UI/MainMenu.tscn");
            return;
        }
        Input.MouseMode = Input.MouseModeEnum.Visible;
        StructuredGameLogger.EnsureInitialized(GetTree());
        StructuredGameLogger.UpdateContext("DeveloperWorkbench", 0, "developer.tools");
        StructuredGameLogger.Log(GameLogLevel.Information, GameLogCategory.BOOT, "developer workbench opened");
        BuildUi();
        ShowSeedExplorer();
    }

    public override void _ExitTree()
    {
        StructuredGameLogger.FlushPending();
    }

    private void BuildUi()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ColorRect background = new() { Color = new Color(0.008f, 0.012f, 0.020f, 1.0f), MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(background);
        MarginContainer margin = new();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 28);
        margin.AddThemeConstantOverride("margin_right", 28);
        margin.AddThemeConstantOverride("margin_top", 22);
        margin.AddThemeConstantOverride("margin_bottom", 22);
        AddChild(margin);
        VBoxContainer root = new();
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);
        Label title = new() { Text = "PROJECT HORIZON — DEVELOPER & DIAGNOSTICS SUITE" };
        title.AddThemeFontSizeOverride("font_size", 28);
        root.AddChild(title);
        root.AddChild(new Label
        {
            Text = "§34 Seed Explorer / Planet Preview / Chunk Profiler / Save Inspector / Debug Console   •   §35 structured logging",
            Modulate = new Color(0.58f, 0.80f, 0.90f, 1.0f)
        });
        root.AddChild(new HSeparator());
        HBoxContainer body = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 18);
        root.AddChild(body);
        VBoxContainer nav = new() { CustomMinimumSize = new Vector2(245, 0) };
        nav.AddThemeConstantOverride("separation", 8);
        body.AddChild(nav);
        AddToolButton(nav, "1  Seed Explorer", ShowSeedExplorer);
        AddToolButton(nav, "2  Planet Preview", ShowPlanetPreview);
        AddToolButton(nav, "3  Chunk Profiler", ShowChunkProfiler);
        AddToolButton(nav, "4  Save Inspector", ShowSaveInspector);
        AddToolButton(nav, "5  Debug Console", ShowDebugConsole);
        nav.AddChild(new HSeparator());
        Button mainMenu = ToolButton("← Main Menu");
        mainMenu.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/UI/MainMenu.tscn");
        nav.AddChild(mainMenu);
        ScrollContainer scroll = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddChild(scroll);
        _content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        _content.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(_content);
        _status = new Label { Text = "READY", Modulate = new Color(0.64f, 0.88f, 0.76f, 1.0f) };
        root.AddChild(_status);
    }

    private void AddToolButton(VBoxContainer parent, string text, Action action)
    {
        Button button = ToolButton(text);
        button.Pressed += action;
        parent.AddChild(button);
    }

    private static Button ToolButton(string text) => new()
    {
        Text = text,
        CustomMinimumSize = new Vector2(225, 42),
        FocusMode = FocusModeEnum.All
    };

    private void ClearContent(string heading, string description)
    {
        if (_content is null) return;
        foreach (Node child in _content.GetChildren()) child.QueueFree();
        Label title = new() { Text = heading };
        title.AddThemeFontSizeOverride("font_size", 24);
        _content.AddChild(title);
        _content.AddChild(new Label { Text = description, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        _content.AddChild(new HSeparator());
    }

    private void ShowSeedExplorer()
    {
        ClearContent("Seed Explorer", "Change universe seed and sector coordinates, inspect the deterministic system and planet list, copy stable IDs and export a report.");
        HBoxContainer row = new();
        _content!.AddChild(row);
        _seedInput = new LineEdit { Text = _selectedSeed.ToString(CultureInfo.InvariantCulture), PlaceholderText = "Universe seed", CustomMinimumSize = new Vector2(220, 40) };
        row.AddChild(_seedInput);
        _sectorX = CoordinateSpin(); _sectorY = CoordinateSpin(); _sectorZ = CoordinateSpin();
        row.AddChild(AxisBox("X", _sectorX)); row.AddChild(AxisBox("Y", _sectorY)); row.AddChild(AxisBox("Z", _sectorZ));
        Button generate = ToolButton("Generate / Inspect"); generate.Pressed += GenerateSeedReport; row.AddChild(generate);
        HBoxContainer actions = new(); _content.AddChild(actions);
        Button copy = ToolButton("Copy System ID"); copy.Pressed += CopySelectedSystemId; actions.AddChild(copy);
        Button export = ToolButton("Export Report"); export.Pressed += ExportSeedReport; actions.AddChild(export);
        _report = ReportBox(); _content.AddChild(_report);
        GenerateSeedReport();
    }

    private void GenerateSeedReport()
    {
        try
        {
            if (_seedInput is null || !long.TryParse(_seedInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long seed) || seed <= 0)
            { SetStatus("Seed must be a positive Int64.", true); return; }
            _selectedSeed = seed;
            GalaxyNavigationRuntime runtime = new(seed);
            int x = (int)(_sectorX?.Value ?? 0), y = (int)(_sectorY?.Value ?? 0), z = (int)(_sectorZ?.Value ?? 0);
            GalaxySystemDefinition selectedSystem = runtime.GenerateSystem(x, y, z);
            _selectedSystem = selectedSystem;
            StringBuilder text = new();
            text.AppendLine($"System: {selectedSystem.DisplayName}");
            text.AppendLine($"ID: {selectedSystem.SystemId}");
            text.AppendLine($"Universe seed: {_selectedSeed}");
            text.AppendLine($"Sector: ({x}, {y}, {z})");
            text.AppendLine($"Position ly: ({selectedSystem.PositionX:F2}, {selectedSystem.PositionY:F2}, {selectedSystem.PositionZ:F2})");
            text.AppendLine($"Star: {selectedSystem.StarType}   Economy: {selectedSystem.EconomyType}   Danger: {selectedSystem.DangerLevel}");
            text.AppendLine($"Planets: {selectedSystem.Planets.Count}\n");
            foreach (GalaxyPlanetDefinition planet in selectedSystem.Planets)
                text.AppendLine($"[{planet.OrbitIndex}] {planet.PlanetId}  archetype={planet.Archetype}  moons={planet.MoonCount}  atmosphere={(planet.HasAtmosphere ? 1 : 0)}  water={(planet.HasWater ? 1 : 0)}  seed={planet.Seed}");
            _report!.Text = text.ToString();
            StructuredGameLogger.UpdateContext("SeedExplorer", _selectedSeed, selectedSystem.SystemId);
            StructuredGameLogger.Log(GameLogLevel.Information, GameLogCategory.WORLDGEN, "seed explorer generated system");
            SetStatus("Seed report generated.");
        }
        catch (Exception exception)
        {
            StructuredGameLogger.Log(GameLogLevel.Error, GameLogCategory.ERROR, "seed explorer failed", exception);
            SetStatus($"Seed Explorer failed: {exception.Message}", true);
        }
    }

    private void CopySelectedSystemId()
    {
        if (_selectedSystem is null) return;
        DisplayServer.ClipboardSet(_selectedSystem.SystemId);
        SetStatus("System ID copied to clipboard.");
    }

    private void ExportSeedReport()
    {
        if (_selectedSystem is null) return;
        string path = BuildReportPath("seed-explorer", "json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { universeSeed = _selectedSeed, system = _selectedSystem }, new JsonSerializerOptions { WriteIndented = true }));
        SetStatus($"Exported: {path}");
    }

    private void ShowPlanetPreview()
    {
        ClearContent("Planet Preview", "Generate a deterministic planet sample, inspect generation time/height/resource density, set overlays and launch the interactive cube-sphere preview.");
        EnsureSelectedSystem();
        HBoxContainer row = new(); _content!.AddChild(row);
        _planetIndex = new SpinBox { MinValue = 0, MaxValue = Math.Max(0, (_selectedSystem?.Planets.Count ?? 1) - 1), Step = 1 };
        _previewLod = new SpinBox { MinValue = 0, MaxValue = 4, Step = 1, Value = DeveloperToolContext.PreviewLod };
        row.AddChild(AxisBox("Planet", _planetIndex)); row.AddChild(AxisBox("LOD", _previewLod));
        Button measure = ToolButton("Generate Profile"); measure.Pressed += MeasurePlanetPreview; row.AddChild(measure);
        Button launch = ToolButton("Launch Interactive Preview"); launch.Pressed += LaunchPlanetPreview; row.AddChild(launch);
        HBoxContainer toggles = new(); _content.AddChild(toggles);
        _showGrid = Toggle("Chunk grid", DeveloperToolContext.PreviewChunkGrid);
        _showBiomes = Toggle("Biomes", DeveloperToolContext.PreviewBiomes);
        _showHeight = Toggle("Height", DeveloperToolContext.PreviewHeight);
        _showResources = Toggle("Resource density", DeveloperToolContext.PreviewResourceDensity);
        toggles.AddChild(_showGrid); toggles.AddChild(_showBiomes); toggles.AddChild(_showHeight); toggles.AddChild(_showResources);
        _report = ReportBox(); _content.AddChild(_report);
        MeasurePlanetPreview();
    }

    private void MeasurePlanetPreview()
    {
        EnsureSelectedSystem();
        if (_selectedSystem is null || _selectedSystem.Planets.Count == 0) return;
        int index = Math.Clamp((int)(_planetIndex?.Value ?? 0), 0, _selectedSystem.Planets.Count - 1);
        int lod = Math.Clamp((int)(_previewLod?.Value ?? 1), 0, 4);
        GalaxyPlanetDefinition planet = _selectedSystem.Planets[index];
        int[] resolutions = { 17, 33, 65, 97, 129 };
        Stopwatch stopwatch = Stopwatch.StartNew();
        CubeSphereBuildData build = CubeSphereMeshBuilder.Build(resolutions[lod], 96.0f, PlanetHeightAmplitude(planet.Archetype), 0.0125f, unchecked((int)planet.Seed));
        stopwatch.Stop();
        double minHeight = double.MaxValue, maxHeight = double.MinValue;
        foreach (CubeSphereFaceData face in build.Faces)
        foreach (Vector3 vertex in face.Vertices)
        { double radius = vertex.Length(); minHeight = Math.Min(minHeight, radius - 96.0); maxHeight = Math.Max(maxHeight, radius - 96.0); }
        double density = DeterministicResourceDensity(planet.Seed);
        PlanetEnvironmentCatalog environmentCatalog =
            PlanetEnvironmentCatalog.LoadFromJson(
                Godot.FileAccess.GetFileAsString(
                    "res://Content/planet_environments.json"));
        PlanetEnvironmentProfile environment =
            new PlanetEnvironmentRuntime(environmentCatalog).BuildProfile(
                planet,
                _selectedSystem?.StarType ?? GalaxyStarType.YellowStar);
        _report!.Text =
            $"Planet: {planet.PlanetId}\nArchetype: {planet.Archetype}  seed={planet.Seed}\nLOD: {lod}  resolution={build.Resolution}\n" +
            $"Environment: radius={environment.RadiusKm:F1} km  gravity={environment.SurfaceGravityG:F2} g  " +
            $"meanT={environment.MeanTemperatureC:F0} C  atmosphere={environment.AtmosphereDensity:F2}\n" +
            $"Water={environment.WaterCoverage:P0}  clouds={environment.CloudLayerCount}  " +
            $"biomes={environment.ActiveBiomeIds.Count}  landable={(environment.Landable ? 1 : 0)}\n" +
            $"Vertices: {build.TotalVertices:N0}  triangles={build.TotalTriangles:N0}\nGeneration CPU: {stopwatch.Elapsed.TotalMilliseconds:F2} ms\n" +
            $"Height range: {minHeight:F2} .. {maxHeight:F2} m\nResource-density proxy: {density:P1}\n" +
            $"Overlays: grid={Flag(_showGrid)} biomes={Flag(_showBiomes)} height={Flag(_showHeight)} resources={Flag(_showResources)}\n" +
            $"Seams: {build.SeamComparisons}/{build.ExpectedSeamComparisons}; maxPositionError={build.MaximumSeamPositionError:E3}";
        SetPreviewContext(planet, lod);
        StructuredGameLogger.UpdateContext("PlanetPreview", _selectedSeed, planet.PlanetId);
        StructuredGameLogger.Log(GameLogLevel.Information, GameLogCategory.PERFORMANCE, "planet preview generation profile",
            fields: new Dictionary<string, object?> { ["cpuMilliseconds"] = stopwatch.Elapsed.TotalMilliseconds, ["vertices"] = build.TotalVertices, ["triangles"] = build.TotalTriangles, ["lod"] = lod });
        SetStatus("Planet preview profile generated.");
    }

    private void LaunchPlanetPreview()
    {
        EnsureSelectedSystem();
        if (_selectedSystem is null || _selectedSystem.Planets.Count == 0) return;
        int index = Math.Clamp((int)(_planetIndex?.Value ?? 0), 0, _selectedSystem.Planets.Count - 1);
        SetPreviewContext(_selectedSystem.Planets[index], Math.Clamp((int)(_previewLod?.Value ?? 1), 0, 4));
        DeveloperToolContext.ReturnToWorkbenchOnF6 = true;
        GetTree().ChangeSceneToFile(DeveloperToolContext.PlanetPreviewScene);
    }

    private void ShowChunkProfiler()
    {
        ClearContent("Chunk Profiler", "Live terrain streaming profiler: loaded chunks, queue, worker CPU, main-thread upload/apply, memory, vertices, collisions and cancelled/stale jobs.");
        _content!.AddChild(new Label { Text = "Launch profiler. WASD moves, F10 stress, P soak, F6 returns to workbench.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        Button launch = ToolButton("Launch Live Chunk Profiler");
        launch.Pressed += () => { DeveloperToolContext.ReturnToWorkbenchOnF6 = true; GetTree().ChangeSceneToFile(DeveloperToolContext.ChunkProfilerScene); };
        _content.AddChild(launch);
        Button export = ToolButton("Export Profiler Contract");
        export.Pressed += () =>
        {
            string path = BuildReportPath("chunk-profiler-contract", "json");
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                required = new[] { "loadedChunks", "queueState", "workerCpuMs", "mainThreadApplyMs", "gpuUploadSubmissionMs", "managedMemory", "vertices", "collisions", "cancelledJobs" },
                scene = DeveloperToolContext.ChunkProfilerScene, stress = "F10", soak = "P"
            }, new JsonSerializerOptions { WriteIndented = true }));
            SetStatus($"Exported: {path}");
        };
        _content.AddChild(export);
    }

    private void ShowSaveInspector()
    {
        ClearContent("Save Inspector", "Open a SQLite save via SaveDatabase, inspect schema/integrity/player/ship/visited systems, export every user table read-only, or migrate only a validated copy.");
        HBoxContainer pathRow = new(); _content!.AddChild(pathRow);
        _savePathInput = new LineEdit
        {
            Text = GameProfilePaths.PrimaryDatabasePath,
            PlaceholderText = "SQLite save path (absolute or user://...)",
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        pathRow.AddChild(_savePathInput);
        Button primary = ToolButton("Use Primary");
        primary.Pressed += () => { if (_savePathInput is not null) _savePathInput.Text = GameProfilePaths.PrimaryDatabasePath; InspectSaveAsync(); };
        pathRow.AddChild(primary);
        HBoxContainer actions = new(); _content.AddChild(actions);
        Button inspect = ToolButton("Inspect Save"); inspect.Pressed += InspectSaveAsync; actions.AddChild(inspect);
        Button export = ToolButton("Export All Tables"); export.Pressed += ExportSaveTables; actions.AddChild(export);
        Button migrate = ToolButton("Migrate Validated Copy"); migrate.Pressed += MigrateSaveCopyAsync; actions.AddChild(migrate);
        _report = ReportBox(); _content.AddChild(_report);
        InspectSaveAsync();
    }

    private async void InspectSaveAsync()
    {
        try
        {
            string databasePath = ResolveSaveInspectorPath();
            if (!File.Exists(databasePath))
            {
                _inspectedSnapshot = null; _inspectedDiagnostics = null;
                SetStatus($"Save file not found: {databasePath}", true);
                return;
            }
            string inspectionCopyPath = CreateReadOnlySnapshotCopy(databasePath, "inspector-working");
            using SaveDatabase database = new(inspectionCopyPath);
            await database.InitializeAsync();
            SaveGameSnapshot? snapshot = await database.LoadAsync(GameProfilePaths.PrimarySlotId);
            SaveDatabaseDiagnostics diagnostics = await database.ReadDiagnosticsAsync(GameProfilePaths.PrimarySlotId);
            _inspectedSnapshot = snapshot;
            _inspectedDiagnostics = diagnostics;
            _inspectedDatabasePath = databasePath;
            StringBuilder text = new();
            text.AppendLine($"Source database: {databasePath}");
            text.AppendLine($"Inspection copy: {inspectionCopyPath}");
            text.AppendLine($"Schema: {diagnostics.SchemaVersion}/{SaveDatabase.CurrentSchemaVersion}");
            text.AppendLine($"Integrity: {diagnostics.IntegrityResult}");
            text.AppendLine($"Journal: {diagnostics.JournalMode}; foreignKeys={(diagnostics.ForeignKeysEnabled ? 1 : 0)}; busy={diagnostics.BusyTimeoutMilliseconds} ms");
            text.AppendLine($"Bytes: {diagnostics.DatabaseBytes:N0}; inventoryRows={diagnostics.InventoryRows}; visitedPlanetRows={diagnostics.VisitedPlanetRows}");
            if (snapshot is null) text.AppendLine("Primary slot is empty.");
            else
            {
                text.AppendLine($"Slot: {snapshot.SlotId}; revision={snapshot.Revision}; updatedUTC={snapshot.UpdatedUtc}");
                text.AppendLine($"Player: {snapshot.Player.PlayerId} @ ({snapshot.Player.PositionX:F2}, {snapshot.Player.PositionY:F2}, {snapshot.Player.PositionZ:F2}) planet={snapshot.Player.CurrentPlanetId}");
                text.AppendLine($"Ship: {snapshot.Ship.ShipId} / {snapshot.Ship.DisplayName}; health={snapshot.Ship.Health:F1}; fuel={snapshot.Ship.Fuel:F1}");
                text.AppendLine($"Visited systems: {snapshot.GalaxyNavigation?.VisitedSystemIds.Count ?? 0}; current={snapshot.GalaxyNavigation?.CurrentSystemId ?? "legacy"}");
                text.AppendLine($"Inventory rows: {snapshot.Inventory.Count}");
            }
            _report!.Text = text.ToString();
            StructuredGameLogger.Log(GameLogLevel.Information, GameLogCategory.DATABASE, "save inspector read isolated snapshot copy");
            SetStatus("Save inspection complete.");
        }
        catch (Exception exception)
        { StructuredGameLogger.Log(GameLogLevel.Error, GameLogCategory.ERROR, "save inspector failed", exception); SetStatus($"Save Inspector failed: {exception.Message}", true); }
    }

    private void ExportSaveTables()
    {
        SaveDatabaseDiagnostics? diagnostics = _inspectedDiagnostics;
        if (diagnostics is null || !File.Exists(_inspectedDatabasePath))
        {
            SetStatus("Inspect a valid save first.", true);
            return;
        }
        try
        {
            string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string directory = Path.Combine(ReportDirectory(), $"save-tables-{stamp}");
            Directory.CreateDirectory(directory);
            SqliteConnectionStringBuilder builder = new()
            {
                DataSource = _inspectedDatabasePath,
                Mode = SqliteOpenMode.ReadOnly
            };
            using SqliteConnection connection = new(builder.ToString());
            connection.Open();
            List<string> tables = new();
            using (SqliteCommand tableCommand = connection.CreateCommand())
            {
                tableCommand.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
                using SqliteDataReader tableReader = tableCommand.ExecuteReader();
                while (tableReader.Read()) tables.Add(tableReader.GetString(0));
            }
            foreach (string table in tables)
            {
                string quoted = table.Replace("\"", "\"\"", StringComparison.Ordinal);
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = $"SELECT * FROM \"{quoted}\";";
                using SqliteDataReader reader = command.ExecuteReader();
                StringBuilder csv = new();
                for (int column = 0; column < reader.FieldCount; column++)
                {
                    if (column > 0) csv.Append(',');
                    csv.Append(Csv(reader.GetName(column)));
                }
                csv.AppendLine();
                while (reader.Read())
                {
                    for (int column = 0; column < reader.FieldCount; column++)
                    {
                        if (column > 0) csv.Append(',');
                        string value = reader.IsDBNull(column)
                            ? string.Empty
                            : Convert.ToString(reader.GetValue(column), CultureInfo.InvariantCulture) ?? string.Empty;
                        csv.Append(Csv(value));
                    }
                    csv.AppendLine();
                }
                File.WriteAllText(Path.Combine(directory, $"{SafeFileName(table)}.csv"), csv.ToString());
            }
            File.WriteAllText(Path.Combine(directory, "_inspection-meta.csv"),
                "schema_version,integrity,database_bytes,inventory_rows,visited_planet_rows\n" +
                $"{diagnostics.SchemaVersion},{Csv(diagnostics.IntegrityResult)},{diagnostics.DatabaseBytes},{diagnostics.InventoryRows},{diagnostics.VisitedPlanetRows}\n");
            SetStatus($"Exported {tables.Count} SQLite tables read-only: {directory}");
            StructuredGameLogger.Log(GameLogLevel.Information, GameLogCategory.DATABASE, "save inspector exported all tables",
                fields: new Dictionary<string, object?> { ["tableCount"] = tables.Count });
        }
        catch (Exception exception)
        {
            StructuredGameLogger.Log(GameLogLevel.Error, GameLogCategory.ERROR, "save table export failed", exception);
            SetStatus($"Export failed: {exception.Message}", true);
        }
    }

    private async void MigrateSaveCopyAsync()
    {
        try
        {
            string sourcePath = _inspectedDatabasePath;
            if (!File.Exists(sourcePath)) { SetStatus("Inspect a valid save first.", true); return; }
            string baselinePath = CreateReadOnlySnapshotCopy(sourcePath, "migration-baseline");
            SaveGameSnapshot? baselineSnapshot;
            using (SaveDatabase baseline = new(baselinePath))
            {
                await baseline.InitializeAsync();
                baselineSnapshot = await baseline.LoadAsync(GameProfilePaths.PrimarySlotId);
            }
            if (baselineSnapshot is null) { SetStatus("Save slot is empty; no copy to migrate.", true); return; }
            string copyPath = Path.Combine(ReportDirectory(), "save_1.migration-copy.db");
            CreateReadOnlySnapshotCopy(sourcePath, "save_1.migration-copy", copyPath);
            using SaveDatabase copy = new(copyPath);
            SaveDatabaseDiagnostics migrated = await copy.InitializeAsync();
            SaveGameSnapshot? copiedSnapshot = await copy.LoadAsync(GameProfilePaths.PrimarySlotId);
            SaveDatabaseDiagnostics copyDiagnostics = await copy.ReadDiagnosticsAsync(GameProfilePaths.PrimarySlotId);
            bool passed = copiedSnapshot is not null && copiedSnapshot.Revision == baselineSnapshot.Revision &&
                string.Equals(copyDiagnostics.IntegrityResult, "ok", StringComparison.OrdinalIgnoreCase) &&
                migrated.SchemaVersion == SaveDatabase.CurrentSchemaVersion;
            SetStatus($"Migration copy {(passed ? "PASS" : "FAIL")}: {copyPath}", !passed);
            StructuredGameLogger.Log(passed ? GameLogLevel.Information : GameLogLevel.Error,
                passed ? GameLogCategory.SAVE : GameLogCategory.ERROR, "save inspector migrated validated copy");
        }
        catch (Exception exception)
        { StructuredGameLogger.Log(GameLogLevel.Error, GameLogCategory.ERROR, "save copy migration failed", exception); SetStatus($"Migration-copy failed: {exception.Message}", true); }
    }

    private void ShowDebugConsole()
    {
        ClearContent("Debug Console", "Launch the vertical slice with developer console opened. Public builds keep this disabled unless --developer is explicitly supplied.");
        RichTextLabel commands = ReportBox();
        commands.Text = "Commands\nteleport x y z\nspawn <definitionId> [count]\ngive <itemId> [qty]\ndamage [amount]\nheal [amount]\n" +
            "set_time <0..24>\nset_weather <clear|wind|storm|toxic>\nload_system <x> <y> <z>\nload_planet <index>\n" +
            "show_chunks\nshow_navmesh\nshow_ai\nprofile_worldgen\nsave\nreload_content";
        _content!.AddChild(commands);
        Button launch = ToolButton("Launch Game + Console");
        launch.Pressed += () => { DeveloperToolContext.OpenConsoleOnGameplay = true; GetTree().ChangeSceneToFile(DeveloperToolContext.GameplayScene); };
        _content.AddChild(launch);
    }

    private void EnsureSelectedSystem()
    {
        if (_selectedSystem is not null) return;
        GalaxyNavigationRuntime runtime = new(_selectedSeed);
        _selectedSystem = runtime.GenerateSystem(0, 0, 0);
    }

    private void SetPreviewContext(GalaxyPlanetDefinition planet, int lod)
    {
        DeveloperToolContext.PreviewUniverseSeed = _selectedSeed;
        DeveloperToolContext.PreviewPlanetSeed = planet.Seed;
        DeveloperToolContext.PreviewPlanetId = planet.PlanetId;
        DeveloperToolContext.PreviewPlanetArchetype = planet.Archetype;
        DeveloperToolContext.PreviewStarType = _selectedSystem?.StarType ??
            GalaxyStarType.YellowStar;
        DeveloperToolContext.PreviewHasAtmosphere = planet.HasAtmosphere;
        DeveloperToolContext.PreviewHasWater = planet.HasWater;
        DeveloperToolContext.PreviewLod = lod;
        DeveloperToolContext.PreviewChunkGrid = _showGrid?.ButtonPressed ?? true;
        DeveloperToolContext.PreviewBiomes = _showBiomes?.ButtonPressed ?? true;
        DeveloperToolContext.PreviewHeight = _showHeight?.ButtonPressed ?? true;
        DeveloperToolContext.PreviewResourceDensity = _showResources?.ButtonPressed ?? true;
    }

    private static string CreateReadOnlySnapshotCopy(string sourcePath, string prefix, string? explicitPath = null)
    {
        string destinationPath = explicitPath ?? Path.Combine(
            ReportDirectory(),
            $"{prefix}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}.db");
        foreach (string candidate in new[] { destinationPath, destinationPath + "-wal", destinationPath + "-shm" })
            if (File.Exists(candidate)) File.Delete(candidate);
        SqliteConnectionStringBuilder sourceBuilder = new()
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly
        };
        SqliteConnectionStringBuilder destinationBuilder = new()
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        };
        using SqliteConnection source = new(sourceBuilder.ToString());
        using SqliteConnection destination = new(destinationBuilder.ToString());
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
        return destinationPath;
    }

    private string ResolveSaveInspectorPath()
    {
        string raw = _savePathInput?.Text.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return GameProfilePaths.PrimaryDatabasePath;
        if (raw.StartsWith("user://", StringComparison.OrdinalIgnoreCase)) return ProjectSettings.GlobalizePath(raw);
        return Path.GetFullPath(raw);
    }

    private static string SafeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        StringBuilder safe = new(value.Length);
        foreach (char c in value) safe.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return safe.Length == 0 ? "table" : safe.ToString();
    }

    private static SpinBox CoordinateSpin() => new() { MinValue = -4096, MaxValue = 4096, Step = 1, Value = 0, CustomMinimumSize = new Vector2(110, 40) };
    private static VBoxContainer AxisBox(string title, Control control) { VBoxContainer box = new(); box.AddChild(new Label { Text = title }); box.AddChild(control); return box; }
    private static CheckButton Toggle(string title, bool active) => new() { Text = title, ButtonPressed = active };
    private static RichTextLabel ReportBox() => new() { FitContent = false, CustomMinimumSize = new Vector2(0, 360), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, SelectionEnabled = true };
    private static float PlanetHeightAmplitude(string archetype) => archetype switch { "volcanic" => 10.0f, "barren" => 8.0f, "frozen" => 5.0f, "oceanic" => 3.5f, "gas_giant" => 2.0f, _ => 6.0f };
    private static double DeterministicResourceDensity(long seed) { ulong value = unchecked((ulong)seed); value ^= value >> 33; value *= 0xff51afd7ed558ccdUL; value ^= value >> 33; return 0.12 + ((value & 0xFFFFUL) / 65535.0) * 0.68; }
    private static int Flag(CheckButton? button) => button?.ButtonPressed == true ? 1 : 0;
    private static string Csv(string value) { string escaped = (value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal); return $"\"{escaped}\""; }
    private static string ReportDirectory() { string directory = ProjectSettings.GlobalizePath("user://developer_reports"); Directory.CreateDirectory(directory); return directory; }
    private static string BuildReportPath(string prefix, string extension) { string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture); return Path.Combine(ReportDirectory(), $"{prefix}-{stamp}.{extension}"); }
    private void SetStatus(string text, bool error = false) { if (_status is null) return; _status.Text = text; _status.Modulate = error ? new Color(1.0f, 0.48f, 0.42f, 1.0f) : new Color(0.64f, 0.88f, 0.76f, 1.0f); }
}
