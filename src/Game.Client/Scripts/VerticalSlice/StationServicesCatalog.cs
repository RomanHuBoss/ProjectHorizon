using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

public enum StationServiceObjectiveType
{
    CollectResource = 0,
    CraftItem = 1,
    TradeItem = 2
}

public sealed record FactionServiceDefinition(
    string FactionId,
    string LocalizationKey,
    string EconomyType,
    IReadOnlyList<string> PreferredTags,
    IReadOnlyList<string> QuestTypes,
    string VisualStyle,
    IReadOnlyList<string> NamePoolKeys,
    IReadOnlyDictionary<string, int> Relations);

public sealed record MarketServiceDefinition(
    string MarketId,
    string LocalizationKey,
    string EconomyType,
    string FactionId,
    double SystemEconomyModifier,
    double FactionModifier,
    double BuyMarkup,
    double SellMarkdown,
    int DailySeed,
    int InitialStockPerItem,
    int TargetStockPerItem,
    int PlayerStartingCredits,
    int MerchantStartingCredits);

public sealed record DialogueOptionServiceDefinition(
    string OptionId,
    string LocalizationKey,
    string Action,
    int MinimumReputation,
    int ReputationDelta);

public sealed record DialogueServiceDefinition(
    string DialogueId,
    string LocalizationKey,
    string GreetingKey,
    string FarewellKey,
    IReadOnlyList<DialogueOptionServiceDefinition> Options);

public sealed record NpcServiceDefinition(
    string NpcId,
    string LocalizationKey,
    string NpcType,
    string FactionId,
    string MarketId,
    string DialogueId);

public sealed record QuestNodeServiceDefinition(
    string NodeId,
    StationServiceObjectiveType ObjectiveType,
    string TargetDefinitionId,
    int RequiredQuantity,
    IReadOnlyList<string> NextNodeIds);

public sealed record QuestServiceDefinition(
    string QuestId,
    string LocalizationKey,
    string GiverNpcId,
    string StartNodeId,
    IReadOnlyList<QuestNodeServiceDefinition> Nodes,
    int RewardCredits,
    int ReputationReward)
{
    public QuestNodeServiceDefinition GetNode(string nodeId)
    {
        return Nodes.FirstOrDefault(node => string.Equals(
                node.NodeId,
                nodeId,
                StringComparison.Ordinal)) ??
            throw new KeyNotFoundException(
                $"Quest {QuestId} has no node {nodeId}.");
    }

    public QuestNodeServiceDefinition StartNode => GetNode(StartNodeId);

    public StationServiceObjectiveType ObjectiveType =>
        StartNode.ObjectiveType;

    public string TargetDefinitionId => StartNode.TargetDefinitionId;

    public int RequiredQuantity => StartNode.RequiredQuantity;
}

public sealed class StationServicesCatalog
{
    public const int CurrentSchemaVersion = 1;

    public static readonly IReadOnlyList<string> RequiredEconomyTypes = new[]
    {
        "Mining",
        "Industrial",
        "Technology",
        "Trading",
        "Scientific",
        "Military"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict
    };

    private readonly string[] _economyTypes;
    private readonly Dictionary<string, FactionServiceDefinition> _factions;
    private readonly Dictionary<string, MarketServiceDefinition> _markets;
    private readonly Dictionary<string, DialogueServiceDefinition> _dialogues;
    private readonly Dictionary<string, NpcServiceDefinition> _npcs;
    private readonly Dictionary<string, QuestServiceDefinition> _quests;

    private StationServicesCatalog(
        int schemaVersion,
        string[] economyTypes,
        Dictionary<string, FactionServiceDefinition> factions,
        Dictionary<string, MarketServiceDefinition> markets,
        Dictionary<string, DialogueServiceDefinition> dialogues,
        Dictionary<string, NpcServiceDefinition> npcs,
        Dictionary<string, QuestServiceDefinition> quests)
    {
        SchemaVersion = schemaVersion;
        _economyTypes = economyTypes;
        _factions = factions;
        _markets = markets;
        _dialogues = dialogues;
        _npcs = npcs;
        _quests = quests;
    }

    public int SchemaVersion { get; }

    public IReadOnlyList<string> EconomyTypes => _economyTypes;

    public IReadOnlyDictionary<string, FactionServiceDefinition> Factions =>
        _factions;

    public IReadOnlyDictionary<string, MarketServiceDefinition> Markets =>
        _markets;

    public IReadOnlyDictionary<string, DialogueServiceDefinition> Dialogues =>
        _dialogues;

    public IReadOnlyDictionary<string, NpcServiceDefinition> Npcs => _npcs;

    public IReadOnlyDictionary<string, QuestServiceDefinition> Quests => _quests;

    public static StationServicesCatalog LoadFromJson(
        string json,
        GameContentCatalog contentCatalog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(contentCatalog);
        StationServicesDocument document;
        try
        {
            document = JsonSerializer.Deserialize<StationServicesDocument>(
                json,
                JsonOptions) ?? throw new ContentValidationException(
                    "station_services.json deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw new ContentValidationException(
                $"station_services.json is invalid: {exception.Message}");
        }

        if (document.SchemaVersion != CurrentSchemaVersion)
        {
            throw new ContentValidationException(
                $"station_services.json schema {document.SchemaVersion} is not " +
                $"supported; expected {CurrentSchemaVersion}.");
        }

        string[] economyTypes = (document.EconomyTypes ?? Array.Empty<string>())
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, FactionServiceDefinition> factions = BuildDictionary(
            document.Factions,
            faction => faction.FactionId,
            "faction");
        Dictionary<string, MarketServiceDefinition> markets = BuildDictionary(
            document.Markets,
            market => market.MarketId,
            "market");
        Dictionary<string, DialogueServiceDefinition> dialogues = BuildDictionary(
            document.Dialogues,
            dialogue => dialogue.DialogueId,
            "dialogue");
        Dictionary<string, NpcServiceDefinition> npcs = BuildDictionary(
            document.Npcs,
            npc => npc.NpcId,
            "npc");
        Dictionary<string, QuestServiceDefinition> quests = BuildDictionary(
            (document.Quests ?? Array.Empty<QuestServiceDocument>())
                .Select(MapQuest),
            quest => quest.QuestId,
            "quest");

        Validate(
            economyTypes,
            factions,
            markets,
            dialogues,
            npcs,
            quests,
            contentCatalog);
        return new StationServicesCatalog(
            document.SchemaVersion,
            economyTypes,
            factions,
            markets,
            dialogues,
            npcs,
            quests);
    }

    public MarketServiceDefinition GetMarket(string marketId)
    {
        return _markets.TryGetValue(marketId, out MarketServiceDefinition? value)
            ? value
            : throw new KeyNotFoundException($"Unknown market {marketId}.");
    }

    public NpcServiceDefinition GetNpc(string npcId)
    {
        return _npcs.TryGetValue(npcId, out NpcServiceDefinition? value)
            ? value
            : throw new KeyNotFoundException($"Unknown NPC {npcId}.");
    }

    public DialogueServiceDefinition GetDialogue(string dialogueId)
    {
        return _dialogues.TryGetValue(
            dialogueId,
            out DialogueServiceDefinition? value)
            ? value
            : throw new KeyNotFoundException(
                $"Unknown dialogue {dialogueId}.");
    }

    private static QuestServiceDefinition MapQuest(
        QuestServiceDocument document)
    {
        QuestNodeServiceDefinition[] nodes =
            (document.Nodes ?? Array.Empty<QuestNodeServiceDocument>())
                .Select(node => MapQuestNode(document.QuestId, node))
                .ToArray();
        return new QuestServiceDefinition(
            document.QuestId,
            document.LocalizationKey,
            document.GiverNpcId,
            document.StartNodeId,
            nodes,
            document.RewardCredits,
            document.ReputationReward);
    }

    private static QuestNodeServiceDefinition MapQuestNode(
        string questId,
        QuestNodeServiceDocument document)
    {
        if (!Enum.TryParse(
            document.ObjectiveType,
            ignoreCase: false,
            out StationServiceObjectiveType objectiveType))
        {
            throw new ContentValidationException(
                $"Quest {questId} node {document.NodeId} uses unsupported " +
                $"objective type {document.ObjectiveType}.");
        }

        return new QuestNodeServiceDefinition(
            document.NodeId,
            objectiveType,
            document.TargetDefinitionId,
            document.RequiredQuantity,
            document.NextNodeIds ?? Array.Empty<string>());
    }

    private static Dictionary<string, T> BuildDictionary<T>(
        IEnumerable<T>? definitions,
        Func<T, string> idSelector,
        string kind)
    {
        Dictionary<string, T> result = new(StringComparer.Ordinal);
        foreach (T definition in definitions ?? Array.Empty<T>())
        {
            string id = idSelector(definition);
            if (!GameContentCatalog.IsStableId(id) ||
                !result.TryAdd(id, definition))
            {
                throw new ContentValidationException(
                    $"station_services.json contains invalid or duplicate {kind} " +
                    $"ID {id}.");
            }
        }

        return result;
    }

    private static void Validate(
        IReadOnlyList<string> economyTypes,
        IReadOnlyDictionary<string, FactionServiceDefinition> factions,
        IReadOnlyDictionary<string, MarketServiceDefinition> markets,
        IReadOnlyDictionary<string, DialogueServiceDefinition> dialogues,
        IReadOnlyDictionary<string, NpcServiceDefinition> npcs,
        IReadOnlyDictionary<string, QuestServiceDefinition> quests,
        GameContentCatalog contentCatalog)
    {
        HashSet<string> economySet = new(economyTypes, StringComparer.Ordinal);
        if (economySet.Count != economyTypes.Count)
        {
            throw new ContentValidationException(
                "Station services economy type list contains duplicates.");
        }

        if (!economySet.SetEquals(RequiredEconomyTypes))
        {
            throw new ContentValidationException(
                "Station services must define exactly the six required economy " +
                "types: " + string.Join(", ", RequiredEconomyTypes) + ".");
        }

        if (factions.Count < 3)
        {
            throw new ContentValidationException(
                "Station services require at least three factions.");
        }

        if (markets.Count == 0 || npcs.Count == 0 || dialogues.Count == 0)
        {
            throw new ContentValidationException(
                "Station services require a market, NPC and dialogue.");
        }

        HashSet<string> factionEconomies = factions.Values
            .Select(faction => faction.EconomyType)
            .ToHashSet(StringComparer.Ordinal);
        foreach (string required in new[] { "Trading", "Scientific", "Military" })
        {
            if (!factionEconomies.Contains(required))
            {
                throw new ContentValidationException(
                    $"Missing required faction economy type {required}.");
            }
        }

        foreach (FactionServiceDefinition faction in factions.Values)
        {
            ValidateText(
                faction.LocalizationKey,
                faction.FactionId,
                "localization key");
            if (!economySet.Contains(faction.EconomyType) ||
                faction.PreferredTags is null ||
                faction.PreferredTags.Count == 0 ||
                faction.PreferredTags.Any(tag => string.IsNullOrWhiteSpace(tag)) ||
                faction.QuestTypes is null ||
                faction.QuestTypes.Count == 0 ||
                faction.QuestTypes.Any(value => string.IsNullOrWhiteSpace(value)) ||
                faction.NamePoolKeys is null ||
                faction.NamePoolKeys.Count == 0 ||
                faction.NamePoolKeys.Any(value =>
                    !GameContentCatalog.IsStableId(value)) ||
                faction.Relations is null ||
                string.IsNullOrWhiteSpace(faction.VisualStyle))
            {
                throw new ContentValidationException(
                    $"Faction {faction.FactionId} has incomplete service metadata.");
            }

            if (faction.Relations.Count != factions.Count)
            {
                throw new ContentValidationException(
                    $"Faction {faction.FactionId} must define a relation to every " +
                    "vertical-slice faction.");
            }

            foreach ((string relationFactionId, int relation) in faction.Relations)
            {
                if (!factions.ContainsKey(relationFactionId) ||
                    relation is < -100 or > 100)
                {
                    throw new ContentValidationException(
                        $"Faction {faction.FactionId} has invalid relation " +
                        $"{relationFactionId}={relation}.");
                }
            }
        }

        foreach (MarketServiceDefinition market in markets.Values)
        {
            if (!factions.ContainsKey(market.FactionId) ||
                !economySet.Contains(market.EconomyType))
            {
                throw new ContentValidationException(
                    $"Market {market.MarketId} references an unknown faction or " +
                    "economy type.");
            }

            if (!double.IsFinite(market.SystemEconomyModifier) ||
                !double.IsFinite(market.FactionModifier) ||
                !double.IsFinite(market.BuyMarkup) ||
                !double.IsFinite(market.SellMarkdown) ||
                market.SystemEconomyModifier <= 0.0 ||
                market.FactionModifier <= 0.0 ||
                market.BuyMarkup <= 0.0 ||
                market.SellMarkdown <= 0.0 ||
                market.SellMarkdown >= market.BuyMarkup ||
                market.InitialStockPerItem <= 0 ||
                market.TargetStockPerItem <= 0 ||
                market.PlayerStartingCredits < 0 ||
                market.MerchantStartingCredits < 0)
            {
                throw new ContentValidationException(
                    $"Market {market.MarketId} has invalid price, stock or credit " +
                    "parameters.");
            }
        }

        foreach (DialogueServiceDefinition dialogue in dialogues.Values)
        {
            ValidateText(
                dialogue.LocalizationKey,
                dialogue.DialogueId,
                "localization key");
            ValidateText(dialogue.GreetingKey, dialogue.DialogueId, "greeting key");
            ValidateText(dialogue.FarewellKey, dialogue.DialogueId, "farewell key");
            if (!GameContentCatalog.IsStableId(dialogue.GreetingKey) ||
                !GameContentCatalog.IsStableId(dialogue.FarewellKey))
            {
                throw new ContentValidationException(
                    $"Dialogue {dialogue.DialogueId} contains invalid localization keys.");
            }
            if (dialogue.Options is null || dialogue.Options.Count == 0)
            {
                throw new ContentValidationException(
                    $"Dialogue {dialogue.DialogueId} has no options.");
            }

            HashSet<string> optionIds = new(StringComparer.Ordinal);
            HashSet<string> actions = new(StringComparer.Ordinal);
            foreach (DialogueOptionServiceDefinition option in dialogue.Options)
            {
                if (!GameContentCatalog.IsStableId(option.OptionId) ||
                    !optionIds.Add(option.OptionId) ||
                    option.MinimumReputation is < -100 or > 100 ||
                    option.ReputationDelta is < -100 or > 100)
                {
                    throw new ContentValidationException(
                        $"Dialogue {dialogue.DialogueId} contains an invalid option.");
                }

                ValidateText(option.LocalizationKey, option.OptionId, "localization key");
                if (!GameContentCatalog.IsStableId(option.LocalizationKey))
                {
                    throw new ContentValidationException(
                        $"Dialogue option {option.OptionId} has invalid localization key.");
                }
                actions.Add(option.Action);
                if (option.Action is not ("OpenTrade" or "OpenQuests" or "Close"))
                {
                    throw new ContentValidationException(
                        $"Dialogue option {option.OptionId} uses unsupported action " +
                        $"{option.Action}.");
                }
            }

            foreach (string requiredAction in new[]
            {
                "OpenTrade",
                "OpenQuests",
                "Close"
            })
            {
                if (!actions.Contains(requiredAction))
                {
                    throw new ContentValidationException(
                        $"Dialogue {dialogue.DialogueId} is missing action " +
                        $"{requiredAction}.");
                }
            }
        }

        foreach (NpcServiceDefinition npc in npcs.Values)
        {
            if (!factions.ContainsKey(npc.FactionId) ||
                !markets.ContainsKey(npc.MarketId) ||
                !dialogues.ContainsKey(npc.DialogueId))
            {
                throw new ContentValidationException(
                    $"NPC {npc.NpcId} contains an unresolved faction, market or " +
                    "dialogue reference.");
            }

            ValidateText(npc.LocalizationKey, npc.NpcId, "localization key");
            if (!string.Equals(npc.NpcType, "Trader", StringComparison.Ordinal))
            {
                throw new ContentValidationException(
                    $"Vertical-slice NPC {npc.NpcId} must be a Trader.");
            }
        }

        if (quests.Count != 3)
        {
            throw new ContentValidationException(
                $"Vertical-slice station services require exactly three quests; " +
                $"found {quests.Count}.");
        }

        HashSet<StationServiceObjectiveType> objectiveTypes = new();
        foreach (QuestServiceDefinition quest in quests.Values)
        {
            ValidateQuestGraph(quest, npcs, contentCatalog, objectiveTypes);
        }

        foreach (StationServiceObjectiveType required in Enum.GetValues<
            StationServiceObjectiveType>())
        {
            if (!objectiveTypes.Contains(required))
            {
                throw new ContentValidationException(
                    $"Missing vertical-slice quest objective {required}.");
            }
        }

        if (contentCatalog.Items.Values.Any(item => item.BasePrice <= 0.0))
        {
            throw new ContentValidationException(
                "Every tradable item must define a positive BasePrice.");
        }
    }

    private static void ValidateQuestGraph(
        QuestServiceDefinition quest,
        IReadOnlyDictionary<string, NpcServiceDefinition> npcs,
        GameContentCatalog contentCatalog,
        ISet<StationServiceObjectiveType> objectiveTypes)
    {
        ValidateText(quest.LocalizationKey, quest.QuestId, "localization key");
        if (!npcs.ContainsKey(quest.GiverNpcId) ||
            quest.Nodes is null ||
            quest.Nodes.Count == 0 ||
            quest.RewardCredits < 0 ||
            quest.ReputationReward < 0)
        {
            throw new ContentValidationException(
                $"Quest {quest.QuestId} contains an invalid giver, graph or reward.");
        }

        Dictionary<string, QuestNodeServiceDefinition> nodes = new(
            StringComparer.Ordinal);
        foreach (QuestNodeServiceDefinition node in quest.Nodes)
        {
            if (!GameContentCatalog.IsStableId(node.NodeId) ||
                !nodes.TryAdd(node.NodeId, node) ||
                !contentCatalog.Items.TryGetValue(
                    node.TargetDefinitionId,
                    out GameItemDefinition? item) ||
                item is null ||
                node.RequiredQuantity <= 0 ||
                node.RequiredQuantity > item.MaxStack ||
                node.NextNodeIds is null ||
                node.NextNodeIds.Distinct(StringComparer.Ordinal).Count() !=
                    node.NextNodeIds.Count)
            {
                throw new ContentValidationException(
                    $"Quest {quest.QuestId} contains invalid node " +
                    $"{node.NodeId}.");
            }

            objectiveTypes.Add(node.ObjectiveType);
            bool feasible = node.ObjectiveType switch
            {
                StationServiceObjectiveType.CollectResource =>
                    contentCatalog.Resources.Values.Any(resource =>
                        string.Equals(
                            resource.ItemDefinitionId,
                            node.TargetDefinitionId,
                            StringComparison.Ordinal)),
                StationServiceObjectiveType.CraftItem =>
                    contentCatalog.Recipes.Values.Any(recipe =>
                        recipe.RuntimeEnabled && recipe.Outputs.Any(output =>
                            string.Equals(
                                output.DefinitionId,
                                node.TargetDefinitionId,
                                StringComparison.Ordinal))),
                StationServiceObjectiveType.TradeItem => true,
                _ => false
            };
            if (!feasible)
            {
                throw new ContentValidationException(
                    $"Quest {quest.QuestId} node {node.NodeId} is not feasible.");
            }
        }

        if (!nodes.ContainsKey(quest.StartNodeId))
        {
            throw new ContentValidationException(
                $"Quest {quest.QuestId} references missing start node " +
                $"{quest.StartNodeId}.");
        }

        foreach (QuestNodeServiceDefinition node in nodes.Values)
        {
            foreach (string nextNodeId in node.NextNodeIds)
            {
                if (!nodes.ContainsKey(nextNodeId))
                {
                    throw new ContentValidationException(
                        $"Quest {quest.QuestId} node {node.NodeId} references " +
                        $"missing next node {nextNodeId}.");
                }
            }
        }

        HashSet<string> visiting = new(StringComparer.Ordinal);
        HashSet<string> visited = new(StringComparer.Ordinal);
        VisitQuestNode(quest, quest.StartNodeId, visiting, visited);
        if (visited.Count != nodes.Count)
        {
            throw new ContentValidationException(
                $"Quest {quest.QuestId} contains unreachable nodes.");
        }
    }

    private static void VisitQuestNode(
        QuestServiceDefinition quest,
        string nodeId,
        ISet<string> visiting,
        ISet<string> visited)
    {
        if (visited.Contains(nodeId))
        {
            return;
        }

        if (!visiting.Add(nodeId))
        {
            throw new ContentValidationException(
                $"Quest {quest.QuestId} contains a dependency cycle at " +
                $"{nodeId}.");
        }

        foreach (string nextNodeId in quest.GetNode(nodeId).NextNodeIds)
        {
            VisitQuestNode(quest, nextNodeId, visiting, visited);
        }

        visiting.Remove(nodeId);
        visited.Add(nodeId);
    }

    private static void ValidateText(string value, string ownerId, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ContentValidationException(
                $"{ownerId} has an empty {field}.");
        }
    }

    private sealed record StationServicesDocument(
        int SchemaVersion,
        IReadOnlyList<string>? EconomyTypes,
        IReadOnlyList<FactionServiceDefinition>? Factions,
        IReadOnlyList<MarketServiceDefinition>? Markets,
        IReadOnlyList<DialogueServiceDefinition>? Dialogues,
        IReadOnlyList<NpcServiceDefinition>? Npcs,
        IReadOnlyList<QuestServiceDocument>? Quests);

    private sealed record QuestServiceDocument(
        string QuestId,
        string LocalizationKey,
        string GiverNpcId,
        string StartNodeId,
        IReadOnlyList<QuestNodeServiceDocument>? Nodes,
        int RewardCredits,
        int ReputationReward);

    private sealed record QuestNodeServiceDocument(
        string NodeId,
        string ObjectiveType,
        string TargetDefinitionId,
        int RequiredQuantity,
        IReadOnlyList<string>? NextNodeIds);
}
