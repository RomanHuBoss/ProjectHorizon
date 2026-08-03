using System;
using System.Collections.Generic;
using System.Linq;

public sealed record CatalogResourcePlacement(
    string ResourceNodeId,
    string ResourceDefinitionId,
    string NodeName,
    double PositionX,
    double PositionY,
    double PositionZ);

/// <summary>
/// Produces a deterministic physical placement for every catalog world resource
/// that is not already represented by a hand-authored scene node. The planner
/// is Godot-independent so catalog coverage and placement stability can be
/// acceptance-tested without touching the scene tree.
/// </summary>
public static class CatalogResourceFieldPlanner
{
    public const int Columns = 7;
    public const double StartX = -15.0;
    public const double StartZ = 23.0;
    public const double SpacingX = 5.0;
    public const double SpacingZ = 4.5;
    public const double ElevationY = 0.7;

    public static IReadOnlyList<CatalogResourcePlacement>
        BuildMissingPlacements(
            IReadOnlyDictionary<string, GameResourceDefinition> resources,
            IEnumerable<string> existingResourceDefinitionIds)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(existingResourceDefinitionIds);
        if (resources.Count == 0)
        {
            throw new InvalidOperationException(
                "Catalog resource field requires at least one resource definition.");
        }

        HashSet<string> existing = existingResourceDefinitionIds
            .Select(ValidateResourceId)
            .ToHashSet(StringComparer.Ordinal);
        string[] unknownExisting = existing
            .Except(resources.Keys, StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        if (unknownExisting.Length > 0)
        {
            throw new InvalidOperationException(
                "Scene resource bindings reference unknown catalog resources: " +
                string.Join(", ", unknownExisting));
        }

        foreach (GameResourceDefinition resource in resources.Values)
        {
            ValidateResourceId(resource.ResourceId);
        }

        GameResourceDefinition[] missing = resources.Values
            .Where(resource => !existing.Contains(resource.ResourceId))
            .OrderBy(resource => resource.ResourceId, StringComparer.Ordinal)
            .ToArray();
        List<CatalogResourcePlacement> placements = new(missing.Length);
        for (int index = 0; index < missing.Length; index++)
        {
            GameResourceDefinition resource = missing[index];
            int row = index / Columns;
            int column = index % Columns;
            string suffix = resource.ResourceId["resource.".Length..];
            placements.Add(new CatalogResourcePlacement(
                $"catalog.{suffix}",
                resource.ResourceId,
                "Catalog" + ToPascalCase(suffix),
                StartX + column * SpacingX,
                ElevationY,
                StartZ + row * SpacingZ));
        }

        ValidatePlacements(placements);
        return placements;
    }

    public static bool CoversCatalog(
        IReadOnlyDictionary<string, GameResourceDefinition> resources,
        IEnumerable<string> physicalResourceDefinitionIds)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(physicalResourceDefinitionIds);
        HashSet<string> physical = physicalResourceDefinitionIds
            .ToHashSet(StringComparer.Ordinal);
        return resources.Keys.All(physical.Contains) &&
            physical.All(resources.ContainsKey);
    }

    private static string ValidateResourceId(string resourceId)
    {
        if (!GameContentCatalog.IsStableId(resourceId) ||
            !resourceId.StartsWith("resource.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Invalid physical resource definition ID {resourceId}.");
        }

        return resourceId;
    }

    private static void ValidatePlacements(
        IReadOnlyList<CatalogResourcePlacement> placements)
    {
        if (placements.Select(placement => placement.ResourceNodeId)
            .Distinct(StringComparer.Ordinal).Count() != placements.Count)
        {
            throw new InvalidOperationException(
                "Generated resource field contains duplicate node IDs.");
        }

        if (placements.Select(placement => placement.ResourceDefinitionId)
            .Distinct(StringComparer.Ordinal).Count() != placements.Count)
        {
            throw new InvalidOperationException(
                "Generated resource field contains duplicate resource definitions.");
        }

        if (placements.Select(placement => (
                placement.PositionX,
                placement.PositionY,
                placement.PositionZ))
            .Distinct().Count() != placements.Count)
        {
            throw new InvalidOperationException(
                "Generated resource field contains overlapping placements.");
        }

        foreach (CatalogResourcePlacement placement in placements)
        {
            if (!GameContentCatalog.IsStableId(placement.ResourceNodeId) ||
                !double.IsFinite(placement.PositionX) ||
                !double.IsFinite(placement.PositionY) ||
                !double.IsFinite(placement.PositionZ))
            {
                throw new InvalidOperationException(
                    $"Invalid generated resource placement {placement.ResourceNodeId}.");
            }
        }
    }

    private static string ToPascalCase(string value)
    {
        return string.Concat(value
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }
}
