using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

public partial class SalvageRepairSlice
{
    private sealed record EcologyMultiMeshGroup(
        MultiMeshInstance3D Lod0Node,
        MultiMeshInstance3D Lod1Node,
        IReadOnlyList<EcologyFloraPlacement> Placements,
        VegetationRegionCoordinate Region,
        bool SmallObject);

    private EcologyCatalog? _ecologyCatalog;
    private EcologyPlan? _ecologyPlan;
    private EcologyRuntime? _ecologyRuntime;
    private Node3D? _ecologyRoot;
    private PanelContainer? _ecologyCatalogPanel;
    private Label? _ecologyCatalogLabel;
    private readonly List<EcologyFaunaNode> _ecologyFaunaNodes = new();
    private readonly Dictionary<string, EcologyFloraSpecimenNode>
        _promotedFloraNodes = new(StringComparer.Ordinal);
    private readonly List<EcologyMultiMeshGroup> _ecologyFloraGroups = new();
    private readonly Dictionary<string, HashSet<VegetationPromotionReason>>
        _vegetationPromotionReasons = new(StringComparer.Ordinal);
    private bool _ecologyCatalogOpen;
    private bool _ecologyFaunaTab;
    private int _ecologyCatalogSelection;
    private string _ecologyFeedback = "";
    private Task<EcologyAcceptanceReport>? _ecologyAcceptanceTask;
    private EcologyAcceptanceReport? _ecologyAcceptanceReport;
    private string _ecologyAcceptanceHud = "READY";

    private EcologyCatalog EcologyCatalog => _ecologyCatalog ??
        throw new InvalidOperationException("Ecology catalog is unavailable.");

    private EcologyPlan EcologyPlan => _ecologyPlan ??
        throw new InvalidOperationException("Ecology plan is unavailable.");

    private EcologyRuntime Ecology => _ecologyRuntime ??
        throw new InvalidOperationException("Ecology runtime is unavailable.");

    private void BindEcologySceneNodes()
    {
        _ecologyRoot = GetNodeOrNull<Node3D>("Gameplay/Ecology");
        _ecologyCatalogPanel = GetNodeOrNull<PanelContainer>("Hud/EcologyCatalog");
        _ecologyCatalogLabel = GetNodeOrNull<Label>("Hud/EcologyCatalog/Label");
        if (_ecologyRoot is null || _ecologyCatalogPanel is null ||
            _ecologyCatalogLabel is null)
        {
            throw new InvalidOperationException(
                "Vertical slice scene is missing ecology nodes or catalogue HUD.");
        }
    }

    private static EcologyCatalog LoadEcologyCatalog(GameContentCatalog contentCatalog)
    {
        string path = "res://Content/ecology.json";
        using Godot.FileAccess file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read) ??
            throw new InvalidOperationException($"Unable to open {path}.");
        EcologyCatalog catalog = EcologyCatalog.LoadFromJson(
            file.GetAsText(),
            contentCatalog);
        int ground = catalog.Fauna.Values.Count(fauna =>
            string.Equals(fauna.MovementMode, "Ground", StringComparison.Ordinal));
        int flying = catalog.Fauna.Values.Count(fauna =>
            string.Equals(fauna.MovementMode, "Flying", StringComparison.Ordinal));
        int aquatic = catalog.Fauna.Values.Count(fauna =>
            string.Equals(fauna.MovementMode, "Aquatic", StringComparison.Ordinal));
        GD.Print(
            "TASK-116 ecology catalog READY: " +
            $"schema={catalog.SchemaVersion}; biomes={catalog.Biomes.Count}; " +
            $"flora={catalog.Flora.Count}; fauna={catalog.Fauna.Count}; " +
            $"ground={ground}; flying={flying}; aquatic={aquatic}; " +
            $"limits={catalog.ActiveFaunaLimit}/{catalog.SimplifiedFaunaLimit}; " +
            $"seed={catalog.WorldSeed}; region={catalog.RegionKey}.");
        return catalog;
    }

    private void InitializeEcologyRuntime(EcologySaveData? saveData)
    {
        _ecologyPlan = EcologyPlanner.Plan(EcologyCatalog);
        _ecologyRuntime = new EcologyRuntime(EcologyCatalog, EcologyPlan, saveData);
        _ecologyCatalogOpen = false;
        _ecologyFaunaTab = false;
        _ecologyCatalogSelection = 0;
        _ecologyFeedback = saveData is null
            ? "fresh/legacy ecology regenerated from seed"
            : "ecology discoveries and harvest deltas restored";
        if (_ecologyCatalogPanel is not null)
        {
            _ecologyCatalogPanel.Visible = false;
        }
        RebuildEcologyScene();
    }

    private void RebuildEcologyScene()
    {
        PlayerController? player = _player;
        if (_ecologyRoot is null || _ecologyPlan is null ||
            _ecologyRuntime is null || _ecologyCatalog is null || player is null)
        {
            return;
        }

        _aerialSteeringRuntime?.RemoveGroup("flying_fauna");
        foreach (Node child in _ecologyRoot.GetChildren())
        {
            _ecologyRoot.RemoveChild(child);
            child.QueueFree();
        }
        _ecologyFaunaNodes.Clear();
        _promotedFloraNodes.Clear();
        _vegetationPromotionReasons.Clear();
        _ecologyFloraGroups.Clear();

        if (_planetSurfaceContentProfile?.WaterHabitatEnabled != false)
        {
            PlanetEnvironmentColor? waterColor =
                _planetSurfaceContentProfile?.Environment.WaterColor;
            MeshInstance3D habitat = new()
            {
                Name = "AquaticHabitat",
                Position = new Vector3(-25.5f, 0.04f, 25.5f),
                Mesh = new BoxMesh
                {
                    Size = new Vector3(15.0f, 0.08f, 15.0f),
                    Material = new StandardMaterial3D
                    {
                        AlbedoColor = waterColor is null
                            ? new Color(0.03f, 0.20f, 0.30f, 0.72f)
                            : new Color(
                                (float)waterColor.R,
                                (float)waterColor.G,
                                (float)waterColor.B,
                                0.72f),
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        Roughness = 0.18f
                    }
                }
            };
            _ecologyRoot.AddChild(habitat);
        }

        BuildRegionalVegetationMultiMeshes();

        foreach (EcologyFaunaSpawn spawn in EcologyPlan.ActiveFauna)
        {
            EcologyFaunaNode faunaNode = new();
            faunaNode.Configure(
                EcologyCatalog.GetFauna(spawn.FaunaId),
                spawn,
                player,
                _aerialSteeringRuntime,
                CurrentTerrainProfile,
                CurrentPlanetSurfaceCurvedPatch);
            faunaNode.Observed += OnEcologyFaunaObserved;
            _ecologyRoot.AddChild(faunaNode);
            _ecologyFaunaNodes.Add(faunaNode);
        }

        PrintFaunaModularReady();
        PromoteNearbyFlora(force: true);
        GD.Print(
            "TASK-116 ecology scene binding PASS: " +
            $"floraInstances={EcologyPlan.Flora.Count - Ecology.RemovedFloraCount}; " +
            $"multiMeshGroups={_ecologyFloraGroups.Count * 2}; " +
            $"vegetationBatches={_ecologyFloraGroups.Count}; " +
            $"vegetationRegions={_ecologyFloraGroups.Select(group => group.Region).Distinct().Count()}; " +
            $"activeFauna={_ecologyFaunaNodes.Count}; " +
            $"simplifiedFauna={EcologyPlan.SimplifiedFauna.Count}; " +
            "lod=regional-near/mid/cull; promotion=proximity+scan+damage+harvest+quest; " +
            "residency=TASK-194; persistence=seed+deltas.");
    }

    private void UpdateEcology(double delta)
    {
        if (_ecologyRuntime is null || _ecologyPlan is null)
        {
            return;
        }
        UpdateRegionalVegetationVisibility();
        UpdateFaunaFlocking();
        if (_stageOneVoyageRuntime?.Piloted == true)
        {
            return;
        }
        Ecology.TickSimplified(delta);
        PromoteNearbyFlora(force: false);
    }

    private void UpdateFaunaFlocking()
    {
        if (_ecologyFaunaNodes.Count == 0)
        {
            return;
        }
        FaunaFlockSample[] population = _ecologyFaunaNodes
            .Where(node => GodotObject.IsInstanceValid(node))
            .Select(node => node.CreateFlockSample())
            .ToArray();
        foreach (EcologyFaunaNode node in _ecologyFaunaNodes)
        {
            if (!GodotObject.IsInstanceValid(node))
            {
                continue;
            }
            node.SetFlockSteering(FaunaFlockRuntime.Compute(
                node.CreateFlockSample(),
                population));
        }
        _faunaFlockUpdatePasses++;
    }

    private void PromoteNearbyFlora(bool force)
    {
        if (_player is null || _ecologyRoot is null ||
            _ecologyPlan is null || _ecologyRuntime is null)
        {
            return;
        }

        Vector3 observer = _player.GlobalPosition;
        Vector3 logicalObserver = WorldToPlanetSurfaceLogicalPosition(observer);
        EcologyFloraPlacement[] nearby = EcologyPlan.Flora
            .Where(placement => !Ecology.IsFloraRemoved(placement.InstanceId))
            .Select(placement => new
            {
                Placement = placement,
                Distance = logicalObserver.DistanceTo(new Vector3(
                    (float)placement.PositionX,
                    (float)FloraSurfaceY(placement),
                    (float)placement.PositionZ))
            })
            .Where(item => VegetationRegionRuntime.ShouldPromote(
                VegetationPromotionReason.Proximity, item.Distance))
            .OrderBy(item => item.Distance)
            .Take(VegetationRegionRuntime.MaximumNearbyPromotions)
            .Select(item => item.Placement)
            .ToArray();
        EcologyFloraPlacement[] quest = EcologyPlan.Flora
            .Where(placement => !Ecology.IsFloraRemoved(placement.InstanceId) &&
                IsFloraQuestRelevant(placement))
            .OrderBy(placement => logicalObserver.DistanceSquaredTo(new Vector3(
                (float)placement.PositionX, 0.0f, (float)placement.PositionZ)))
            .Take(VegetationRegionRuntime.MaximumQuestPromotions)
            .ToArray();
        EcologyFloraPlacement[] desired = nearby
            .Concat(quest)
            .GroupBy(placement => placement.InstanceId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        foreach (EcologyFloraPlacement placement in nearby)
        {
            RecordVegetationPromotionReason(placement.InstanceId, VegetationPromotionReason.Proximity);
        }
        foreach (EcologyFloraPlacement placement in quest)
        {
            RecordVegetationPromotionReason(placement.InstanceId, VegetationPromotionReason.Quest);
        }
        HashSet<string> desiredIds = desired
            .Select(placement => placement.InstanceId)
            .ToHashSet(StringComparer.Ordinal);

        foreach ((string instanceId, EcologyFloraSpecimenNode node) in
            _promotedFloraNodes.ToArray())
        {
            float distance = node.GlobalPosition.DistanceTo(observer);
            TryFindFloraPlacement(instanceId, out EcologyFloraPlacement? placement);
            bool questRelevant = placement is not null && IsFloraQuestRelevant(placement);
            if (!desiredIds.Contains(instanceId) &&
                (force || VegetationRegionRuntime.ShouldDemote(distance, questRelevant)))
            {
                node.QueueFree();
                _promotedFloraNodes.Remove(instanceId);
            }
        }

        foreach (EcologyFloraPlacement placement in desired)
        {
            VegetationPromotionReason reason = IsFloraQuestRelevant(placement)
                ? VegetationPromotionReason.Quest
                : VegetationPromotionReason.Proximity;
            EnsureFloraPromoted(placement, reason);
        }
    }

    private bool HandleEcologyInput(Key physical, Key logical)
    {
        if (_ecologyCatalogOpen)
        {
            if (Matches(physical, logical, Key.Escape) ||
                Matches(physical, logical, Key.O))
            {
                CloseEcologyCatalog(L("ui.ecology.closed"));
            }
            else if (Matches(physical, logical, Key.Tab))
            {
                _ecologyFaunaTab = !_ecologyFaunaTab;
                _ecologyCatalogSelection = 0;
                UpdateEcologyCatalogPanel();
            }
            else if (Matches(physical, logical, Key.Up))
            {
                MoveEcologyCatalogSelection(-1);
            }
            else if (Matches(physical, logical, Key.Down))
            {
                MoveEcologyCatalogSelection(1);
            }
            else if (Matches(physical, logical, Key.V))
            {
                PulseEcologyScanner();
                UpdateEcologyCatalogPanel();
            }
            return true;
        }

        if (_stageOneVoyageRuntime?.Piloted == true)
        {
            return false;
        }

        if (Matches(physical, logical, Key.O) &&
            (_state == SalvageRepairSliceState.Ready ||
             _state == SalvageRepairSliceState.Passed))
        {
            OpenEcologyCatalog();
            return true;
        }

        if (Matches(physical, logical, Key.V) &&
            (_state == SalvageRepairSliceState.Ready ||
             _state == SalvageRepairSliceState.Passed))
        {
            PulseEcologyScanner();
            return true;
        }

        return false;
    }

    private void OpenEcologyCatalog()
    {
        if (_ecologyCatalogPanel is null || _ecologyCatalogLabel is null)
        {
            return;
        }
        CloseRecipeSelector();
        CloseStationServices();
        CloseBaseBuildMode();
        CloseDiscoveryCatalog();
        CloseShipManagement();
        CloseGalaxyMap();
        _ecologyCatalogOpen = true;
        _ecologyCatalogSelection = 0;
        _ecologyCatalogPanel.Visible = true;
        UpdateEcologyCatalogPanel();
        _status = L("ui.ecology.opened");
    }

    private void CloseEcologyCatalog(string status = "")
    {
        _ecologyCatalogOpen = false;
        if (_ecologyCatalogPanel is not null)
        {
            _ecologyCatalogPanel.Visible = false;
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            _status = status;
        }
    }

    private void MoveEcologyCatalogSelection(int delta)
    {
        int count = _ecologyFaunaTab
            ? EcologyCatalog.Fauna.Count
            : EcologyCatalog.Flora.Count;
        if (count <= 0)
        {
            return;
        }
        _ecologyCatalogSelection =
            (_ecologyCatalogSelection + delta + count) % count;
        UpdateEcologyCatalogPanel();
    }

    private void UpdateEcologyCatalogPanel()
    {
        if (!_ecologyCatalogOpen || _ecologyCatalogLabel is null || _ecologyRuntime is null || _ecologyCatalog is null)
        {
            return;
        }

        string[] lines;
        if (_ecologyFaunaTab)
        {
            EcologyFaunaDefinition[] definitions = EcologyCatalog.Fauna.Values.OrderBy(definition => definition.FaunaId, StringComparer.Ordinal).ToArray();
            _ecologyCatalogSelection = Math.Clamp(_ecologyCatalogSelection, 0, Math.Max(0, definitions.Length - 1));
            lines = definitions.Select((definition, index) =>
            {
                bool known = Ecology.DiscoveredFaunaIds.Contains(definition.FaunaId, StringComparer.Ordinal);
                string marker = index == _ecologyCatalogSelection ? ">" : " ";
                return known
                    ? $"{marker} " + LF("ui.ecology.fauna_row",
                        ("name", GameLocalizationService.Text(definition.LocalizationKey)), ("movement", LocalizeEcologyToken(definition.MovementMode)),
                        ("body", LocalizeEcologyToken(definition.BodyPlan)), ("diet", LocalizeEcologyToken(definition.Diet)),
                        ("aggression", definition.Aggression.ToString("0.00", CultureInfo.InvariantCulture)))
                    : $"{marker} {L("ui.ecology.unknown_fauna")}";
            }).ToArray();
        }
        else
        {
            EcologyFloraDefinition[] definitions = EcologyCatalog.Flora.Values.OrderBy(definition => definition.FloraId, StringComparer.Ordinal).ToArray();
            _ecologyCatalogSelection = Math.Clamp(_ecologyCatalogSelection, 0, Math.Max(0, definitions.Length - 1));
            lines = definitions.Select((definition, index) =>
            {
                bool known = Ecology.DiscoveredFloraIds.Contains(definition.FloraId, StringComparer.Ordinal);
                string marker = index == _ecologyCatalogSelection ? ">" : " ";
                return known
                    ? $"{marker} " + LF("ui.ecology.flora_row",
                        ("name", GameLocalizationService.Text(definition.LocalizationKey)), ("shape", LocalizeEcologyToken(definition.Shape)),
                        ("harvest", GetShortContentId(definition.HarvestDefinitionId)), ("hazard", LocalizeEcologyToken(definition.Hazard)))
                    : $"{marker} {L("ui.ecology.unknown_flora")}";
            }).ToArray();
        }

        string body = string.Join("\n", lines.Take(26));
        _ecologyCatalogLabel.Text = string.Join("\n", new[]
        {
            L("ui.ecology.header"),
            LF("ui.ecology.summary", ("region", EcologyCatalog.RegionKey), ("seed", EcologyCatalog.WorldSeed),
                ("floraKnown", Ecology.DiscoveredFloraCount), ("floraTotal", EcologyCatalog.Flora.Count),
                ("faunaKnown", Ecology.DiscoveredFaunaCount), ("faunaTotal", EcologyCatalog.Fauna.Count),
                ("harvested", Ecology.RemovedFloraCount), ("points", Ecology.DiscoveryPoints)),
            LF("ui.ecology.tab", ("tab", L(_ecologyFaunaTab ? "ui.ecology.tab.fauna" : "ui.ecology.tab.flora")), ("feedback", _ecologyFeedback)),
            "",
            body
        });
    }

    private static string LocalizeEcologyToken(string token)
    {
        string key = "ui.ecology.token." + token.ToLowerInvariant();
        return GameLocalizationService.ContainsKey(key)
            ? GameLocalizationService.Text(key)
            : token;
    }

    private void PulseEcologyScanner()
    {
        if (_player is null || _ecologyRuntime is null || _ecologyPlan is null)
        {
            return;
        }

        const float scanRange = 16.0f;
        Vector3 observer = _player.GlobalPosition;
        Vector3 logicalObserver = WorldToPlanetSurfaceLogicalPosition(observer);
        EcologyFaunaNode? fauna = _ecologyFaunaNodes
            .Where(node => node.Visible)
            .OrderBy(node => node.GlobalPosition.DistanceSquaredTo(observer))
            .FirstOrDefault();
        float faunaDistance = fauna is null
            ? float.PositiveInfinity
            : fauna.GlobalPosition.DistanceTo(observer);
        EcologyFloraPlacement? flora = EcologyPlan.Flora
            .Where(placement => !Ecology.IsFloraRemoved(placement.InstanceId))
            .OrderBy(placement => logicalObserver.DistanceSquaredTo(new Vector3(
                (float)placement.PositionX,
                0.55f,
                (float)placement.PositionZ)))
            .FirstOrDefault();
        float floraDistance = flora is null
            ? float.PositiveInfinity
            : logicalObserver.DistanceTo(new Vector3(
                (float)flora.PositionX,
                0.55f,
                (float)flora.PositionZ));

        if (Math.Min(faunaDistance, floraDistance) > scanRange)
        {
            _ecologyFeedback = LF("ui.ecology.no_signal", ("range", scanRange.ToString("0")));
            _status = _ecologyFeedback;
            return;
        }

        RecordPlayerMultitoolUse(PlayerMultitoolFunction.Analyzer, "ecology-scan");
        bool changed;
        string kind;
        string species;
        string message;
        if (faunaDistance <= floraDistance && fauna is not null)
        {
            changed = Ecology.TryScanFauna(
                fauna.InstanceId,
                out EcologyFaunaDefinition definition,
                out message);
            kind = "fauna";
            species = definition.FaunaId;
        }
        else if (flora is not null)
        {
            EnsureFloraPromoted(flora, VegetationPromotionReason.Scan);
            changed = Ecology.TryScanFlora(
                flora.InstanceId,
                out EcologyFloraDefinition definition,
                out message);
            kind = "flora";
            species = definition.FloraId;
        }
        else
        {
            return;
        }

        _ecologyFeedback = message;
        _status = message;
        if (changed)
        {
            RecordProceduralQuestObjective(
                ProceduralQuestObjectiveType.ScanSpecies,
                species,
                1,
                queueAutosave: false);
            QueueCurrentSnapshot(AutosaveTrigger.DiscoveryChanged);
            GD.Print(
                "TASK-116 player ecology scan PASS: " +
                $"kind={kind}; species={species}; " +
                $"flora={Ecology.DiscoveredFloraCount}; " +
                $"fauna={Ecology.DiscoveredFaunaCount}; " +
                $"points={Ecology.DiscoveryPoints}.");
        }
    }

    private void OnEcologyFaunaObserved(EcologyFaunaNode node)
    {
        if (_ecologyRuntime is null)
        {
            return;
        }
        bool changed = Ecology.TryScanFauna(
            node.InstanceId,
            out EcologyFaunaDefinition definition,
            out string message);
        _ecologyFeedback = message;
        _status = message;
        if (changed)
        {
            RecordProceduralQuestObjective(
                ProceduralQuestObjectiveType.ScanSpecies,
                definition.FaunaId,
                1,
                queueAutosave: false);
            QueueCurrentSnapshot(AutosaveTrigger.DiscoveryChanged);
            GD.Print(
                "TASK-116 player ecology scan PASS: " +
                $"kind=fauna; species={definition.FaunaId}; " +
                $"flora={Ecology.DiscoveredFloraCount}; " +
                $"fauna={Ecology.DiscoveredFaunaCount}; " +
                $"points={Ecology.DiscoveryPoints}.");
        }
    }

    private void OnEcologyFloraHarvestRequested(
        EcologyFloraSpecimenNode node,
        Node3D interactor)
    {
        if (_ecologyRuntime is null || _session is null)
        {
            return;
        }
        RecordVegetationPromotionReason(node.InstanceId, VegetationPromotionReason.Harvest);
        bool changed = Ecology.TryHarvestFlora(
            node.InstanceId,
            out EcologyFloraDefinition definition,
            out string message);
        _ecologyFeedback = message;
        _status = message;
        if (!changed)
        {
            return;
        }

        GrantSharedInventory(definition.HarvestDefinitionId, 1);
        RecordProceduralQuestObjective(
            ProceduralQuestObjectiveType.CollectResource,
            definition.HarvestDefinitionId,
            1,
            queueAutosave: false);
        node.QueueFree();
        _promotedFloraNodes.Remove(node.InstanceId);
        QueueCurrentSnapshot(AutosaveTrigger.DiscoveryChanged);
        GD.Print(
            "TASK-116 player flora harvest PASS: " +
            $"instance={node.InstanceId}; species={definition.FloraId}; " +
            $"yield={definition.HarvestDefinitionId}; quantity=1; " +
            $"removed={Ecology.RemovedFloraCount}; interactor={interactor.Name}.");
        RebuildEcologyFloraMultiMeshes();
    }

    private void RebuildEcologyFloraMultiMeshes()
    {
        ClearRegionalVegetationMultiMeshes();
        BuildRegionalVegetationMultiMeshes();
        UpdateRegionalVegetationVisibility();
    }

    private void AdjustEcologyFloraCurvatureAnchor(
        PlanetSurfaceCurvedPatchDescriptor previousPatch,
        PlanetSurfaceCurvedPatchDescriptor nextPatch)
    {
        foreach (EcologyMultiMeshGroup group in _ecologyFloraGroups)
        {
            foreach (MultiMeshInstance3D node in new[] { group.Lod0Node, group.Lod1Node })
            {
                if (!GodotObject.IsInstanceValid(node) || node.Multimesh is not MultiMesh multiMesh)
                {
                    continue;
                }
                int count = Math.Min(multiMesh.InstanceCount, group.Placements.Count);
                for (int index = 0; index < count; index++)
                {
                    EcologyFloraPlacement placement = group.Placements[index];
                    Transform3D transform = multiMesh.GetInstanceTransform(index);
                    double semanticHeight = transform.Origin.Y +
                        previousPatch.TangentSagMeters(placement.PositionX, placement.PositionZ);
                    transform.Origin = new Vector3(
                        transform.Origin.X,
                        (float)(semanticHeight - nextPatch.TangentSagMeters(
                            placement.PositionX, placement.PositionZ)),
                        transform.Origin.Z);
                    multiMesh.SetInstanceTransform(index, transform);
                }
            }
        }
    }

    private string BuildEcologyHudLine()
    {
        if (_ecologyRuntime is null || _ecologyPlan is null ||
            _ecologyCatalog is null)
        {
            return L("ui.hud.ecology.unavailable");
        }
        return LF(
            "ui.hud.ecology.summary",
            ("biomes", EcologyCatalog.Biomes.Count),
            ("floraFound", Ecology.DiscoveredFloraCount),
            ("floraTotal", EcologyCatalog.Flora.Count),
            ("floraInstanced", EcologyPlan.Flora.Count - Ecology.RemovedFloraCount),
            ("faunaFound", Ecology.DiscoveredFaunaCount),
            ("faunaTotal", EcologyCatalog.Fauna.Count),
            ("active", EcologyPlan.ActiveFauna.Count),
            ("simplified", EcologyPlan.SimplifiedFauna.Count),
            ("points", Ecology.DiscoveryPoints));
    }

    private void BeginEcologyAcceptance(string directory)
    {
        if (_ecologyAcceptanceTask is not null || _repairRecipe is null ||
            _ecologyCatalog is null)
        {
            return;
        }
        _ecologyAcceptanceReport = null;
        _ecologyAcceptanceHud = "RUNNING";
        string path = Path.Combine(directory, "save_1.ecology-test.db");
        _ecologyAcceptanceTask = EcologyAcceptanceRunner.RunAsync(
            path,
            SlotId,
            EcologyCatalog,
            RepairRecipe,
            _lifetimeCancellation.Token);
    }

    private void PollEcologyAcceptanceTask()
    {
        Task<EcologyAcceptanceReport>? task = _ecologyAcceptanceTask;
        if (task is null || !task.IsCompleted)
        {
            return;
        }
        _ecologyAcceptanceTask = null;
        if (task.IsCanceled)
        {
            _ecologyAcceptanceHud = "CANCELED";
            return;
        }
        if (task.IsFaulted)
        {
            _ecologyAcceptanceHud = "FAIL exception";
            GD.PushError(
                "TASK-116 ecology acceptance FAIL: " +
                (task.Exception?.GetBaseException().Message ?? "unknown exception"));
            UpdateCombinedCatalogAndShipAcceptanceState();
            return;
        }

        EcologyAcceptanceReport report = task.Result;
        _ecologyAcceptanceReport = report;
        _ecologyAcceptanceHud = report.Passed
            ? $"PASS biomes={report.Biomes}, flora={report.FloraModules}, fauna={report.FaunaArchetypes}, deltaOnly={(report.RegionDeltaOnly ? 1 : 0)}"
            : $"FAIL {report.Result}";
        string prefix = report.Passed
            ? "TASK-116 ecology acceptance PASS: "
            : "TASK-116 ecology acceptance FAIL: ";
        GD.Print(
            prefix +
            $"biomes={report.Biomes}; flora={report.FloraModules}; fauna={report.FaunaArchetypes}; " +
            $"movement={(report.MovementCoverage ? 1 : 0)}; " +
            $"bodyPlans={(report.BodyPlanCoverage ? 1 : 0)}; " +
            $"behaviors={(report.BehaviorCoverage ? 1 : 0)}; " +
            $"deterministic={(report.DeterministicPlacement ? 1 : 0)}; " +
            $"multiMesh={(report.FloraInstancing ? 1 : 0)}; " +
            $"populations={(report.PopulationLimits ? 1 : 0)}; " +
            $"updateTiers={(report.UpdateTiers ? 1 : 0)}; " +
            $"behaviorRuntime={(report.BehaviorRuntime ? 1 : 0)}; " +
            $"discovery={(report.DiscoveryLifecycle ? 1 : 0)}; " +
            $"deltaOnly={(report.RegionDeltaOnly ? 1 : 0)}; " +
            $"stress16={(report.Stress16Biomes ? 1 : 0)}; " +
            $"coldRestore={(report.ColdRestore ? 1 : 0)}; " +
            $"legacyFallback={(report.LegacyFallback ? 1 : 0)}; " +
            $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}; " +
            $"logWritten={(report.LogWritten ? 1 : 0)}; " +
            $"maxWriters={report.Diagnostics.MaximumConcurrentWriters}; " +
            $"integrity={report.Diagnostics.IntegrityResult}; " +
            $"elapsedMs={report.ElapsedMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)}; " +
            $"result={report.Result}");
        UpdateCombinedCatalogAndShipAcceptanceState();
    }
}
