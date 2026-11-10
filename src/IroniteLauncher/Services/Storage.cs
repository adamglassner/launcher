using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ironite.Launcher.Services;

/// <summary>
/// Where the launcher keeps things. These are the same folders launcher 2.x used, so play stats,
/// installed games and the player's own games carry over:
///   %APPDATA%\Ironite\Launcher       settings.json, CustomIcons\, WebGames\ (web game saves)
///   %LOCALAPPDATA%\Ironite\Launcher  Games\, Downloads\, releases.json, Covers\
/// </summary>
public static class AppPaths
{
    public static string DataDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ironite", "Launcher");

    public static string CacheDir { get; set; } = Environment.GetEnvironmentVariable("IRONITE_CACHE_DIR") is { Length: > 0 } c
        ? c
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ironite", "Launcher");

    public static string Settings => Path.Combine(DataDir, "settings.json");
    public static string CustomIcons => Path.Combine(DataDir, "CustomIcons");
    public static string WebGames => Path.Combine(DataDir, "WebGames");
    public static string Games => Path.Combine(CacheDir, "Games");
    public static string Downloads => Path.Combine(CacheDir, "Downloads");
    public static string ReleaseCache => Path.Combine(CacheDir, "releases.json");
    public static string CoverCache => Path.Combine(CacheDir, "Covers");
    public static string Log => Path.Combine(Path.GetTempPath(), "IroniteLauncher.log");
    /// <summary>The restart helper launcher 2.x leaves behind after updating.</summary>
    public static string OldUpdateHelper => Path.Combine(Path.GetTempPath(), "IroniteLauncherUpdate.exe");

    public static string InstallDir => AppContext.BaseDirectory.TrimEnd('\\', '/');
    public static string ExePath => Environment.ProcessPath ?? Path.Combine(InstallDir, "IroniteLauncher.exe");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(CacheDir);
    }

    /// <summary>Moves settings and games from the folders used before the launcher was called Ironite Launcher.</summary>
    public static void MigrateOldFolders()
    {
        foreach (var dir in new[] { DataDir, CacheDir })
        {
            var old = Path.Combine(Path.GetDirectoryName(dir)!, "Casual Game Compilation");
            if (!Directory.Exists(old) || Directory.Exists(dir)) continue;
            try { Directory.Move(old, dir); Services.Log.Info($"migrated {old} -> {dir}"); }
            catch (Exception ex) { Services.Log.Error($"migrate {old}", ex); }
        }
    }
}

/// <summary>Small JSON file store (System.Text.Json) with atomic writes.</summary>
public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // Like launcher 2.x (Go's omitempty for missing values), so an older launcher can read the file too.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static T Load<T>(string path, Func<T> fallback)
    {
        try
        {
            if (!File.Exists(path)) return fallback();
            var text = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(text)) return fallback();
            return JsonSerializer.Deserialize<T>(text, Options) ?? fallback();
        }
        catch (Exception ex)
        {
            // Keep the unreadable file so the player's data is never silently lost.
            Log.Error($"Could not read {path}; keeping a copy as .bad", ex);
            try { File.Copy(path, path + ".bad", overwrite: true); } catch { /* ignore */ }
            LastLoadError = $"{Path.GetFileName(path)} could not be read ({ex.Message}). A copy was saved as {Path.GetFileName(path)}.bad.";
            return fallback();
        }
    }

    /// <summary>Set when a file failed to parse, so the UI can tell the player.</summary>
    public static string? LastLoadError { get; set; }

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options), new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>The launcher's log: %TEMP%\IroniteLauncher.log (the same file launcher 2.x wrote).</summary>
public static class Log
{
    private static readonly object Gate = new();

    /// <summary>Starts a fresh log for this run (a second start appends instead).</summary>
    public static void Start(bool append)
    {
        try
        {
            lock (Gate)
            {
                if (!append && File.Exists(AppPaths.Log))
                    File.Move(AppPaths.Log, AppPaths.Log + ".old", overwrite: true);
            }
        }
        catch { /* ignore */ }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex == null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                var fi = new FileInfo(AppPaths.Log);
                if (fi.Exists && fi.Length > 2_000_000)
                    File.Move(AppPaths.Log, AppPaths.Log + ".old", overwrite: true);
                File.AppendAllText(AppPaths.Log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch { /* logging must never crash the app */ }
    }
}
