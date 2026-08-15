using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;

public enum GameLogLevel { Trace, Debug, Information, Warning, Error, Critical }
public enum GameLogCategory { BOOT, CONTENT, WORLDGEN, STREAMING, DATABASE, SAVE, PLAYER, SHIP, AI, QUEST, NETWORK, SERVER, PERFORMANCE, ERROR }

public sealed record StructuredGameLoggerDiagnostics(
    string SessionId,
    string LogPath,
    int EntriesWritten,
    int RedactedValues,
    IReadOnlyList<string> CategoriesSeen,
    bool Initialized);

public static class StructuredGameLogger
{
    private static readonly object Gate = new();
    private static readonly Regex SecretAssignment = new(
        @"(?i)\b(password|passwd|token|secret|authorization|bearer|cookie|api[_-]?key)\s*[:=]\s*[^\s;,]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly string[] SensitiveFieldTokens =
    {
        "password", "passwd", "token", "secret", "authorization",
        "bearer", "cookie", "apikey", "api_key", "sessioncookie",
        "email", "username", "user_name", "fullname", "full_name",
        "phone", "address"
    };

    private static bool _initialized;
    private static string _sessionId = string.Empty;
    private static string _logPath = string.Empty;
    private static string _systemInfo = string.Empty;
    private static string _scene = "unknown";
    private static long _worldSeed;
    private static string _worldObject = "none";
    private static int _entriesWritten;
    private static int _redactedValues;
    private static readonly List<string> PendingLines = new();
    private static readonly HashSet<string> CategoriesSeen = new(StringComparer.Ordinal);

    public static void EnsureInitialized(SceneTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        lock (Gate)
        {
            if (_initialized) return;
            string directory = ProjectSettings.GlobalizePath("user://logs");
            Directory.CreateDirectory(directory);
            _sessionId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            _logPath = Path.Combine(
                directory,
                $"project-horizon-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{_sessionId[..8]}.jsonl");
            _systemInfo = string.Join(
                "; ",
                $"os={OS.GetName()}",
                $"osVersion={OS.GetVersion()}",
                $"dotnet={Environment.Version}",
                $"cpu={Environment.ProcessorCount}");
            _scene = tree.CurrentScene?.SceneFilePath ?? "bootstrap";
            _initialized = true;
        }
    }

    public static void UpdateContext(string scene, long worldSeed, string worldObject)
    {
        lock (Gate)
        {
            _scene = string.IsNullOrWhiteSpace(scene) ? "unknown" : scene;
            _worldSeed = worldSeed;
            _worldObject = string.IsNullOrWhiteSpace(worldObject) ? "none" : worldObject;
        }
    }

    public static void Log(
        GameLogLevel level,
        GameLogCategory category,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? fields = null)
    {
        lock (Gate)
        {
            if (!_initialized || string.IsNullOrWhiteSpace(_logPath)) return;
            Dictionary<string, object?> safeFields = new(StringComparer.Ordinal);
            if (fields is not null)
            {
                foreach ((string key, object? value) in fields)
                {
                    if (IsSensitiveField(key))
                    {
                        safeFields[key] = "[REDACTED]";
                        _redactedValues++;
                    }
                    else
                    {
                        safeFields[key] = value is string text ? Redact(text) : value;
                    }
                }
            }
            string? exceptionText = exception is null
                ? null
                : Redact($"{exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}");
            var entry = new
            {
                timestampUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                level = level.ToString(),
                category = category.ToString(),
                sessionId = _sessionId,
                message = Redact(message ?? string.Empty),
                exception = exceptionText,
                system = _systemInfo,
                scene = _scene,
                worldSeed = _worldSeed,
                worldObject = _worldObject,
                fields = safeFields
            };
            PendingLines.Add(JsonSerializer.Serialize(entry));
            CategoriesSeen.Add(category.ToString());
        }
    }

    /// <summary>Flushes all queued telemetry records to the JSONL file as one batch.</summary>
    public static void FlushPending()
    {
        lock (Gate)
        {
            if (!_initialized || string.IsNullOrWhiteSpace(_logPath) || PendingLines.Count == 0)
            {
                return;
            }
            string payload = string.Join(Environment.NewLine, PendingLines) + Environment.NewLine;
            File.AppendAllText(_logPath, payload);
            _entriesWritten += PendingLines.Count;
            PendingLines.Clear();
        }
    }

    public static StructuredGameLoggerDiagnostics GetDiagnostics()
    {
        lock (Gate)
        {
            return new StructuredGameLoggerDiagnostics(
                _sessionId,
                _logPath,
                _entriesWritten,
                _redactedValues,
                CategoriesSeen.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                _initialized);
        }
    }

    private static bool IsSensitiveField(string key)
    {
        string normalized = key.Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();
        return SensitiveFieldTokens.Any(token => normalized.Contains(token, StringComparison.Ordinal));
    }

    private static string Redact(string value)
    {
        string safe = SecretAssignment.Replace(value, match =>
        {
            _redactedValues++;
            int split = match.Value.IndexOfAny(new[] { ':', '=' });
            string prefix = split < 0 ? "secret" : match.Value[..split];
            return prefix + "=[REDACTED]";
        });

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile) && safe.Contains(userProfile, StringComparison.OrdinalIgnoreCase))
        {
            safe = safe.Replace(userProfile, "[USER_HOME]", StringComparison.OrdinalIgnoreCase);
            _redactedValues++;
        }
        string userName = Environment.UserName;
        if (!string.IsNullOrWhiteSpace(userName) && userName.Length >= 3 && safe.Contains(userName, StringComparison.OrdinalIgnoreCase))
        {
            safe = safe.Replace(userName, "[USER]", StringComparison.OrdinalIgnoreCase);
            _redactedValues++;
        }
        return safe;
    }
}
