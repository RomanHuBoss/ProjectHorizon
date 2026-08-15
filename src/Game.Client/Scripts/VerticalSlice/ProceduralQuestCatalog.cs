using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

public enum ProceduralQuestObjectiveType
{
    VisitLocation = 0,
    ScanObject = 1,
    ScanSpecies = 2,
    CollectResource = 3,
    CraftItem = 4,
    DeliverItem = 5,
    RepairObject = 6,
    DefeatTarget = 7,
    ProtectTarget = 8,
    BuildModule = 9,
    TradeItem = 10,
    FindSignal = 11,
    ExplorePlanet = 12,
    ExploreSystem = 13,
    ReturnToNpc = 14
}

public sealed record ProceduralQuestObjectiveProfile(
    ProceduralQuestObjectiveType ObjectiveType,
    int Weight,
    int BaseRewardCredits,
    int ReputationReward,
    IReadOnlyList<string> Factions);

public sealed class ProceduralQuestCatalog
{
    public const int CurrentSchemaVersion = 1;
    public const int ExpectedObjectiveTypeCount = 15;
    public const int ExpectedBoardSize = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict
    };

    private readonly Dictionary<ProceduralQuestObjectiveType,
        ProceduralQuestObjectiveProfile> _profiles;

    private ProceduralQuestCatalog(
        int schemaVersion,
        long worldSeed,
        int boardSize,
        int maximumActive,
        Dictionary<ProceduralQuestObjectiveType,
            ProceduralQuestObjectiveProfile> profiles)
    {
        SchemaVersion = schemaVersion;
        WorldSeed = worldSeed;
        BoardSize = boardSize;
        MaximumActive = maximumActive;
        _profiles = profiles;
    }

    public int SchemaVersion { get; }
    public long WorldSeed { get; }
    public int BoardSize { get; }
    public int MaximumActive { get; }
    public IReadOnlyDictionary<ProceduralQuestObjectiveType,
        ProceduralQuestObjectiveProfile> Profiles => _profiles;

    public ProceduralQuestObjectiveProfile GetProfile(
        ProceduralQuestObjectiveType objectiveType) =>
        _profiles.TryGetValue(objectiveType, out ProceduralQuestObjectiveProfile? value)
            ? value
            : throw new KeyNotFoundException(
                $"No procedural quest profile for {objectiveType}.");

    public static ProceduralQuestCatalog LoadFromJson(
        string json,
        StationServicesCatalog stationServicesCatalog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(stationServicesCatalog);
        ProceduralQuestCatalogDocument document;
        try
        {
            document = JsonSerializer.Deserialize<ProceduralQuestCatalogDocument>(
                json,
                JsonOptions) ?? throw new ContentValidationException(
                    "procedural_quests.json deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw new ContentValidationException(
                $"procedural_quests.json is invalid: {exception.Message}");
        }

        if (document.SchemaVersion != CurrentSchemaVersion ||
            document.WorldSeed <= 0 ||
            document.BoardSize != ExpectedBoardSize ||
            document.MaximumActive <= 0 ||
            document.MaximumActive > document.BoardSize)
        {
            throw new ContentValidationException(
                "procedural_quests.json has invalid schema, seed or board limits.");
        }

        Dictionary<ProceduralQuestObjectiveType, ProceduralQuestObjectiveProfile>
            profiles = new();
        foreach (ProceduralQuestObjectiveProfileDocument source in
            document.ObjectiveTypes ?? Array.Empty<ProceduralQuestObjectiveProfileDocument>())
        {
            if (!Enum.TryParse(
                    source.ObjectiveType,
                    ignoreCase: false,
                    out ProceduralQuestObjectiveType objectiveType) ||
                !Enum.IsDefined(objectiveType))
            {
                throw new ContentValidationException(
                    $"Unknown procedural quest objective type {source.ObjectiveType}.");
            }
            if (source.Weight <= 0 || source.BaseRewardCredits <= 0 ||
                source.ReputationReward < 0 || source.Factions is null ||
                source.Factions.Length == 0 ||
                source.Factions.Distinct(StringComparer.Ordinal).Count() !=
                    source.Factions.Length)
            {
                throw new ContentValidationException(
                    $"Objective profile {objectiveType} has invalid balancing data.");
            }
            foreach (string factionId in source.Factions)
            {
                if (!stationServicesCatalog.Factions.ContainsKey(factionId))
                {
                    throw new ContentValidationException(
                        $"Objective profile {objectiveType} references unknown faction {factionId}.");
                }
            }
            if (!profiles.TryAdd(
                    objectiveType,
                    new ProceduralQuestObjectiveProfile(
                        objectiveType,
                        source.Weight,
                        source.BaseRewardCredits,
                        source.ReputationReward,
                        source.Factions.OrderBy(value => value, StringComparer.Ordinal).ToArray())))
            {
                throw new ContentValidationException(
                    $"Duplicate procedural quest objective profile {objectiveType}.");
            }
        }

        if (profiles.Count != ExpectedObjectiveTypeCount ||
            Enum.GetValues<ProceduralQuestObjectiveType>().Any(type =>
                !profiles.ContainsKey(type)))
        {
            throw new ContentValidationException(
                $"Procedural quest catalog must define exactly all {ExpectedObjectiveTypeCount} objective types.");
        }

        return new ProceduralQuestCatalog(
            document.SchemaVersion,
            document.WorldSeed,
            document.BoardSize,
            document.MaximumActive,
            profiles);
    }

    private sealed record ProceduralQuestCatalogDocument(
        int SchemaVersion,
        long WorldSeed,
        int BoardSize,
        int MaximumActive,
        ProceduralQuestObjectiveProfileDocument[]? ObjectiveTypes);

    private sealed record ProceduralQuestObjectiveProfileDocument(
        string ObjectiveType,
        int Weight,
        int BaseRewardCredits,
        int ReputationReward,
        string[] Factions);
}
