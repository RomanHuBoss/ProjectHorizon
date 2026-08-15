using System.Text.Json;

namespace ProjectHorizon.Tests.Support;

internal static class RepositoryFixture
{
    static RepositoryFixture()
    {
        GameLocalizationService.InitializeForStandaloneTests(
            File.ReadAllText(Path.Combine(
                Root, "src", "Game.Client", "Content", "localization.en.json")),
            File.ReadAllText(Path.Combine(
                Root, "src", "Game.Client", "Content", "localization.ru.json")));
    }

    private static readonly Lazy<string> RootLazy = new(FindRoot);
    private static readonly Lazy<GameContentCatalog> ContentLazy = new(LoadContentCore);
    private static readonly Lazy<StationServicesCatalog> StationLazy = new(() =>
        StationServicesCatalog.LoadFromJson(ReadContent("station_services.json"), Content));
    private static readonly Lazy<ShipSystemsCatalog> ShipsLazy = new(() =>
        ShipSystemsCatalog.LoadFromJson(ReadContent("ships.json"), Content));
    private static readonly Lazy<BaseConstructionCatalog> BaseLazy = new(() =>
        BaseConstructionCatalog.LoadFromJson(ReadContent("base_construction.json"), Content));
    private static readonly Lazy<PlanetaryPoiCatalog> PoiLazy = new(() =>
        PlanetaryPoiCatalog.LoadFromJson(ReadContent("planetary_pois.json")));
    private static readonly Lazy<ProceduralQuestCatalog> ProceduralQuestLazy = new(() =>
        ProceduralQuestCatalog.LoadFromJson(ReadContent("procedural_quests.json"), StationServices));

    public static string Root => RootLazy.Value;
    public static GameContentCatalog Content => ContentLazy.Value;
    public static StationServicesCatalog StationServices => StationLazy.Value;
    public static ShipSystemsCatalog Ships => ShipsLazy.Value;
    public static BaseConstructionCatalog BaseConstruction => BaseLazy.Value;
    public static PlanetaryPoiCatalog Pois => PoiLazy.Value;
    public static ProceduralQuestCatalog ProceduralQuests => ProceduralQuestLazy.Value;

    public static string ReadContent(string fileName) =>
        File.ReadAllText(Path.Combine(Root, "src", "Game.Client", "Content", fileName));

    public static string NewTempPath(string fileName)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "ProjectHorizon.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }

    public static T ReadJson<T>(string relativePath)
    {
        string text = File.ReadAllText(Path.Combine(Root, relativePath));
        return JsonSerializer.Deserialize<T>(text, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidDataException($"Unable to deserialize {relativePath}.");
    }

    private static GameContentCatalog LoadContentCore() =>
        GameContentCatalog.LoadFromJson(
            ReadContent("items.json"),
            ReadContent("resources.json"),
            ReadContent("recipes.json"),
            ReadContent("stations.json"),
            ReadContent("technologies.json"));

    private static string FindRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "global.json")) &&
                File.Exists(Path.Combine(current.FullName, "REQUIREMENTS_STATUS.md")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Repository root not found above {AppContext.BaseDirectory}.");
    }
}
