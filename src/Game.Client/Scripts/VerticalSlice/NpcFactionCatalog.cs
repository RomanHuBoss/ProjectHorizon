using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

public enum NpcArchetype
{
    Trader = 0,
    Technician = 1,
    Pilot = 2,
    Scientist = 3,
    Guard = 4,
    GuildRepresentative = 5,
    Traveler = 6,
    Opponent = 7
}

public sealed record NpcArchetypeDefinition(
    NpcArchetype Archetype,
    string LocalizationKey);

public sealed record NpcFactionAgentDefinition(
    string NpcId,
    string DisplayNameKey,
    NpcArchetype Archetype,
    string FactionId,
    string DialogueId,
    double PositionX,
    double PositionY,
    double PositionZ,
    double PatrolRadius,
    double Health,
    double WalkSpeed,
    double DetectionRange,
    double AttackRange,
    double AttackDamage,
    double AttackCooldownSeconds,
    bool ExistingScene,
    bool Hostile,
    bool Respawnable,
    bool CanBeProtected,
    double ColorR,
    double ColorG,
    double ColorB);

public sealed record NpcDialogueOptionDefinition(
    string OptionId,
    string TextKey,
    string Condition,
    string Action,
    int MinimumReputation,
    int ReputationDelta,
    string ConsequenceKey);

public sealed record NpcDialogueDefinition(
    string DialogueId,
    NpcArchetype Archetype,
    string GreetingKey,
    string FarewellKey,
    IReadOnlyList<NpcDialogueOptionDefinition> Options);

public sealed class NpcFactionCatalog
{
    public const int CurrentSchemaVersion = 1;
    public const int ExpectedFactionCount = 3;
    public const int ExpectedArchetypeCount = 8;
    public const int ExpectedAgentCount = 8;

    private static readonly HashSet<string> AllowedActions = new(
        new[]
        {
            "OpenTrade", "OpenMissions", "RepairHint", "FlightHint",
            "ScienceHint", "GuardBriefing", "TravelerStory",
            "AcknowledgeProtection", "HostileWarning", "Close"
        },
        StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict
    };

    private readonly Dictionary<NpcArchetype, NpcArchetypeDefinition> _archetypes;
    private readonly Dictionary<string, NpcFactionAgentDefinition> _agents;
    private readonly Dictionary<string, NpcDialogueDefinition> _dialogues;
    private readonly Dictionary<string, FactionServiceDefinition> _factions;

    private NpcFactionCatalog(
        int schemaVersion,
        long worldSeed,
        string regionKey,
        Dictionary<NpcArchetype, NpcArchetypeDefinition> archetypes,
        Dictionary<string, NpcFactionAgentDefinition> agents,
        Dictionary<string, NpcDialogueDefinition> dialogues,
        Dictionary<string, FactionServiceDefinition> factions)
    {
        SchemaVersion = schemaVersion;
        WorldSeed = worldSeed;
        RegionKey = regionKey;
        _archetypes = archetypes;
        _agents = agents;
        _dialogues = dialogues;
        _factions = factions;
    }

    public int SchemaVersion { get; }

    public long WorldSeed { get; }

    public string RegionKey { get; }

    public IReadOnlyDictionary<NpcArchetype, NpcArchetypeDefinition> Archetypes =>
        _archetypes;

    public IReadOnlyDictionary<string, NpcFactionAgentDefinition> Agents =>
        _agents;

    public IReadOnlyDictionary<string, NpcDialogueDefinition> Dialogues =>
        _dialogues;

    public IReadOnlyDictionary<string, FactionServiceDefinition> Factions =>
        _factions;

    public IReadOnlyList<string> DefeatTargetIds => _agents.Values
        .Where(agent => agent.Archetype == NpcArchetype.Opponent)
        .Select(agent => agent.NpcId)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

    public IReadOnlyList<string> ProtectTargetIds => _agents.Values
        .Where(agent => agent.CanBeProtected)
        .Select(agent => agent.NpcId)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

    public NpcFactionAgentDefinition GetAgent(string npcId) =>
        _agents.TryGetValue(npcId, out NpcFactionAgentDefinition? agent)
            ? agent
            : throw new KeyNotFoundException($"Unknown NPC {npcId}.");

    public NpcDialogueDefinition GetDialogue(string dialogueId) =>
        _dialogues.TryGetValue(dialogueId, out NpcDialogueDefinition? dialogue)
            ? dialogue
            : throw new KeyNotFoundException($"Unknown NPC dialogue {dialogueId}.");

    public static NpcFactionCatalog LoadFromJson(
        string json,
        StationServicesCatalog stationServices)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(stationServices);
        NpcFactionDocument document;
        try
        {
            document = JsonSerializer.Deserialize<NpcFactionDocument>(
                json,
                JsonOptions) ?? throw new ContentValidationException(
                    "npc_factions.json deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw new ContentValidationException(
                $"npc_factions.json is invalid JSON: {exception.Message}");
        }

        if (document.SchemaVersion != CurrentSchemaVersion ||
            document.WorldSeed <= 0 ||
            string.IsNullOrWhiteSpace(document.RegionKey) ||
            !document.RegionKey.StartsWith("region.", StringComparison.Ordinal) ||
            document.Archetypes is null ||
            document.Agents is null ||
            document.Dialogues is null)
        {
            throw new ContentValidationException(
                "npc_factions.json has invalid schema, seed, region or null collections.");
        }

        if (stationServices.Factions.Count != ExpectedFactionCount)
        {
            throw new ContentValidationException(
                $"NPC/faction core requires exactly {ExpectedFactionCount} station-service factions.");
        }
        ValidateFactionMatrix(stationServices.Factions);

        Dictionary<NpcArchetype, NpcArchetypeDefinition> archetypes = new();
        foreach (NpcArchetypeDocument raw in document.Archetypes)
        {
            if (!Enum.TryParse(raw.Archetype, false, out NpcArchetype archetype) ||
                !Enum.IsDefined(archetype) ||
                !archetypes.TryAdd(
                    archetype,
                    new NpcArchetypeDefinition(
                        archetype,
                        raw.LocalizationKey)) ||
                !GameContentCatalog.IsStableId(raw.LocalizationKey))
            {
                throw new ContentValidationException(
                    $"Invalid or duplicate NPC archetype {raw.Archetype}.");
            }
        }
        if (archetypes.Count != ExpectedArchetypeCount ||
            Enum.GetValues<NpcArchetype>().Any(value => !archetypes.ContainsKey(value)))
        {
            throw new ContentValidationException(
                $"NPC archetype coverage must be exactly {ExpectedArchetypeCount}/{ExpectedArchetypeCount}.");
        }

        Dictionary<string, NpcDialogueDefinition> dialogues = new(
            StringComparer.Ordinal);
        HashSet<NpcArchetype> dialogueArchetypes = new();
        foreach (NpcDialogueDocument raw in document.Dialogues)
        {
            if (!Enum.TryParse(raw.Archetype, false, out NpcArchetype archetype) ||
                !Enum.IsDefined(archetype) ||
                !GameContentCatalog.IsStableId(raw.DialogueId) ||
                !raw.DialogueId.StartsWith("dialogue.", StringComparison.Ordinal) ||
                !GameContentCatalog.IsStableId(raw.GreetingKey) ||
                !GameContentCatalog.IsStableId(raw.FarewellKey) ||
                raw.Options is null || raw.Options.Length == 0 ||
                !dialogueArchetypes.Add(archetype))
            {
                throw new ContentValidationException(
                    $"Invalid or duplicate NPC dialogue {raw.DialogueId}.");
            }

            HashSet<string> optionIds = new(StringComparer.Ordinal);
            NpcDialogueOptionDefinition[] options = raw.Options.Select(option =>
            {
                if (!GameContentCatalog.IsStableId(option.OptionId) ||
                    !option.OptionId.StartsWith("dialogue_option.", StringComparison.Ordinal) ||
                    !optionIds.Add(option.OptionId) ||
                    !GameContentCatalog.IsStableId(option.TextKey) ||
                    !ValidateDialogueCondition(option.Condition, option.MinimumReputation) ||
                    !AllowedActions.Contains(option.Action) ||
                    option.MinimumReputation is < -100 or > 100 ||
                    option.ReputationDelta is < -20 or > 20 ||
                    !GameContentCatalog.IsStableId(option.ConsequenceKey))
                {
                    throw new ContentValidationException(
                        $"Dialogue {raw.DialogueId} has invalid option {option.OptionId}.");
                }
                return new NpcDialogueOptionDefinition(
                    option.OptionId,
                    option.TextKey,
                    option.Condition,
                    option.Action,
                    option.MinimumReputation,
                    option.ReputationDelta,
                    option.ConsequenceKey);
            }).ToArray();
            if (options.All(option => !string.Equals(
                    option.Action,
                    "Close",
                    StringComparison.Ordinal)))
            {
                throw new ContentValidationException(
                    $"Dialogue {raw.DialogueId} must contain a Close option.");
            }
            if (!dialogues.TryAdd(
                    raw.DialogueId,
                    new NpcDialogueDefinition(
                        raw.DialogueId,
                        archetype,
                        raw.GreetingKey,
                        raw.FarewellKey,
                        options)))
            {
                throw new ContentValidationException(
                    $"Duplicate NPC dialogue ID {raw.DialogueId}.");
            }
        }
        if (dialogues.Count != ExpectedArchetypeCount)
        {
            throw new ContentValidationException(
                $"NPC dialogue templates must cover all {ExpectedArchetypeCount} archetypes.");
        }

        Dictionary<string, NpcFactionAgentDefinition> agents = new(
            StringComparer.Ordinal);
        Dictionary<NpcArchetype, int> archetypeCounts = Enum
            .GetValues<NpcArchetype>()
            .ToDictionary(value => value, _ => 0);
        foreach (NpcAgentDocument raw in document.Agents)
        {
            if (!Enum.TryParse(raw.Archetype, false, out NpcArchetype archetype) ||
                !Enum.IsDefined(archetype) ||
                !GameContentCatalog.IsStableId(raw.NpcId) ||
                !raw.NpcId.StartsWith("npc.", StringComparison.Ordinal) ||
                !GameContentCatalog.IsStableId(raw.DisplayNameKey) ||
                !GameContentCatalog.IsStableId(raw.DialogueId) ||
                !dialogues.TryGetValue(raw.DialogueId, out NpcDialogueDefinition? dialogue) ||
                dialogue.Archetype != archetype ||
                !double.IsFinite(raw.PositionX) || !double.IsFinite(raw.PositionY) ||
                !double.IsFinite(raw.PositionZ) ||
                Math.Abs(raw.PositionX) > 38.0 || Math.Abs(raw.PositionZ) > 38.0 ||
                raw.PositionY is < 0.0 or > 4.0 ||
                !double.IsFinite(raw.PatrolRadius) || raw.PatrolRadius is < 0.0 or > 12.0 ||
                !double.IsFinite(raw.Health) || raw.Health is <= 0.0 or > 500.0 ||
                !double.IsFinite(raw.WalkSpeed) || raw.WalkSpeed is < 0.0 or > 8.0 ||
                !double.IsFinite(raw.DetectionRange) || raw.DetectionRange is < 0.0 or > 50.0 ||
                !double.IsFinite(raw.AttackRange) || raw.AttackRange is < 0.0 or > 10.0 ||
                !double.IsFinite(raw.AttackDamage) || raw.AttackDamage is < 0.0 or > 100.0 ||
                !double.IsFinite(raw.AttackCooldownSeconds) || raw.AttackCooldownSeconds is < 0.0 or > 30.0 ||
                !ValidColor(raw.ColorR) || !ValidColor(raw.ColorG) || !ValidColor(raw.ColorB))
            {
                throw new ContentValidationException(
                    $"Invalid NPC agent {raw.NpcId}.");
            }

            bool opponent = archetype == NpcArchetype.Opponent;
            if ((!opponent && (!GameContentCatalog.IsStableId(raw.FactionId) ||
                               !stationServices.Factions.ContainsKey(raw.FactionId) ||
                               !stationServices.Factions[raw.FactionId].NamePoolKeys.Contains(
                                   raw.DisplayNameKey,
                                   StringComparer.Ordinal))) ||
                (opponent && !string.IsNullOrEmpty(raw.FactionId)) ||
                raw.Hostile != opponent ||
                raw.Respawnable != opponent ||
                (raw.CanBeProtected && (opponent || raw.ExistingScene)) ||
                (opponent && (raw.AttackDamage <= 0.0 || raw.AttackRange <= 0.0 ||
                              raw.DetectionRange <= raw.AttackRange ||
                              raw.AttackCooldownSeconds <= 0.0)) ||
                (!opponent && (raw.AttackDamage != 0.0 || raw.AttackRange != 0.0)) ||
                (raw.ExistingScene && archetype != NpcArchetype.Trader))
            {
                throw new ContentValidationException(
                    $"NPC {raw.NpcId} has inconsistent faction/combat flags.");
            }

            NpcFactionAgentDefinition definition = new(
                raw.NpcId,
                raw.DisplayNameKey,
                archetype,
                raw.FactionId,
                raw.DialogueId,
                raw.PositionX,
                raw.PositionY,
                raw.PositionZ,
                raw.PatrolRadius,
                raw.Health,
                raw.WalkSpeed,
                raw.DetectionRange,
                raw.AttackRange,
                raw.AttackDamage,
                raw.AttackCooldownSeconds,
                raw.ExistingScene,
                raw.Hostile,
                raw.Respawnable,
                raw.CanBeProtected,
                raw.ColorR,
                raw.ColorG,
                raw.ColorB);
            if (!agents.TryAdd(raw.NpcId, definition))
            {
                throw new ContentValidationException(
                    $"Duplicate NPC ID {raw.NpcId}.");
            }
            archetypeCounts[archetype]++;
        }

        if (agents.Count != ExpectedAgentCount ||
            archetypeCounts.Any(pair => pair.Value != 1))
        {
            throw new ContentValidationException(
                $"NPC population must contain exactly one agent for every archetype ({ExpectedAgentCount} total).");
        }
        NpcFactionAgentDefinition trader = agents.Values.Single(
            agent => agent.Archetype == NpcArchetype.Trader);
        if (!trader.ExistingScene ||
            !stationServices.Npcs.ContainsKey(trader.NpcId))
        {
            throw new ContentValidationException(
                "NPC Trader must reuse the existing Station Services NPC without duplication.");
        }
        if (agents.Values.Count(agent => agent.CanBeProtected) < 2 ||
            agents.Values.Count(agent => agent.Hostile) != 1)
        {
            throw new ContentValidationException(
                "NPC core requires at least two protected targets and exactly one hostile opponent.");
        }

        NpcDialogueDefinition traderDialogue = dialogues.Values.Single(
            dialogue => dialogue.Archetype == NpcArchetype.Trader);
        NpcDialogueDefinition guildDialogue = dialogues.Values.Single(
            dialogue => dialogue.Archetype == NpcArchetype.GuildRepresentative);
        if (!traderDialogue.Options.Any(option =>
                string.Equals(option.Action, "OpenTrade", StringComparison.Ordinal)) ||
            !guildDialogue.Options.Any(option =>
                string.Equals(option.Action, "OpenMissions", StringComparison.Ordinal)))
        {
            throw new ContentValidationException(
                "Trader dialogue must expose trade and guild dialogue must expose missions.");
        }

        return new NpcFactionCatalog(
            document.SchemaVersion,
            document.WorldSeed,
            document.RegionKey,
            archetypes,
            agents,
            dialogues,
            stationServices.Factions.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal));
    }

    private static bool ValidateDialogueCondition(
        string condition,
        int minimumReputation)
    {
        if (string.Equals(condition, "always", StringComparison.Ordinal))
        {
            return minimumReputation == -100;
        }
        const string prefix = "reputation>=";
        return condition.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(
                condition[prefix.Length..],
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out int parsed) &&
            parsed == minimumReputation &&
            parsed is >= -100 and <= 100;
    }

    private static bool ValidColor(double value) =>
        double.IsFinite(value) && value is >= 0.0 and <= 1.0;

    private static void ValidateFactionMatrix(
        IReadOnlyDictionary<string, FactionServiceDefinition> factions)
    {
        string[] ids = factions.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        foreach (FactionServiceDefinition faction in factions.Values)
        {
            if (!GameContentCatalog.IsStableId(faction.FactionId) ||
                faction.Relations is null ||
                faction.Relations.Count != ids.Length ||
                ids.Any(id => !faction.Relations.TryGetValue(id, out int relation) ||
                    relation is < -100 or > 100) ||
                !faction.Relations.TryGetValue(faction.FactionId, out int self) ||
                self != 100 ||
                faction.NamePoolKeys is null || faction.NamePoolKeys.Count == 0 ||
                faction.PreferredTags is null || faction.PreferredTags.Count == 0 ||
                faction.QuestTypes is null || faction.QuestTypes.Count == 0 ||
                string.IsNullOrWhiteSpace(faction.VisualStyle) ||
                string.IsNullOrWhiteSpace(faction.EconomyType))
            {
                throw new ContentValidationException(
                    $"Faction {faction.FactionId} is incomplete for NPC runtime.");
            }
            foreach (string other in ids)
            {
                if (faction.Relations[other] != factions[other].Relations[faction.FactionId])
                {
                    throw new ContentValidationException(
                        $"Faction relation matrix is not reciprocal: {faction.FactionId} <-> {other}.");
                }
            }
        }
    }

    private sealed record NpcFactionDocument(
        int SchemaVersion,
        long WorldSeed,
        string RegionKey,
        NpcArchetypeDocument[] Archetypes,
        NpcAgentDocument[] Agents,
        NpcDialogueDocument[] Dialogues);

    private sealed record NpcArchetypeDocument(
        string Archetype,
        string LocalizationKey);

    private sealed record NpcAgentDocument(
        string NpcId,
        string DisplayNameKey,
        string Archetype,
        string FactionId,
        string DialogueId,
        double PositionX,
        double PositionY,
        double PositionZ,
        double PatrolRadius,
        double Health,
        double WalkSpeed,
        double DetectionRange,
        double AttackRange,
        double AttackDamage,
        double AttackCooldownSeconds,
        bool ExistingScene,
        bool Hostile,
        bool Respawnable,
        bool CanBeProtected,
        double ColorR,
        double ColorG,
        double ColorB);

    private sealed record NpcDialogueDocument(
        string DialogueId,
        string Archetype,
        string GreetingKey,
        string FarewellKey,
        NpcDialogueOptionDocument[] Options);

    private sealed record NpcDialogueOptionDocument(
        string OptionId,
        string TextKey,
        string Condition,
        string Action,
        int MinimumReputation,
        int ReputationDelta,
        string ConsequenceKey);
}
