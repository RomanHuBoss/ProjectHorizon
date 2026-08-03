using System;

public static class IndustryRecipePolicy
{
    public static bool IsRepeatable(CraftingRecipeDefinition recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return string.Equals(recipe.Category, "Refining", StringComparison.Ordinal) ||
            string.Equals(recipe.Category, "Chemistry", StringComparison.Ordinal);
    }
}
