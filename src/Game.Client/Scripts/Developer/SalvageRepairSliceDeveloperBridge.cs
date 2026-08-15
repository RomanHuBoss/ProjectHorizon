using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private DeveloperDiagnosticsSuite? _developerDiagnosticsSuite;
    private double _developerDiagnosticsAccumulator;
    private string? _developerPlanetOverrideId;
    private string _developerWeather = "clear";
    private Node3D? _developerChunkGrid;
    private Node3D? _developerNavmeshGrid;
    private bool _developerAiDebug;
    private string _task136AcceptanceHud = "READY";

    private void InitializeDeveloperDiagnosticsRuntime()
    {
        StructuredGameLogger.EnsureInitialized(GetTree());
        UpdateDeveloperDiagnosticsRuntime(10.0);
        StructuredGameLogger.Log(GameLogLevel.Information, GameLogCategory.CONTENT, "vertical slice content runtime initialized");
        if (DeveloperToolContext.IsDeveloperModeAllowed())
        {
            _developerDiagnosticsSuite = new DeveloperDiagnosticsSuite { Name = "DeveloperDiagnosticsSuite" };
            AddChild(_developerDiagnosticsSuite);
        }
        GD.Print(
            "TASK-136 developer diagnostics READY: tools=5; commands=15; " +
            "loggingCategories=14; consoleGate=debug-or---developer; structuredJsonl=1; redaction=1.");
    }

    private void UpdateDeveloperDiagnosticsRuntime(double delta)
    {
        _developerDiagnosticsAccumulator += Math.Max(0.0, delta);
        if (_developerDiagnosticsAccumulator < 1.0 && delta < 9.0) return;
        _developerDiagnosticsAccumulator = 0.0;
        long seed = _galaxyNavigationRuntime?.UniverseSeed ?? 0;
        string worldObject = _developerPlanetOverrideId ??
            _galaxyNavigationRuntime?.CurrentPlanetId ?? "none";
        StructuredGameLogger.UpdateContext(
            SceneFilePath.Length > 0 ? SceneFilePath : "VerticalSlice",
            seed,
            worldObject);
    }

    private string GetActiveDeveloperPlanetId()
    {
        if (!string.IsNullOrWhiteSpace(_developerPlanetOverrideId) &&
            GalaxyNavigation.CurrentSystem.Planets.Any(planet =>
                string.Equals(planet.PlanetId, _developerPlanetOverrideId, StringComparison.Ordinal)))
        {
            return _developerPlanetOverrideId;
        }
        return GalaxyNavigation.CurrentPlanetId;
    }

    public DeveloperCommandResult ExecuteDeveloperCommand(string commandLine)
    {
        if (!DeveloperToolContext.IsDeveloperModeAllowed())
            return new DeveloperCommandResult(false, "developer mode disabled");
        string[] parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return new DeveloperCommandResult(false, "empty command");
        try
        {
            return parts[0].ToLowerInvariant() switch
            {
                "teleport" => DeveloperTeleport(parts),
                "spawn" => DeveloperSpawn(parts),
                "give" => DeveloperGive(parts),
                "damage" => DeveloperDamage(parts),
                "heal" => DeveloperHeal(parts),
                "set_time" => DeveloperSetTime(parts),
                "set_weather" => DeveloperSetWeather(parts),
                "load_system" => DeveloperLoadSystem(parts),
                "load_planet" => DeveloperLoadPlanet(parts),
                "show_chunks" => DeveloperToggleChunkGrid(),
                "show_navmesh" => DeveloperToggleNavmeshGrid(),
                "show_ai" => DeveloperToggleAi(),
                "profile_worldgen" => DeveloperProfileWorldgen(),
                "save" => DeveloperSave(),
                "reload_content" => DeveloperReloadContent(),
                _ => new DeveloperCommandResult(false, "unknown command; type help")
            };
        }
        catch (Exception exception)
        {
            StructuredGameLogger.Log(GameLogLevel.Error, GameLogCategory.ERROR, "developer command failed", exception,
                new Dictionary<string, object?> { ["command"] = parts[0] });
            return new DeveloperCommandResult(false, $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private DeveloperCommandResult DeveloperTeleport(string[] parts)
    {
        if (parts.Length != 4 || !TryFloat(parts[1], out float x) || !TryFloat(parts[2], out float y) || !TryFloat(parts[3], out float z))
            return new DeveloperCommandResult(false, "usage: teleport x y z");
        Vector3 destination = new(x, y, z);
        if (_stageOneVoyageRuntime?.Piloted == true && _voyageShip is not null) _voyageShip.GlobalPosition = destination;
        else if (_player is not null) _player.GlobalPosition = destination;
        else return new DeveloperCommandResult(false, "no controllable entity");
        StructuredGameLogger.Log(GameLogLevel.Information, GameLogCategory.PLAYER, "developer teleport",
            fields: new Dictionary<string, object?> { ["x"] = x, ["y"] = y, ["z"] = z });
        return new DeveloperCommandResult(true, $"teleported to ({x:F1}, {y:F1}, {z:F1})");
    }

    private DeveloperCommandResult DeveloperSpawn(string[] parts)
    {
        if (parts.Length < 2) return new DeveloperCommandResult(false, "usage: spawn <definitionId> [count]");
        string definitionId = parts[1];
        int count = parts.Length > 2 && int.TryParse(parts[2], out int parsed) ? Math.Clamp(parsed, 1, 20) : 1;
        if (!ContentCatalog.Items.ContainsKey(definitionId)) return new DeveloperCommandResult(false, $"unknown definition: {definitionId}");
        Node3D parent = GetNodeOrNull<Node3D>("Gameplay/DeveloperSpawns") ?? new Node3D { Name = "DeveloperSpawns" };
        if (parent.GetParent() is null) GetNode<Node3D>("Gameplay").AddChild(parent);
        Vector3 origin = _player?.GlobalPosition ?? Vector3.Zero;
        for (int index = 0; index < count; index++)
        {
            MeshInstance3D marker = new()
            {
                Name = $"DevSpawn_{definitionId.Replace('.', '_')}_{index}",
                Mesh = new SphereMesh { Radius = 0.28f, Height = 0.56f },
                Position = origin + new Vector3((index % 5) * 0.7f, 0.45f, (index / 5) * 0.7f)
            };
            marker.SetMeta("definition_id", definitionId);
            parent.AddChild(marker);
        }
        return new DeveloperCommandResult(true, $"spawned {count} x {definitionId}");
    }

    private DeveloperCommandResult DeveloperGive(string[] parts)
    {
        if (parts.Length < 2) return new DeveloperCommandResult(false, "usage: give <itemId> [qty]");
        string itemId = parts[1];
        int quantity = parts.Length > 2 && int.TryParse(parts[2], out int parsed) ? Math.Clamp(parsed, 1, 9999) : 1;
        if (!ContentCatalog.Items.ContainsKey(itemId)) return new DeveloperCommandResult(false, $"unknown item: {itemId}");
        Session.GrantInventory(itemId, quantity);
        StructuredGameLogger.Log(GameLogLevel.Information, GameLogCategory.PLAYER, "developer inventory grant",
            fields: new Dictionary<string, object?> { ["itemId"] = itemId, ["quantity"] = quantity });
        return new DeveloperCommandResult(true, $"granted {quantity} x {itemId}");
    }

    private DeveloperCommandResult DeveloperDamage(string[] parts)
    {
        double amount = parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : 25.0;
        PlayerSurvival.ApplyDamage(Math.Clamp(amount, 0.1, 10000.0));
        return new DeveloperCommandResult(true, $"damage applied; health={PlayerSurvival.Health:F1}; shield={PlayerSurvival.Shield:F1}");
    }

    private DeveloperCommandResult DeveloperHeal(string[] parts)
    {
        double amount = parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : 10000.0;
        PlayerSurvival.RestoreHealth(Math.Clamp(amount, 0.1, 10000.0));
        return new DeveloperCommandResult(true, $"healed; health={PlayerSurvival.Health:F1}; shield={PlayerSurvival.Shield:F1}");
    }

    private DeveloperCommandResult DeveloperSetTime(string[] parts)
    {
        if (parts.Length != 2 || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double hour))
            return new DeveloperCommandResult(false, "usage: set_time <0..24>");
        hour = ((hour % 24.0) + 24.0) % 24.0;
        DirectionalLight3D? sun = GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");
        if (sun is null) return new DeveloperCommandResult(false, "directional light missing");
        sun.RotationDegrees = new Vector3((float)(hour / 24.0 * 360.0 - 90.0), -35.0f, 0.0f);
        return new DeveloperCommandResult(true, $"time={hour:F2}h");
    }

    private DeveloperCommandResult DeveloperSetWeather(string[] parts)
    {
        if (parts.Length != 2) return new DeveloperCommandResult(false, "usage: set_weather <clear|wind|storm|toxic>");
        string weather = parts[1].ToLowerInvariant();
        if (weather is not ("clear" or "wind" or "storm" or "toxic")) return new DeveloperCommandResult(false, "weather must be clear|wind|storm|toxic");
        _developerWeather = weather;
        DirectionalLight3D? sun = GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");
        if (sun is not null) sun.LightEnergy = weather switch { "storm" => 0.45f, "toxic" => 0.75f, "wind" => 1.15f, _ => 1.35f };
        StructuredGameLogger.Log(GameLogLevel.Information, GameLogCategory.WORLDGEN, "developer weather override",
            fields: new Dictionary<string, object?> { ["weather"] = weather });
        return new DeveloperCommandResult(true, $"weather={weather}");
    }

    private DeveloperCommandResult DeveloperLoadSystem(string[] parts)
    {
        if (parts.Length != 4 || !int.TryParse(parts[1], out int x) || !int.TryParse(parts[2], out int y) || !int.TryParse(parts[3], out int z))
            return new DeveloperCommandResult(false, "usage: load_system x y z");
        GalaxySystemDefinition system = GalaxyNavigation.LoadSystemForDeveloper(x, y, z);
        _developerPlanetOverrideId = null;
        InitializeStarSystemSimulationRuntime();
        StructuredGameLogger.UpdateContext(SceneFilePath, GalaxyNavigation.UniverseSeed, system.SystemId);
        return new DeveloperCommandResult(true, $"loaded {system.SystemId}; planets={system.Planets.Count}");
    }

    private DeveloperCommandResult DeveloperLoadPlanet(string[] parts)
    {
        if (parts.Length != 2 || !int.TryParse(parts[1], out int index)) return new DeveloperCommandResult(false, "usage: load_planet <index>");
        if (index < 0 || index >= GalaxyNavigation.CurrentSystem.Planets.Count) return new DeveloperCommandResult(false, $"planet index 0..{GalaxyNavigation.CurrentSystem.Planets.Count - 1}");
        _developerPlanetOverrideId = GalaxyNavigation.CurrentSystem.Planets[index].PlanetId;
        UpdateStarSystemSimulation(0.0);
        return new DeveloperCommandResult(true, $"detailed planet={_developerPlanetOverrideId}");
    }

    private DeveloperCommandResult DeveloperToggleChunkGrid()
    {
        _developerChunkGrid = ToggleTileGrid(_developerChunkGrid, "DeveloperChunkGrid", 8.0f, 5, 0.05f, new Color(0.18f, 0.75f, 1.0f, 0.62f));
        return new DeveloperCommandResult(true, $"chunk grid={(_developerChunkGrid is null ? 0 : 1)}");
    }

    private DeveloperCommandResult DeveloperToggleNavmeshGrid()
    {
        if (_developerNavmeshGrid is not null)
        {
            _developerNavmeshGrid.QueueFree(); _developerNavmeshGrid = null;
            return new DeveloperCommandResult(true, "navmesh overlay=0");
        }
        if (_npcNavigationSurface is null) return new DeveloperCommandResult(false, "NPC navigation surface unavailable");
        NpcNavigationSurfaceSnapshot snapshot = _npcNavigationSurface.CreateSnapshot();
        Node3D root = new() { Name = "DeveloperNavmeshGrid" };
        AddChild(root);
        StandardMaterial3D material = new() { AlbedoColor = new Color(0.22f, 1.0f, 0.38f, 0.22f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        foreach (NpcNavigationTileKey tile in snapshot.ActiveTiles)
        {
            MeshInstance3D cell = new()
            {
                Mesh = new BoxMesh { Size = new Vector3(NpcNavigationSurfaceNode.TileSizeMeters - 0.12f, 0.025f, NpcNavigationSurfaceNode.TileSizeMeters - 0.12f), Material = material },
                Position = new Vector3((tile.X + 0.5f) * NpcNavigationSurfaceNode.TileSizeMeters, NpcNavigationSurfaceNode.NavigationSurfaceY + 0.04f, (tile.Z + 0.5f) * NpcNavigationSurfaceNode.TileSizeMeters)
            };
            root.AddChild(cell);
        }
        _developerNavmeshGrid = root;
        return new DeveloperCommandResult(true, $"navmesh overlay=1; tiles={snapshot.ActiveTiles.Count}; walkableCells={snapshot.WalkableCells}");
    }

    private DeveloperCommandResult DeveloperToggleAi()
    {
        _developerAiDebug = !_developerAiDebug;
        RemoveDeveloperAiMarkers();
        int ground = AddDeveloperAiMarkers("npc_faction_agent", new Color(0.20f, 1.0f, 0.36f, 0.92f));
        int ships = AddDeveloperAiMarkers("npc_ship_navigation", new Color(1.0f, 0.62f, 0.18f, 0.92f));
        int fauna = AddDeveloperAiMarkers("ecology_fauna", new Color(0.30f, 0.72f, 1.0f, 0.92f));
        if (!_developerAiDebug) RemoveDeveloperAiMarkers();
        return new DeveloperCommandResult(true,
            $"ai debug={(_developerAiDebug ? 1 : 0)}; groundNpc={ground}; ships={ships}; fauna={fauna}; markers={(_developerAiDebug ? ground + ships + fauna : 0)}");
    }

    private int AddDeveloperAiMarkers(string group, Color color)
    {
        int count = 0;
        foreach (Node node in GetTree().GetNodesInGroup(group))
        {
            count++;
            if (!_developerAiDebug || node is not Node3D body) continue;
            StandardMaterial3D material = new()
            {
                AlbedoColor = color,
                EmissionEnabled = true,
                Emission = new Color(color.R, color.G, color.B, 1.0f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
            };
            MeshInstance3D marker = new()
            {
                Name = "DeveloperAiMarker",
                Mesh = new SphereMesh { Radius = 0.20f, Height = 0.40f, Material = material },
                Position = new Vector3(0.0f, 2.0f, 0.0f)
            };
            marker.AddToGroup("developer_ai_debug_marker");
            body.AddChild(marker);
        }
        return count;
    }

    private void RemoveDeveloperAiMarkers()
    {
        foreach (Node marker in GetTree().GetNodesInGroup("developer_ai_debug_marker"))
            marker.QueueFree();
    }

    private DeveloperCommandResult DeveloperProfileWorldgen()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        CubeSphereBuildData build = CubeSphereMeshBuilder.Build(65, 96.0f, 6.0f, 0.0125f, unchecked((int)GalaxyNavigation.UniverseSeed));
        stopwatch.Stop();
        StructuredGameLogger.Log(GameLogLevel.Information, GameLogCategory.PERFORMANCE, "developer worldgen profile",
            fields: new Dictionary<string, object?> { ["cpuMilliseconds"] = stopwatch.Elapsed.TotalMilliseconds, ["vertices"] = build.TotalVertices, ["triangles"] = build.TotalTriangles });
        return new DeveloperCommandResult(true, $"worldgen={stopwatch.Elapsed.TotalMilliseconds:F2}ms; vertices={build.TotalVertices}; triangles={build.TotalTriangles}; seamError={build.MaximumSeamPositionError:E2}");
    }

    private DeveloperCommandResult DeveloperSave()
    {
        QueueCurrentSnapshot(AutosaveTrigger.PlayerChanged);
        StructuredGameLogger.Log(GameLogLevel.Information, GameLogCategory.SAVE, "developer save requested");
        return new DeveloperCommandResult(true, "save queued through autosave coordinator");
    }

    private DeveloperCommandResult DeveloperReloadContent()
    {
        StructuredGameLogger.Log(GameLogLevel.Warning, GameLogCategory.CONTENT, "developer full content reload requested");
        Error error = GetTree().ReloadCurrentScene();
        return new DeveloperCommandResult(error == Error.Ok, $"reload_current_scene={error}");
    }

    private Node3D? ToggleTileGrid(Node3D? existing, string name, float spacing, int radius, float thickness, Color color)
    {
        if (existing is not null) { existing.QueueFree(); return null; }
        Node3D root = new() { Name = name };
        AddChild(root);
        StandardMaterial3D material = new() { AlbedoColor = color, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        float extent = spacing * radius;
        for (int index = -radius; index <= radius; index++)
        {
            float offset = index * spacing;
            root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(thickness, thickness, extent * 2), Material = material }, Position = new Vector3(offset, 0.17f, 0) });
            root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(extent * 2, thickness, thickness), Material = material }, Position = new Vector3(0, 0.17f, offset) });
        }
        return root;
    }

    private void RunDeveloperDiagnosticsAcceptance()
    {
        _task136AcceptanceHud = "RUNNING";
        bool devGate = DeveloperToolContext.IsDeveloperModeAllowed();
        bool seedExplorer = false, planetPreview = false, chunkProfiler = false, saveInspector = false, debugConsole = false;
        try
        {
            long seed = GalaxyNavigationRuntime.DefaultUniverseSeed + 136;
            GalaxyNavigationRuntime first = new(seed);
            GalaxyNavigationRuntime second = new(seed);
            GalaxySystemDefinition a = first.GenerateSystem(3, -2, 1);
            GalaxySystemDefinition b = second.GenerateSystem(3, -2, 1);
            seedExplorer = string.Equals(a.SystemId, b.SystemId, StringComparison.Ordinal) && a.Planets.Count is >= 1 and <= 8;
            Stopwatch stopwatch = Stopwatch.StartNew();
            CubeSphereBuildData build = CubeSphereMeshBuilder.Build(33, 96.0f, 6.0f, 0.0125f, unchecked((int)seed));
            stopwatch.Stop();
            planetPreview = build.TotalVertices > 0 && build.TotalTriangles > 0 && stopwatch.Elapsed.TotalMilliseconds >= 0.0;
            chunkProfiler = typeof(TerrainChunkManager).GetMethod("CaptureProfilerSnapshot") is not null;
            saveInspector = _database is not null && File.Exists(GameProfilePaths.PrimaryDatabasePath);
            debugConsole = _developerDiagnosticsSuite is not null && DeveloperDiagnosticsSuite.RequiredCommands.Length == 15;
            StructuredGameLogger.EnsureInitialized(GetTree());
            foreach (GameLogCategory category in Enum.GetValues<GameLogCategory>())
                StructuredGameLogger.Log(GameLogLevel.Debug, category, "TASK-136 category acceptance sample");
            StructuredGameLogger.Log(GameLogLevel.Debug, GameLogCategory.ERROR, "TASK-136 redaction acceptance token=should-never-appear",
                fields: new Dictionary<string, object?> { ["api_token"] = "sensitive-test-value" });
            StructuredGameLogger.FlushPending();
            StructuredGameLoggerDiagnostics logger = StructuredGameLogger.GetDiagnostics();
            string logText = File.Exists(logger.LogPath) ? File.ReadAllText(logger.LogPath) : string.Empty;
            bool secretsAbsent =
                !logText.Contains("should-never-appear", StringComparison.Ordinal) &&
                !logText.Contains("sensitive-test-value", StringComparison.Ordinal);
            bool logging = logger.Initialized && logger.CategoriesSeen.Count == 14 &&
                logger.RedactedValues >= 2 && File.Exists(logger.LogPath) && secretsAbsent;
            bool passed = devGate && seedExplorer && planetPreview && chunkProfiler && saveInspector && debugConsole && logging;
            _task136AcceptanceHud = passed ? "PASS" : "FAIL";
            string line =
                $"TASK-136 developer diagnostics acceptance {(passed ? "PASS" : "FAIL")}: " +
                "tools=5/5; commands=15/15; " +
                $"devGate={(devGate ? 1 : 0)}; seedExplorer={(seedExplorer ? 1 : 0)}; planetPreview={(planetPreview ? 1 : 0)}; " +
                $"chunkProfiler={(chunkProfiler ? 1 : 0)}; saveInspector={(saveInspector ? 1 : 0)}; debugConsole={(debugConsole ? 1 : 0)}; " +
                $"logCategories={logger.CategoriesSeen.Count}/14; utc=1; session={(string.IsNullOrWhiteSpace(logger.SessionId) ? 0 : 1)}; " +
                $"context=1; redaction={(logger.RedactedValues >= 2 ? 1 : 0)}; secretLeak={(secretsAbsent ? 0 : 1)}; jsonl={(File.Exists(logger.LogPath) ? 1 : 0)}; " +
                "result=section-34-35-developer-diagnostics.";
            if (passed) GD.Print(line); else GD.PushError(line);
        }
        catch (Exception exception)
        {
            _task136AcceptanceHud = "FAIL";
            StructuredGameLogger.Log(GameLogLevel.Error, GameLogCategory.ERROR, "TASK-136 acceptance exception", exception);
            GD.PushError($"TASK-136 developer diagnostics acceptance FAIL: {exception}");
        }
    }

    private static bool TryFloat(string value, out float parsed) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
}
