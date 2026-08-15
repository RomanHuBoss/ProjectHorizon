using System;
using System.Collections.Generic;
using System.Linq;

public enum ProceduralQuestStatus
{
    Offered = 0,
    Accepted = 1,
    ReturnRequired = 2,
    ReadyToClaim = 3,
    Completed = 4
}

public sealed record ProceduralQuestCondition(
    string ConditionType,
    string TargetDefinitionId,
    int RequiredQuantity);

public sealed record ProceduralQuestAction(
    string ActionType,
    string Value);

public sealed record ProceduralQuestReward(
    int Credits,
    int Reputation,
    string FactionId);

public sealed record ProceduralQuestNode(
    string NodeId,
    string Stage,
    IReadOnlyList<ProceduralQuestCondition> Conditions,
    IReadOnlyList<ProceduralQuestAction> Actions,
    string NextNodeId);

public sealed record ProceduralQuestDefinition(
    string QuestId,
    string FactionId,
    string GiverNpcId,
    ProceduralQuestObjectiveType ObjectiveType,
    string TargetDefinitionId,
    int RequiredQuantity,
    int RewardCredits,
    int ReputationReward,
    bool RequiresReturnToGiver,
    IReadOnlyList<ProceduralQuestNode>? Nodes = null,
    ProceduralQuestReward? Reward = null);

public sealed record ProceduralQuestInstance(
    string QuestId,
    ProceduralQuestStatus Status,
    int Progress);

public sealed record ProceduralQuestView(
    ProceduralQuestDefinition Definition,
    ProceduralQuestStatus Status,
    int Progress);

public sealed record ProceduralQuestCapabilities(
    IReadOnlyList<string> LocationIds,
    IReadOnlyList<string> ScannableObjectIds,
    IReadOnlyList<string> SpeciesIds,
    IReadOnlyList<string> ResourceIds,
    IReadOnlyList<string> CraftableItemIds,
    IReadOnlyList<string> DeliverableItemIds,
    IReadOnlyList<string> RepairObjectIds,
    IReadOnlyList<string> DefeatTargetIds,
    IReadOnlyList<string> ProtectTargetIds,
    IReadOnlyList<string> BuildModuleIds,
    IReadOnlyList<string> TradableItemIds,
    IReadOnlyList<string> SignalIds,
    IReadOnlyList<string> PlanetIds,
    IReadOnlyList<string> SystemIds,
    IReadOnlyList<string> NpcIds,
    bool LandingAvailable,
    bool InventoryCapacityAvailable,
    int EquipmentTier)
{
    public IReadOnlyList<string> GetTargets(ProceduralQuestObjectiveType type) =>
        type switch
        {
            ProceduralQuestObjectiveType.VisitLocation => LocationIds,
            ProceduralQuestObjectiveType.ScanObject => ScannableObjectIds,
            ProceduralQuestObjectiveType.ScanSpecies => SpeciesIds,
            ProceduralQuestObjectiveType.CollectResource => ResourceIds,
            ProceduralQuestObjectiveType.CraftItem => CraftableItemIds,
            ProceduralQuestObjectiveType.DeliverItem => DeliverableItemIds,
            ProceduralQuestObjectiveType.RepairObject => RepairObjectIds,
            ProceduralQuestObjectiveType.DefeatTarget => DefeatTargetIds,
            ProceduralQuestObjectiveType.ProtectTarget => ProtectTargetIds,
            ProceduralQuestObjectiveType.BuildModule => BuildModuleIds,
            ProceduralQuestObjectiveType.TradeItem => TradableItemIds,
            ProceduralQuestObjectiveType.FindSignal => SignalIds,
            ProceduralQuestObjectiveType.ExplorePlanet => PlanetIds,
            ProceduralQuestObjectiveType.ExploreSystem => SystemIds,
            ProceduralQuestObjectiveType.ReturnToNpc => NpcIds,
            _ => Array.Empty<string>()
        };

    public bool Supports(ProceduralQuestObjectiveType type)
    {
        if (!LandingAvailable &&
            type is ProceduralQuestObjectiveType.VisitLocation or
                ProceduralQuestObjectiveType.ExplorePlanet)
        {
            return false;
        }
        if (!InventoryCapacityAvailable &&
            type is ProceduralQuestObjectiveType.CollectResource or
                ProceduralQuestObjectiveType.CraftItem or
                ProceduralQuestObjectiveType.DeliverItem or
                ProceduralQuestObjectiveType.TradeItem)
        {
            return false;
        }
        if (EquipmentTier < 1 &&
            type is ProceduralQuestObjectiveType.RepairObject or
                ProceduralQuestObjectiveType.DefeatTarget or
                ProceduralQuestObjectiveType.ProtectTarget)
        {
            return false;
        }
        return GetTargets(type).Count > 0;
    }
}

public static class ProceduralQuestGenerator
{
    public const int BoardRevision = 1;

    public static IReadOnlyList<ProceduralQuestDefinition> Generate(
        ProceduralQuestCatalog catalog,
        ProceduralQuestCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(capabilities);
        ProceduralQuestObjectiveType[] feasible =
            Enum.GetValues<ProceduralQuestObjectiveType>()
                .Where(capabilities.Supports)
                .ToArray();
        if (feasible.Length == 0)
        {
            throw new InvalidOperationException(
                "No feasible procedural quest objective types are available.");
        }

        List<ProceduralQuestObjectiveType> plan = new();
        foreach (ProceduralQuestObjectiveType type in
            Enum.GetValues<ProceduralQuestObjectiveType>())
        {
            if (capabilities.Supports(type) && plan.Count < catalog.BoardSize)
            {
                plan.Add(type);
            }
        }
        for (int index = plan.Count; index < catalog.BoardSize; index++)
        {
            plan.Add(ChooseWeightedType(catalog, feasible, index));
        }

        List<ProceduralQuestDefinition> result = new(catalog.BoardSize);
        for (int index = 0; index < catalog.BoardSize; index++)
        {
            ProceduralQuestObjectiveType type = plan[index];
            ProceduralQuestObjectiveProfile profile = catalog.GetProfile(type);
            IReadOnlyList<string> targets = capabilities.GetTargets(type);
            int targetIndex = PositiveMod(
                StableHash(catalog.WorldSeed, index, (int)type, 17),
                targets.Count);
            int factionIndex = PositiveMod(
                StableHash(catalog.WorldSeed, index, (int)type, 29),
                profile.Factions.Count);
            string target = targets[targetIndex];
            string faction = profile.Factions[factionIndex];
            string giver = type == ProceduralQuestObjectiveType.ReturnToNpc
                ? target
                : capabilities.NpcIds.Count > 0
                    ? capabilities.NpcIds[PositiveMod(
                        StableHash(catalog.WorldSeed, index, 53, 7),
                        capabilities.NpcIds.Count)]
                    : "npc.trader.ilia_voss";
            int quantity = type switch
            {
                ProceduralQuestObjectiveType.CollectResource or
                ProceduralQuestObjectiveType.CraftItem or
                ProceduralQuestObjectiveType.DeliverItem or
                ProceduralQuestObjectiveType.TradeItem =>
                    1 + PositiveMod(
                        StableHash(catalog.WorldSeed, index, 71, 3), 3),
                _ => 1
            };
            int difficulty = 1 + PositiveMod(
                StableHash(catalog.WorldSeed, index, 89, 11), 4);
            int reward = profile.BaseRewardCredits + difficulty * 35 +
                Math.Max(0, quantity - 1) * 40;
            int reputation = profile.ReputationReward + difficulty / 2;
            bool requiresReturn = type != ProceduralQuestObjectiveType.ReturnToNpc;
            string questId =
                $"quest.proc.{index + 1:00}.{type.ToString().ToLowerInvariant()}";
            result.Add(new ProceduralQuestDefinition(
                questId,
                faction,
                giver,
                type,
                target,
                quantity,
                reward,
                reputation,
                requiresReturn,
                BuildStateGraph(
                    questId,
                    type,
                    target,
                    quantity,
                    giver,
                    requiresReturn),
                new ProceduralQuestReward(reward, reputation, faction)));
        }

        if (result.Select(quest => quest.QuestId)
                .Distinct(StringComparer.Ordinal).Count() != result.Count)
        {
            throw new InvalidOperationException(
                "Procedural quest generator produced duplicate IDs.");
        }
        return result;
    }

    public static bool ValidateFeasibility(
        ProceduralQuestDefinition quest,
        ProceduralQuestCapabilities capabilities,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(quest);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (!capabilities.Supports(quest.ObjectiveType))
        {
            reason = $"capability missing for {quest.ObjectiveType}";
            return false;
        }
        if (!capabilities.GetTargets(quest.ObjectiveType).Contains(
                quest.TargetDefinitionId,
                StringComparer.Ordinal))
        {
            reason = $"target {quest.TargetDefinitionId} is unavailable";
            return false;
        }
        if (quest.RequiredQuantity <= 0 || quest.RewardCredits <= 0 ||
            quest.ReputationReward < 0 ||
            string.IsNullOrWhiteSpace(quest.GiverNpcId) ||
            !capabilities.NpcIds.Contains(quest.GiverNpcId, StringComparer.Ordinal))
        {
            reason = "invalid quantity, reward or giver";
            return false;
        }
        if (quest.Nodes is null || quest.Nodes.Count is < 2 or > 3 ||
            quest.Reward is null || quest.Reward.Credits != quest.RewardCredits ||
            quest.Reward.Reputation != quest.ReputationReward ||
            !string.Equals(
                quest.Reward.FactionId,
                quest.FactionId,
                StringComparison.Ordinal) ||
            !ValidateLinearStateGraph(quest.Nodes))
        {
            reason = "invalid quest state graph or reward";
            return false;
        }
        reason = "feasible";
        return true;
    }

    private static bool ValidateLinearStateGraph(
        IReadOnlyList<ProceduralQuestNode> nodes)
    {
        if (nodes.Count == 0 ||
            nodes.Select(node => node.NodeId)
                .Distinct(StringComparer.Ordinal).Count() != nodes.Count)
        {
            return false;
        }
        Dictionary<string, ProceduralQuestNode> byId = nodes.ToDictionary(
            node => node.NodeId,
            StringComparer.Ordinal);
        HashSet<string> visited = new(StringComparer.Ordinal);
        string current = nodes[0].NodeId;
        while (!string.IsNullOrEmpty(current))
        {
            if (!byId.TryGetValue(current, out ProceduralQuestNode? node) ||
                !visited.Add(current))
            {
                return false;
            }
            if (node.Conditions is null || node.Actions is null)
            {
                return false;
            }
            current = node.NextNodeId;
        }
        return visited.Count == nodes.Count;
    }

    private static IReadOnlyList<ProceduralQuestNode> BuildStateGraph(
        string questId,
        ProceduralQuestObjectiveType objectiveType,
        string targetDefinitionId,
        int requiredQuantity,
        string giverNpcId,
        bool requiresReturn)
    {
        string objectiveNode = $"{questId}.objective";
        string returnNode = $"{questId}.return";
        string claimNode = $"{questId}.claim";
        List<ProceduralQuestNode> nodes = new()
        {
            new ProceduralQuestNode(
                objectiveNode,
                "Objective",
                new[]
                {
                    new ProceduralQuestCondition(
                        objectiveType.ToString(),
                        targetDefinitionId,
                        requiredQuantity)
                },
                new[]
                {
                    new ProceduralQuestAction(
                        "Advance",
                        requiresReturn ? returnNode : claimNode)
                },
                requiresReturn ? returnNode : claimNode)
        };
        if (requiresReturn)
        {
            nodes.Add(new ProceduralQuestNode(
                returnNode,
                "Return",
                new[]
                {
                    new ProceduralQuestCondition(
                        ProceduralQuestObjectiveType.ReturnToNpc.ToString(),
                        giverNpcId,
                        1)
                },
                new[]
                {
                    new ProceduralQuestAction("Advance", claimNode)
                },
                claimNode));
        }
        nodes.Add(new ProceduralQuestNode(
            claimNode,
            "Claim",
            Array.Empty<ProceduralQuestCondition>(),
            new[]
            {
                new ProceduralQuestAction("GrantReward", questId),
                new ProceduralQuestAction("Complete", questId)
            },
            string.Empty));
        return nodes;
    }

    private static ProceduralQuestObjectiveType ChooseWeightedType(
        ProceduralQuestCatalog catalog,
        IReadOnlyList<ProceduralQuestObjectiveType> feasible,
        int index)
    {
        int total = feasible.Sum(type => catalog.GetProfile(type).Weight);
        int roll = PositiveMod(
            StableHash(catalog.WorldSeed, index, 101, 31), total);
        foreach (ProceduralQuestObjectiveType type in feasible)
        {
            roll -= catalog.GetProfile(type).Weight;
            if (roll < 0)
            {
                return type;
            }
        }
        return feasible[^1];
    }

    private static int StableHash(long seed, int a, int b, int c)
    {
        unchecked
        {
            ulong value = (ulong)seed;
            value ^= (uint)a + 0x9E3779B9u + (value << 6) + (value >> 2);
            value ^= (uint)b + 0x85EBCA6Bu + (value << 6) + (value >> 2);
            value ^= (uint)c + 0xC2B2AE35u + (value << 6) + (value >> 2);
            value ^= value >> 33;
            value *= 0xff51afd7ed558ccdUL;
            value ^= value >> 33;
            return (int)(value & 0x7FFFFFFF);
        }
    }

    private static int PositiveMod(int value, int divisor) =>
        divisor <= 0 ? 0 : (value % divisor + divisor) % divisor;
}

public sealed class ProceduralQuestRuntime
{
    private sealed class MutableState
    {
        public MutableState(
            ProceduralQuestDefinition definition,
            ProceduralQuestStatus status,
            int progress)
        {
            Definition = definition;
            Status = status;
            Progress = progress;
        }
        public ProceduralQuestDefinition Definition { get; }
        public ProceduralQuestStatus Status { get; set; }
        public int Progress { get; set; }
    }

    private readonly ProceduralQuestCatalog _catalog;
    private readonly Dictionary<string, MutableState> _states;

    public ProceduralQuestRuntime(
        ProceduralQuestCatalog catalog,
        ProceduralQuestCapabilities capabilities,
        ProceduralQuestSaveData? saveData)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(capabilities);
        _catalog = catalog;
        Capabilities = capabilities;
        Board = ProceduralQuestGenerator.Generate(catalog, capabilities);
        _states = Board.ToDictionary(
            quest => quest.QuestId,
            quest => new MutableState(
                quest,
                ProceduralQuestStatus.Offered,
                progress: 0),
            StringComparer.Ordinal);
        Restore(saveData);
    }

    public ProceduralQuestCapabilities Capabilities { get; }
    public IReadOnlyList<ProceduralQuestDefinition> Board { get; }
    public int AcceptedCount => _states.Values.Count(state =>
        state.Status is ProceduralQuestStatus.Accepted or
            ProceduralQuestStatus.ReturnRequired or
            ProceduralQuestStatus.ReadyToClaim);
    public int CompletedCount => _states.Values.Count(state =>
        state.Status == ProceduralQuestStatus.Completed);
    public int ReadyCount => _states.Values.Count(state =>
        state.Status == ProceduralQuestStatus.ReadyToClaim);

    public IReadOnlyList<ProceduralQuestInstance> Instances => _states.Values
        .OrderBy(state => state.Definition.QuestId, StringComparer.Ordinal)
        .Select(state => new ProceduralQuestInstance(
            state.Definition.QuestId,
            state.Status,
            state.Progress))
        .ToArray();

    public int GetFactionReputation(string factionId) => _states.Values
        .Where(state => state.Status == ProceduralQuestStatus.Completed &&
            string.Equals(
                state.Definition.FactionId,
                factionId,
                StringComparison.Ordinal))
        .Sum(state => state.Definition.ReputationReward);

    public IReadOnlyList<ProceduralQuestView> Views => _states.Values
        .OrderBy(state => state.Definition.QuestId, StringComparer.Ordinal)
        .Select(state => new ProceduralQuestView(
            state.Definition,
            state.Status,
            state.Progress))
        .ToArray();

    public bool TryAccept(string questId, out string result)
    {
        MutableState state = Get(questId);
        if (state.Status != ProceduralQuestStatus.Offered)
        {
            result = $"{questId} is not offered";
            return false;
        }
        if (AcceptedCount >= _catalog.MaximumActive)
        {
            result = $"active quest limit {_catalog.MaximumActive} reached";
            return false;
        }
        state.Status = ProceduralQuestStatus.Accepted;
        result = $"accepted {questId}";
        return true;
    }

    public int RecordObjective(
        ProceduralQuestObjectiveType objectiveType,
        string targetDefinitionId,
        int quantity,
        out IReadOnlyList<string> changedQuestIds)
    {
        if (quantity <= 0 || string.IsNullOrWhiteSpace(targetDefinitionId))
        {
            changedQuestIds = Array.Empty<string>();
            return 0;
        }
        List<string> changed = new();
        foreach (MutableState state in _states.Values)
        {
            if (state.Status != ProceduralQuestStatus.Accepted ||
                state.Definition.ObjectiveType != objectiveType ||
                !string.Equals(
                    state.Definition.TargetDefinitionId,
                    targetDefinitionId,
                    StringComparison.Ordinal))
            {
                continue;
            }
            int before = state.Progress;
            state.Progress = Math.Min(
                state.Definition.RequiredQuantity,
                state.Progress + quantity);
            if (state.Progress != before)
            {
                if (state.Progress >= state.Definition.RequiredQuantity)
                {
                    state.Status = state.Definition.RequiresReturnToGiver
                        ? ProceduralQuestStatus.ReturnRequired
                        : ProceduralQuestStatus.ReadyToClaim;
                }
                changed.Add(state.Definition.QuestId);
            }
        }
        changedQuestIds = changed;
        return changed.Count;
    }

    public int RecordReturnToNpc(
        string npcId,
        out IReadOnlyList<string> changedQuestIds)
    {
        List<string> changed = new();
        foreach (MutableState state in _states.Values)
        {
            if (!string.Equals(
                    state.Definition.GiverNpcId,
                    npcId,
                    StringComparison.Ordinal))
            {
                continue;
            }
            if (state.Status == ProceduralQuestStatus.Accepted &&
                state.Definition.ObjectiveType ==
                    ProceduralQuestObjectiveType.ReturnToNpc)
            {
                state.Progress = state.Definition.RequiredQuantity;
                state.Status = ProceduralQuestStatus.ReadyToClaim;
                changed.Add(state.Definition.QuestId);
            }
            else if (state.Status == ProceduralQuestStatus.ReturnRequired)
            {
                state.Status = ProceduralQuestStatus.ReadyToClaim;
                changed.Add(state.Definition.QuestId);
            }
        }
        changedQuestIds = changed;
        return changed.Count;
    }

    public bool TryClaim(
        string questId,
        out int rewardCredits,
        out int reputationReward,
        out string factionId,
        out string result)
    {
        MutableState state = Get(questId);
        rewardCredits = 0;
        reputationReward = 0;
        factionId = state.Definition.FactionId;
        if (state.Status != ProceduralQuestStatus.ReadyToClaim)
        {
            result = $"{questId} is not ready to claim";
            return false;
        }
        state.Status = ProceduralQuestStatus.Completed;
        rewardCredits = state.Definition.RewardCredits;
        reputationReward = state.Definition.ReputationReward;
        result = $"completed {questId}";
        return true;
    }

    public ProceduralQuestSaveData CreateSaveData() =>
        new(
            _catalog.WorldSeed,
            ProceduralQuestGenerator.BoardRevision,
            _states.Values
                .Where(state => state.Status != ProceduralQuestStatus.Offered ||
                    state.Progress != 0)
                .OrderBy(state => state.Definition.QuestId, StringComparer.Ordinal)
                .Select(state => new ProceduralQuestStateSaveData(
                    state.Definition.QuestId,
                    state.Status,
                    state.Progress))
                .ToArray());

    private void Restore(ProceduralQuestSaveData? saveData)
    {
        if (saveData is null)
        {
            return;
        }
        if (saveData.WorldSeed != _catalog.WorldSeed ||
            saveData.BoardRevision != ProceduralQuestGenerator.BoardRevision ||
            saveData.States is null)
        {
            throw new InvalidOperationException(
                "Procedural quest save data is incompatible with the current board.");
        }
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (ProceduralQuestStateSaveData saved in saveData.States)
        {
            if (!_states.TryGetValue(saved.QuestId, out MutableState? state) ||
                !ids.Add(saved.QuestId) ||
                !Enum.IsDefined(saved.Status) ||
                saved.Progress < 0 ||
                saved.Progress > state.Definition.RequiredQuantity)
            {
                throw new InvalidOperationException(
                    $"Invalid procedural quest state {saved.QuestId}.");
            }
            bool completeProgress = saved.Progress >=
                state.Definition.RequiredQuantity;
            if ((saved.Status == ProceduralQuestStatus.Accepted && completeProgress) ||
                (saved.Status is ProceduralQuestStatus.ReturnRequired or
                    ProceduralQuestStatus.ReadyToClaim or
                    ProceduralQuestStatus.Completed) && !completeProgress)
            {
                throw new InvalidOperationException(
                    $"Procedural quest {saved.QuestId} has inconsistent progress/status.");
            }
            state.Status = saved.Status;
            state.Progress = saved.Progress;
        }
        if (AcceptedCount > _catalog.MaximumActive)
        {
            throw new InvalidOperationException(
                "Procedural quest save exceeds the active quest limit.");
        }
    }

    private MutableState Get(string questId) =>
        _states.TryGetValue(questId, out MutableState? value)
            ? value
            : throw new KeyNotFoundException($"Unknown procedural quest {questId}.");
}

public static class ProceduralQuestCapabilityFactory
{
    public static ProceduralQuestCapabilities Create(
        GameContentCatalog content,
        StationServicesCatalog stationServices,
        BaseConstructionCatalog baseConstruction,
        PlanetaryPoiCatalog poiCatalog,
        EcologyCatalog ecologyCatalog,
        GalaxyNavigationRuntime galaxyNavigation,
        bool includeCombatTargets)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(stationServices);
        ArgumentNullException.ThrowIfNull(baseConstruction);
        ArgumentNullException.ThrowIfNull(poiCatalog);
        ArgumentNullException.ThrowIfNull(ecologyCatalog);
        ArgumentNullException.ThrowIfNull(galaxyNavigation);

        string[] poiIds = poiCatalog.Definitions.Keys
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        string[] species = ecologyCatalog.Flora.Keys
            .Concat(ecologyCatalog.Fauna.Keys)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        string[] resources = content.Resources.Keys
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        string[] craftable = content.Recipes.Values
            .Where(recipe => recipe.RuntimeEnabled &&
                string.Equals(
                    recipe.Application.Type,
                    "StoreOutputs",
                    StringComparison.Ordinal))
            .SelectMany(recipe => recipe.Outputs)
            .Select(output => output.DefinitionId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        string[] attainableItems = resources
            .Concat(craftable)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        GalaxySystemDefinition[] nearby = galaxyNavigation
            .GetNearbySystems(radius: 1, maximumCount: 26)
            .Append(galaxyNavigation.CurrentSystem)
            .GroupBy(system => system.SystemId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(system => system.SystemId, StringComparer.Ordinal)
            .ToArray();
        string[] planets = nearby
            .Where(system => system.Planets.Count > 0)
            .Select(system => system.Planets[0].PlanetId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        string[] systems = nearby
            .Select(system => system.SystemId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        string[] npcs = stationServices.Npcs.Keys
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return new ProceduralQuestCapabilities(
            poiIds,
            poiIds,
            species,
            resources,
            craftable,
            attainableItems,
            new[] { "object.ship.starter" },
            includeCombatTargets
                ? new[] { "target.raider_drone", "target.hostile_scout" }
                : Array.Empty<string>(),
            includeCombatTargets
                ? new[] { "target.frontier_relay", "target.science_probe" }
                : Array.Empty<string>(),
            baseConstruction.Modules.Keys
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            attainableItems,
            poiIds,
            planets,
            systems,
            npcs,
            LandingAvailable: true,
            InventoryCapacityAvailable: true,
            EquipmentTier: 2);
    }
}
