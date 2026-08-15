using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Godot;

public sealed record GameLocalizationDiagnostics(
    int LocaleCount,
    int KeyCount,
    bool KeyParity,
    int MissingValueCount,
    string ActiveLocale);

public static class GameLocalizationService
{
    public const string AutomaticLanguage = "auto";
    public const string EnglishLocale = "en";
    public const string RussianLocale = "ru";

    private const string EnglishPath = "res://Content/localization.en.json";
    private const string RussianPath = "res://Content/localization.ru.json";
    private const string TextKeyMeta = "_ph_loc_text_key";
    private const string TooltipKeyMeta = "_ph_loc_tooltip_key";

    private static readonly string[] Supported = { EnglishLocale, RussianLocale };
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Catalogs =
        new(StringComparer.OrdinalIgnoreCase);
    private static bool _initialized;
    private static string _activeLocale = EnglishLocale;
    private static bool _keyParity;
    private static int _missingValueCount;

    public static event Action<string>? LocaleChanged;

    public static IReadOnlyList<string> SupportedLocales => Supported;
    public static string ActiveLocale
    {
        get
        {
            EnsureInitialized();
            return _activeLocale;
        }
    }

    public static int KeyCount
    {
        get
        {
            EnsureInitialized();
            return Catalogs[EnglishLocale].Count;
        }
    }

    public static GameLocalizationDiagnostics Diagnostics
    {
        get
        {
            EnsureInitialized();
            return new GameLocalizationDiagnostics(
                Catalogs.Count,
                KeyCount,
                _keyParity,
                _missingValueCount,
                _activeLocale);
        }
    }

    public static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        Catalogs.Clear();
        Catalogs[EnglishLocale] = LoadCatalog(EnglishPath, EnglishLocale);
        Catalogs[RussianLocale] = LoadCatalog(RussianPath, RussianLocale);

        HashSet<string> english = Catalogs[EnglishLocale].Keys.ToHashSet(StringComparer.Ordinal);
        HashSet<string> russian = Catalogs[RussianLocale].Keys.ToHashSet(StringComparer.Ordinal);
        _keyParity = english.SetEquals(russian);
        _missingValueCount = Catalogs.Values.Sum(catalog =>
            catalog.Values.Count(string.IsNullOrWhiteSpace));
        if (!_keyParity || _missingValueCount != 0)
        {
            throw new InvalidOperationException(
                $"Localization catalog invalid: parity={(_keyParity ? 1 : 0)}; " +
                $"missingValues={_missingValueCount}; en={english.Count}; ru={russian.Count}.");
        }

        _initialized = true;
    }

    public static bool IsSupportedConfiguration(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return false;
        }
        return string.Equals(languageCode, AutomaticLanguage, StringComparison.OrdinalIgnoreCase) ||
            Supported.Any(locale => string.Equals(locale, languageCode, StringComparison.OrdinalIgnoreCase));
    }

    public static string ResolveConfiguredLocale(string? languageCode)
    {
        EnsureInitialized();
        if (string.Equals(languageCode, RussianLocale, StringComparison.OrdinalIgnoreCase))
        {
            return RussianLocale;
        }
        if (string.Equals(languageCode, EnglishLocale, StringComparison.OrdinalIgnoreCase))
        {
            return EnglishLocale;
        }

        string preferred = OS.GetLocaleLanguage();
        return preferred.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
            ? RussianLocale
            : EnglishLocale;
    }

    public static void ApplyConfiguredLanguage(string? languageCode, bool notify = true)
    {
        EnsureInitialized();
        string resolved = ResolveConfiguredLocale(languageCode);
        bool changed = !string.Equals(_activeLocale, resolved, StringComparison.OrdinalIgnoreCase);
        _activeLocale = resolved;
        TranslationServer.SetLocale(resolved);
        if (notify && changed)
        {
            LocaleChanged?.Invoke(resolved);
        }
    }

    public static void SetLocaleForAcceptance(string locale, bool notify = true)
    {
        EnsureInitialized();
        if (!Supported.Any(value => string.Equals(value, locale, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentOutOfRangeException(nameof(locale), locale, "Unsupported Project Horizon locale.");
        }
        bool changed = !string.Equals(_activeLocale, locale, StringComparison.OrdinalIgnoreCase);
        _activeLocale = locale.ToLowerInvariant();
        TranslationServer.SetLocale(_activeLocale);
        if (notify && changed)
        {
            LocaleChanged?.Invoke(_activeLocale);
        }
    }

    public static bool ContainsKey(string key)
    {
        EnsureInitialized();
        return Catalogs.Values.All(catalog => catalog.ContainsKey(key));
    }

    public static string Text(string key)
    {
        EnsureInitialized();
        if (Catalogs.TryGetValue(_activeLocale, out IReadOnlyDictionary<string, string>? active) &&
            active.TryGetValue(key, out string? value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
        if (Catalogs[EnglishLocale].TryGetValue(key, out string? fallback) &&
            !string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
        }
        return $"[MISSING:{key}]";
    }

    public static string TextForLocale(string locale, string key)
    {
        EnsureInitialized();
        return Catalogs.TryGetValue(locale, out IReadOnlyDictionary<string, string>? catalog) &&
            catalog.TryGetValue(key, out string? value)
                ? value
                : $"[MISSING:{key}]";
    }

    public static string Format(
        string key,
        params (string Name, object? Value)[] values)
    {
        string text = Text(key);
        foreach ((string name, object? value) in values)
        {
            string formatted = value switch
            {
                null => string.Empty,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty
            };
            text = text.Replace(
                "{" + name + "}",
                formatted,
                StringComparison.Ordinal);
        }
        return text;
    }

    public static IReadOnlyList<string> MissingKeys(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        EnsureInitialized();
        return keys
            .Where(key => !ContainsKey(key))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
    }

    public static void LocalizeControlTree(Node root)
    {
        ArgumentNullException.ThrowIfNull(root);
        EnsureInitialized();
        LocalizeNode(root);
        foreach (Node child in root.GetChildren())
        {
            LocalizeControlTree(child);
        }
    }

    private static void LocalizeNode(Node node)
    {
        if (node is Label label)
        {
            label.Text = ResolveControlText(label, label.Text);
            label.TooltipText = ResolveTooltip(label, label.TooltipText);
            return;
        }
        if (node is Button button)
        {
            button.Text = ResolveControlText(button, button.Text);
            button.TooltipText = ResolveTooltip(button, button.TooltipText);
            return;
        }
        if (node is LineEdit lineEdit)
        {
            lineEdit.PlaceholderText = ResolveControlText(lineEdit, lineEdit.PlaceholderText);
            lineEdit.TooltipText = ResolveTooltip(lineEdit, lineEdit.TooltipText);
            return;
        }
        if (node is Control control)
        {
            control.TooltipText = ResolveTooltip(control, control.TooltipText);
        }
    }

    private static string ResolveControlText(GodotObject control, string current)
    {
        string key = control.HasMeta(TextKeyMeta)
            ? control.GetMeta(TextKeyMeta).AsString()
            : current;
        if (!control.HasMeta(TextKeyMeta) && LooksLikeLocalizationKey(key) && ContainsKey(key))
        {
            control.SetMeta(TextKeyMeta, key);
        }
        return control.HasMeta(TextKeyMeta) ? Text(key) : current;
    }

    private static string ResolveTooltip(Control control, string current)
    {
        string key = control.HasMeta(TooltipKeyMeta)
            ? control.GetMeta(TooltipKeyMeta).AsString()
            : current;
        if (!control.HasMeta(TooltipKeyMeta) && LooksLikeLocalizationKey(key) && ContainsKey(key))
        {
            control.SetMeta(TooltipKeyMeta, key);
        }
        return control.HasMeta(TooltipKeyMeta) ? Text(key) : current;
    }

    private static bool LooksLikeLocalizationKey(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains('.', StringComparison.Ordinal) &&
        !value.Any(char.IsWhiteSpace);

    private static IReadOnlyDictionary<string, string> LoadCatalog(string path, string locale)
    {
        string json = FileAccess.GetFileAsString(path);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException($"Localization file {path} is empty or missing.");
        }
        LocalizationDocument? document = JsonSerializer.Deserialize<LocalizationDocument>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (document is null || document.SchemaVersion != 1 || document.Strings is null)
        {
            throw new InvalidOperationException($"Localization file {path} has invalid schema.");
        }
        Dictionary<string, string> normalized = new(StringComparer.Ordinal);
        foreach ((string key, string value) in document.Strings)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value) ||
                !normalized.TryAdd(key, value))
            {
                throw new InvalidOperationException(
                    $"Localization file {path} contains invalid/duplicate key {key} for {locale}.");
            }
        }
        return normalized;
    }

    private sealed class LocalizationDocument
    {
        public int SchemaVersion { get; set; }
        public Dictionary<string, string>? Strings { get; set; }
    }
}
