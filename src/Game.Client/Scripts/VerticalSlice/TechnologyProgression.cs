using System;
using System.Collections.Generic;
using System.Linq;

public enum TechnologyUnlockResult
{
    Unlocked = 0,
    AlreadyUnlocked = 1,
    MissingPrerequisites = 2,
    InsufficientResearchPoints = 3,
    UnknownTechnology = 4
}

public sealed class TechnologyProgression
{
    private readonly IReadOnlyDictionary<string, TechnologyDefinition> _technologies;
    private readonly HashSet<string> _unlocked = new(StringComparer.Ordinal);

    public TechnologyProgression(
        IReadOnlyDictionary<string, TechnologyDefinition> technologies,
        int researchPoints,
        IEnumerable<string>? unlockedTechnologyIds = null)
    {
        ArgumentNullException.ThrowIfNull(technologies);
        if (researchPoints < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(researchPoints),
                "Research points must not be negative.");
        }

        _technologies = technologies;
        ResearchPoints = researchPoints;
        if (unlockedTechnologyIds is not null)
        {
            foreach (string technologyId in unlockedTechnologyIds)
            {
                if (!_technologies.ContainsKey(technologyId))
                {
                    throw new ArgumentException(
                        $"Unknown unlocked technology {technologyId}.",
                        nameof(unlockedTechnologyIds));
                }

                _unlocked.Add(technologyId);
            }
        }

        foreach (TechnologyDefinition technology in _technologies.Values)
        {
            if (technology.ResearchCost == 0 && technology.Prerequisites.Count == 0)
            {
                _unlocked.Add(technology.TechnologyId);
            }
        }
    }

    public int ResearchPoints { get; private set; }

    public IReadOnlyList<string> UnlockedTechnologyIds => _unlocked
        .OrderBy(id => id, StringComparer.Ordinal)
        .ToArray();

    public int UnlockedCount => _unlocked.Count;

    public bool IsUnlocked(string technologyId)
    {
        return string.IsNullOrWhiteSpace(technologyId) ||
            _unlocked.Contains(technologyId);
    }

    public IReadOnlyList<string> GetMissingPrerequisites(string technologyId)
    {
        if (!_technologies.TryGetValue(
                technologyId,
                out TechnologyDefinition? technology) ||
            technology is null)
        {
            return Array.Empty<string>();
        }

        return technology.Prerequisites
            .Where(prerequisite => !_unlocked.Contains(prerequisite))
            .OrderBy(prerequisite => prerequisite, StringComparer.Ordinal)
            .ToArray();
    }

    public TechnologyUnlockResult TryUnlock(
        string technologyId,
        out string result)
    {
        if (!_technologies.TryGetValue(
                technologyId,
                out TechnologyDefinition? technology) ||
            technology is null)
        {
            result = GameLocalizationService.Format("ui.tech.unknown", ("technology", technologyId));
            return TechnologyUnlockResult.UnknownTechnology;
        }

        if (_unlocked.Contains(technologyId))
        {
            result = GameLocalizationService.Format("ui.tech.already", ("technology", technologyId));
            return TechnologyUnlockResult.AlreadyUnlocked;
        }

        IReadOnlyList<string> missing = GetMissingPrerequisites(technologyId);
        if (missing.Count > 0)
        {
            result = GameLocalizationService.Format(
                "ui.tech.prerequisites",
                ("technology", technologyId),
                ("requirements", string.Join(", ", missing)));
            return TechnologyUnlockResult.MissingPrerequisites;
        }

        if (ResearchPoints < technology.ResearchCost)
        {
            result = GameLocalizationService.Format(
                "ui.tech.points",
                ("technology", technologyId),
                ("cost", technology.ResearchCost),
                ("available", ResearchPoints));
            return TechnologyUnlockResult.InsufficientResearchPoints;
        }

        ResearchPoints -= technology.ResearchCost;
        _unlocked.Add(technologyId);
        result = GameLocalizationService.Format(
            "ui.tech.unlocked",
            ("technology", technologyId),
            ("cost", technology.ResearchCost),
            ("remaining", ResearchPoints));
        return TechnologyUnlockResult.Unlocked;
    }

    public IReadOnlyList<TechnologyDefinition> GetRelevantTechnologies(
        IEnumerable<CraftingRecipeDefinition> recipes)
    {
        ArgumentNullException.ThrowIfNull(recipes);
        HashSet<string> relevant = new(StringComparer.Ordinal);
        foreach (CraftingRecipeDefinition recipe in recipes)
        {
            AddTechnologyClosure(recipe.RequiredTechnology, relevant);
        }

        return relevant
            .Select(id => _technologies[id])
            .OrderBy(technology => technology.Tier)
            .ThenBy(technology => technology.TechnologyId, StringComparer.Ordinal)
            .ToArray();
    }

    public TechnologyProgressSaveData ToSaveData()
    {
        return new TechnologyProgressSaveData(
            ResearchPoints,
            UnlockedTechnologyIds);
    }

    public static TechnologyProgression FromSaveData(
        IReadOnlyDictionary<string, TechnologyDefinition> technologies,
        TechnologyProgressSaveData? saveData,
        int defaultResearchPoints)
    {
        return saveData is null
            ? new TechnologyProgression(
                technologies,
                defaultResearchPoints,
                unlockedTechnologyIds: null)
            : new TechnologyProgression(
                technologies,
                saveData.ResearchPoints,
                saveData.UnlockedTechnologyIds);
    }

    private void AddTechnologyClosure(
        string technologyId,
        HashSet<string> result)
    {
        if (string.IsNullOrWhiteSpace(technologyId) ||
            !result.Add(technologyId) ||
            !_technologies.TryGetValue(
                technologyId,
                out TechnologyDefinition? technology) ||
            technology is null)
        {
            return;
        }

        foreach (string prerequisite in technology.Prerequisites)
        {
            AddTechnologyClosure(prerequisite, result);
        }
    }
}

public sealed record StationRecipeSelectorEntry(
    CraftingRecipeDefinition Recipe,
    bool TechnologyUnlocked,
    bool Crafted,
    int MissingInputQuantity)
{
    public bool InputsAvailable => MissingInputQuantity == 0;

    public bool Craftable => TechnologyUnlocked && !Crafted && InputsAvailable;
}

public sealed class StationRecipeSelectorModel
{
    private readonly GameContentCatalog _catalog;
    private readonly StarterRepairSession _session;
    private readonly TechnologyProgression _technologyProgression;

    public StationRecipeSelectorModel(
        GameContentCatalog catalog,
        StarterRepairSession session,
        TechnologyProgression technologyProgression)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(technologyProgression);
        _catalog = catalog;
        _session = session;
        _technologyProgression = technologyProgression;
    }

    public IReadOnlyList<CraftingRecipeDefinition> GetRecipes(string stationId)
    {
        return _catalog.Recipes.Values
            .Where(recipe =>
                recipe.RuntimeEnabled &&
                string.Equals(
                    recipe.Application.Type,
                    "StoreOutputs",
                    StringComparison.Ordinal) &&
                string.Equals(
                    recipe.RequiredStation,
                    stationId,
                    StringComparison.Ordinal))
            .OrderBy(recipe => recipe.TechnologyTier)
            .ThenBy(recipe => recipe.Category, StringComparer.Ordinal)
            .ThenBy(recipe => recipe.RecipeId, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<StationRecipeSelectorEntry> GetRecipeEntries(
        string stationId)
    {
        return GetRecipes(stationId)
            .Select(recipe => new StationRecipeSelectorEntry(
                recipe,
                _technologyProgression.IsUnlocked(recipe.RequiredTechnology),
                !IndustryRecipePolicy.IsRepeatable(recipe) &&
                    _session.IsRecipeCrafted(recipe.RecipeId),
                recipe.Inputs.Sum(input => Math.Max(
                    0,
                    input.Quantity -
                    _session.GetAvailableQuantity(input.DefinitionId)))))
            .ToArray();
    }

    public IReadOnlyList<TechnologyDefinition> GetResearchEntries(
        string stationId)
    {
        return _technologyProgression.GetRelevantTechnologies(
            GetRecipes(stationId));
    }
}
