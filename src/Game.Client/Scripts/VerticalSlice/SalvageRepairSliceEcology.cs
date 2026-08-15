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
        MultiMeshInstance3D Node,
        IReadOnlyList<EcologyFloraPlacement> Placements);

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
    private bool _ecologyCatalogOpen;
    private bool _ecologyFaunaTab;
    private int _ecologyCatalogSelection;
    private string _ecologyFeedback = "V scan • E harvest • O catalogue";
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
        if (_ecologyRoot is null || _ecologyPlan is null ||
            _ecologyRuntime is null || _ecologyCatalog is null)
        {
            return;
        }

        foreach (Node child in _ecologyRoot.GetChildren())
        {
            _ecologyRoot.RemoveChild(child);
            child.QueueFree();
        }
        _ecologyFaunaNodes.Clear();
        _promotedFloraNodes.Clear();
        _ecologyFloraGroups.Clear();

        MeshInstance3D habitat = new()
        {
            Name = "AquaticHabitat",
            Position = new Vector3(-25.5f, 0.04f, 25.5f),
            Mesh = new BoxMesh
            {
                Size = new Vector3(15.0f, 0.08f, 15.0f),
                Material = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.03f, 0.20f, 0.30f, 0.72f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    Roughness = 0.18f
                }
            }
        };
        _ecologyRoot.AddChild(habitat);

        foreach (IGrouping<string, EcologyFloraPlacement> group in EcologyPlan.Flora
            .Where(placement => !Ecology.IsFloraRemoved(placement.InstanceId))
            .GroupBy(placement => placement.FloraId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            EcologyFloraDefinition definition = EcologyCatalog.GetFlora(group.Key);
            EcologyFloraPlacement[] placements = group.ToArray();
            StandardMaterial3D material = new()
            {
                AlbedoColor = new Color(
                    (float)definition.ColorR,
                    (float)definition.ColorG,
                    (float)definition.ColorB,
                    1.0f),
                Roughness = 0.90f
            };
            MultiMesh multiMesh = new()
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = EcologyFloraSpecimenNode.CreateMesh(definition, material),
                InstanceCount = placements.Length
            };
            for (int index = 0; index < placements.Length; index++)
            {
                EcologyFloraPlacement placement = placements[index];
                float angle = Mathf.DegToRad((float)placement.RotationDegrees);
                float scale = (float)placement.Scale;
                Basis basis = Basis.Identity.Rotated(Vector3.Up, angle).Scaled(
                    Vector3.One * scale);
                multiMesh.SetInstanceTransform(
                    index,
                    new Transform3D(
                        basis,
                        new Vector3(
                            (float)placement.PositionX,
                            0.55f,
                            (float)placement.PositionZ)));
            }

            MultiMeshInstance3D instance = new()
            {
                Name = $"Flora_{GetShortContentId(group.Key)}",
                Multimesh = multiMesh
            };
            _ecologyRoot.AddChild(instance);
            _ecologyFloraGroups.Add(new EcologyMultiMeshGroup(instance, placements));
        }

        foreach (EcologyFaunaSpawn spawn in EcologyPlan.ActiveFauna)
        {
            EcologyFaunaNode faunaNode = new();
            faunaNode.Configure(EcologyCatalog.GetFauna(spawn.FaunaId), spawn, _player);
            faunaNode.Observed += OnEcologyFaunaObserved;
            _ecologyRoot.AddChild(faunaNode);
            _ecologyFaunaNodes.Add(faunaNode);
        }

        PromoteNearbyFlora(force: true);
        GD.Print(
            "TASK-116 ecology scene binding PASS: " +
            $"floraInstances={EcologyPlan.Flora.Count - Ecology.RemovedFloraCount}; " +
            $"multiMeshGroups={_ecologyFloraGroups.Count}; " +
            $"activeFauna={_ecologyFaunaNodes.Count}; " +
            $"simplifiedFauna={EcologyPlan.SimplifiedFauna.Count}; " +
            "promotion=proximity; persistence=seed+deltas.");
    }

    private void UpdateEcology(double delta)
    {
        if (_ecologyRuntime is null || _ecologyPlan is null ||
            _stageOneVoyageRuntime?.Piloted == true)
        {
            return;
        }

        Ecology.TickSimplified(delta);
        PromoteNearbyFlora(force: false);
    }

    private void PromoteNearbyFlora(bool force)
    {
        if (_player is null || _ecologyRoot is null ||
            _ecologyPlan is null || _ecologyRuntime is null)
        {
            return;
        }

        Vector3 observer = _player.GlobalPosition;
        EcologyFloraPlacement[] desired = EcologyPlan.Flora
            .Where(placement => !Ecology.IsFloraRemoved(placement.InstanceId))
            .Select(placement => new
            {
                Placement = placement,
                Distance = observer.DistanceTo(new Vector3(
                    (float)placement.PositionX,
                    0.55f,
                    (float)placement.PositionZ))
            })
            .Where(item => item.Distance <= 5.0f)
            .OrderBy(item => item.Distance)
            .Take(8)
            .Select(item => item.Placement)
            .ToArray();
        HashSet<string> desiredIds = desired
            .Select(placement => placement.InstanceId)
            .ToHashSet(StringComparer.Ordinal);

        foreach ((string instanceId, EcologyFloraSpecimenNode node) in
            _promotedFloraNodes.ToArray())
        {
            float distance = node.GlobalPosition.DistanceTo(observer);
            if (!desiredIds.Contains(instanceId) && (force || distance > 7.0f))
            {
                node.QueueFree();
                _promotedFloraNodes.Remove(instanceId);
            }
        }

        foreach (EcologyFloraPlacement placement in desired)
        {
            if (_promotedFloraNodes.ContainsKey(placement.InstanceId))
            {
                continue;
            }

            EcologyFloraSpecimenNode specimen = new();
            // The MultiMesh remains the visual representation. Promotion adds
            // only an interactive physics proxy, avoiding duplicate geometry
            // and z-fighting while keeping the plant harvestable.
            specimen.Configure(
                EcologyCatalog.GetFlora(placement.FloraId),
                placement,
                renderMesh: false);
            specimen.HarvestRequested += OnEcologyFloraHarvestRequested;
            _ecologyRoot.AddChild(specimen);
            _promotedFloraNodes[placement.InstanceId] = specimen;
        }
    }

    private bool HandleEcologyInput(Key physical, Key logical)
    {
        if (_ecologyCatalogOpen)
        {
            if (Matches(physical, logical, Key.Escape) ||
                Matches(physical, logical, Key.O))
            {
                CloseEcologyCatalog("ecology catalogue closed");
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
        _status = "ecology catalogue opened";
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
        if (!_ecologyCatalogOpen || _ecologyCatalogLabel is null ||
            _ecologyRuntime is null || _ecologyCatalog is null)
        {
            return;
        }

        string[] lines;
        if (_ecologyFaunaTab)
        {
            EcologyFaunaDefinition[] definitions = EcologyCatalog.Fauna.Values
                .OrderBy(definition => definition.FaunaId, StringComparer.Ordinal)
                .ToArray();
            _ecologyCatalogSelection = Math.Clamp(
                _ecologyCatalogSelection,
                0,
                Math.Max(0, definitions.Length - 1));
            lines = definitions.Select((definition, index) =>
            {
                bool known = Ecology.DiscoveredFaunaIds.Contains(
                    definition.FaunaId,
                    StringComparer.Ordinal);
                string marker = index == _ecologyCatalogSelection ? ">" : " ";
                return known
                    ? $"{marker} {definition.DisplayNameEn,-24} {definition.MovementMode,-8} {definition.BodyPlan,-10} diet={definition.Diet,-9} aggression={definition.Aggression:0.00}"
                    : $"{marker} UNKNOWN FAUNA SIGNAL";
            }).ToArray();
        }
        else
        {
            EcologyFloraDefinition[] definitions = EcologyCatalog.Flora.Values
                .OrderBy(definition => definition.FloraId, StringComparer.Ordinal)
                .ToArray();
            _ecologyCatalogSelection = Math.Clamp(
                _ecologyCatalogSelection,
                0,
                Math.Max(0, definitions.Length - 1));
            lines = definitions.Select((definition, index) =>
            {
                bool known = Ecology.DiscoveredFloraIds.Contains(
                    definition.FloraId,
                    StringComparer.Ordinal);
                string marker = index == _ecologyCatalogSelection ? ">" : " ";
                return known
                    ? $"{marker} {definition.DisplayNameEn,-24} {definition.Shape,-8} harvest={GetShortContentId(definition.HarvestDefinitionId),-18} hazard={definition.Hazard}"
                    : $"{marker} UNKNOWN FLORA SIGNAL";
            }).ToArray();
        }

        string body = string.Join("\n", lines.Take(26));
        _ecologyCatalogLabel.Text =
            "PLANETARY ECOLOGY CATALOGUE  [O close]  [Tab flora/fauna]  [V scan]\n" +
            $"Region {EcologyCatalog.RegionKey} • seed={EcologyCatalog.WorldSeed} • " +
            $"flora={Ecology.DiscoveredFloraCount}/{EcologyCatalog.Flora.Count} • " +
            $"fauna={Ecology.DiscoveredFaunaCount}/{EcologyCatalog.Fauna.Count} • " +
            $"harvested={Ecology.RemovedFloraCount} • points={Ecology.DiscoveryPoints}\n" +
            $"TAB: {(_ecologyFaunaTab ? "FAUNA" : "FLORA")} • {_ecologyFeedback}\n\n" +
            body;
    }

    private void PulseEcologyScanner()
    {
        if (_player is null || _ecologyRuntime is null || _ecologyPlan is null)
        {
            return;
        }

        const float scanRange = 16.0f;
        Vector3 observer = _player.GlobalPosition;
        EcologyFaunaNode? fauna = _ecologyFaunaNodes
            .Where(node => node.Visible)
            .OrderBy(node => node.GlobalPosition.DistanceSquaredTo(observer))
            .FirstOrDefault();
        float faunaDistance = fauna is null
            ? float.PositiveInfinity
            : fauna.GlobalPosition.DistanceTo(observer);
        EcologyFloraPlacement? flora = EcologyPlan.Flora
            .Where(placement => !Ecology.IsFloraRemoved(placement.InstanceId))
            .OrderBy(placement => observer.DistanceSquaredTo(new Vector3(
                (float)placement.PositionX,
                0.55f,
                (float)placement.PositionZ)))
            .FirstOrDefault();
        float floraDistance = flora is null
            ? float.PositiveInfinity
            : observer.DistanceTo(new Vector3(
                (float)flora.PositionX,
                0.55f,
                (float)flora.PositionZ));

        if (Math.Min(faunaDistance, floraDistance) > scanRange)
        {
            _ecologyFeedback = $"no ecology signal within {scanRange:0} m";
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
        foreach (EcologyMultiMeshGroup group in _ecologyFloraGroups)
        {
            if (group.Node.GetParent() is Node parent)
            {
                parent.RemoveChild(group.Node);
            }
            group.Node.QueueFree();
        }
        _ecologyFloraGroups.Clear();
        if (_ecologyRoot is null || _ecologyRuntime is null || _ecologyPlan is null)
        {
            return;
        }

        foreach (IGrouping<string, EcologyFloraPlacement> group in EcologyPlan.Flora
            .Where(placement => !Ecology.IsFloraRemoved(placement.InstanceId))
            .GroupBy(placement => placement.FloraId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            EcologyFloraDefinition definition = EcologyCatalog.GetFlora(group.Key);
            EcologyFloraPlacement[] placements = group.ToArray();
            StandardMaterial3D material = new()
            {
                AlbedoColor = new Color(
                    (float)definition.ColorR,
                    (float)definition.ColorG,
                    (float)definition.ColorB,
                    1.0f),
                Roughness = 0.90f
            };
            MultiMesh multiMesh = new()
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = EcologyFloraSpecimenNode.CreateMesh(definition, material),
                InstanceCount = placements.Length
            };
            for (int index = 0; index < placements.Length; index++)
            {
                EcologyFloraPlacement placement = placements[index];
                Basis basis = Basis.Identity.Rotated(
                    Vector3.Up,
                    Mathf.DegToRad((float)placement.RotationDegrees)).Scaled(
                    Vector3.One * (float)placement.Scale);
                multiMesh.SetInstanceTransform(
                    index,
                    new Transform3D(
                        basis,
                        new Vector3(
                            (float)placement.PositionX,
                            0.55f,
                            (float)placement.PositionZ)));
            }
            MultiMeshInstance3D instance = new()
            {
                Name = $"Flora_{GetShortContentId(group.Key)}",
                Multimesh = multiMesh
            };
            _ecologyRoot.AddChild(instance);
            _ecologyFloraGroups.Add(new EcologyMultiMeshGroup(instance, placements));
        }
    }

    private string BuildEcologyHudLine()
    {
        if (_ecologyRuntime is null || _ecologyPlan is null ||
            _ecologyCatalog is null)
        {
            return "Ecology: unavailable";
        }
        return
            $"Ecology: biomes={EcologyCatalog.Biomes.Count} • " +
            $"flora={Ecology.DiscoveredFloraCount}/{EcologyCatalog.Flora.Count} " +
            $"({EcologyPlan.Flora.Count - Ecology.RemovedFloraCount} instanced) • " +
            $"fauna={Ecology.DiscoveredFaunaCount}/{EcologyCatalog.Fauna.Count} • " +
            $"active/simplified={EcologyPlan.ActiveFauna.Count}/{EcologyPlan.SimplifiedFauna.Count} • " +
            $"points={Ecology.DiscoveryPoints} • V scan • O catalogue";
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
