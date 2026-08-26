using System.Text.Json;
using System.Text.Json.Serialization;

namespace DraftPilot.Core.Config;

/// <summary>User settings. Written on change, read once at start-up.</summary>
public sealed class AppSettings
{
    /// <summary>Window position; NaN means "not placed yet, centre on the working area".</summary>
    public double WindowLeft { get; set; } = double.NaN;

    public double WindowTop { get; set; } = double.NaN;

    public double WindowWidth { get; set; } = 430;

    public double WindowHeight { get; set; } = 790;

    /// <summary>Name of the active weighting preset.</summary>
    public string Preset { get; set; } = "Meta";

    /// <summary>Custom weights, used when <see cref="Preset"/> is <c>Eigene</c>.</summary>
    public double WeightTier { get; set; } = 1.0;

    public double WeightLaneMatchup { get; set; } = 1.0;

    public double WeightTeamMatchup { get; set; } = 0.4;

    public double WeightSynergy { get; set; } = 0.6;

    public double WeightComposition { get; set; } = 0.8;

    /// <summary>
    /// Show champions the account does not own. Off by default: a pick you cannot click is not a
    /// recommendation. Only ever applies to your own seat, since the client does not report what
    /// team-mates own.
    /// </summary>
    public bool ShowUnowned { get; set; }

    /// <summary>Bring the window up by itself when champion select starts.</summary>
    public bool AutoShowOnChampSelect { get; set; } = true;

    /// <summary>
    /// Hide the window into the notification area once champion select ends. Off by default: a window
    /// that vanishes reads as the tool having quit. It shrinks to a compact status card instead.
    /// </summary>
    public bool AutoHideAfterChampSelect { get; set; }

    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>The one-off "still running in the tray" balloon has been shown.</summary>
    public bool TrayHintShown { get; set; }

    /// <summary>How many entries the recommendation list shows.</summary>
    public int RecommendationCount { get; set; } = 8;

    /// <summary>
    /// Render the window on the CPU instead of the GPU. Saves loading the graphics driver into the
    /// process, which is the single largest block of mapped memory; the panel has nothing that
    /// animates, so there is nothing to gain from hardware rendering.
    /// </summary>
    public bool SoftwareRendering { get; set; } = true;

    /// <summary>Overrides lockfile discovery for non-standard installs.</summary>
    public string? LockfilePath { get; set; }

    /// <summary>OP.GG game mode for updates: <c>ranked</c> or <c>flex</c>.</summary>
    public string GameMode { get; set; } = "ranked";

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsPath))
                return new AppSettings();

            using var stream = File.OpenRead(AppPaths.SettingsPath);
            return JsonSerializer.Deserialize(stream, SettingsJson.Default.AppSettings) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt settings must not stop the tool from starting.
            return new AppSettings();
        }
    }

    /// <summary>Writes settings atomically; failures are silent because this is a convenience, not data.</summary>
    public void Save()
    {
        try
        {
            AppPaths.EnsureSettingsDirectory();
            var temporary = AppPaths.SettingsPath + ".tmp";

            using (var stream = File.Create(temporary))
            {
                JsonSerializer.Serialize(stream, this, SettingsJson.Default.AppSettings);
            }

            File.Move(temporary, AppPaths.SettingsPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing worth interrupting the user for.
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
public sealed partial class SettingsJson : JsonSerializerContext;
