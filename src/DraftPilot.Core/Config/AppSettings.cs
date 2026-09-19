using System.Text.Json;
using System.Text.Json.Serialization;

namespace DraftPilot.Core.Config;

/// <summary>User settings. Written on change, read once at start-up.</summary>
public sealed class AppSettings
{
    /// <summary>Window position; NaN means "not placed yet, park at the working area's edge".
    /// The size is fixed in XAML and deliberately not stored — see MainWindow.SavePlacement.</summary>
    public double WindowLeft { get; set; } = double.NaN;

    public double WindowTop { get; set; } = double.NaN;

    /// <summary>
    /// Show champions the account does not own. Off by default: a pick you cannot click is not a
    /// recommendation. Only ever applies to your own seat, since the client does not report what
    /// team-mates own.
    /// </summary>
    public bool ShowUnowned { get; set; }

    /// <summary>Bring the window up by itself when champion select starts.</summary>
    public bool AutoShowOnChampSelect { get; set; } = true;

    /// <summary>Bring the window up with the build view when the game starts.</summary>
    public bool AutoShowOnGameStart { get; set; } = true;

    /// <summary>Data Dragon locale for item, rune and spell names, matching the client's shop.</summary>
    public string DataLanguage { get; set; } = "de_DE";

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

    /// <summary>
    /// OP.GG game mode the stored snapshot is built for: <c>ranked</c>, <c>flex</c> or <c>aram</c>.
    /// <para>
    /// Only the update button reads this. Everything that happens during a draft — the live counter
    /// calls and the build — follows the queue the client reports, because that is a fact and this
    /// is a guess made before anybody knew what would be queued. The window says so when the two
    /// disagree.
    /// </para>
    /// </summary>
    public string GameMode { get; set; } = "ranked";

    /// <summary>
    /// Rank bracket the numbers should describe: <c>iron</c>, <c>bronze</c>, <c>silver</c>,
    /// <c>gold</c>, <c>platinum</c>, <c>emerald</c>, <c>diamond</c>, <c>master</c>, the aggregates
    /// <c>emerald_plus</c>, <c>platinum_plus</c>, <c>diamond_plus</c> — or <c>all</c> for every rank
    /// at once, which is what this defaults to and what it always sent.
    /// <para>
    /// A named bracket describes the games the player actually plays: measured on 2026-09-18, Darius
    /// Top has 100.734 games in gold and 88.193 in platinum, and the champions listed as his
    /// strongest counters differ between the two. It also makes the file consistent — with
    /// <c>all</c>, lane strength comes from OP.GG's default bracket and the duels from every rank,
    /// which are two different populations inside one score. Takes effect on the next data update.
    /// </para>
    /// </summary>
    public string Tier { get; set; } = "all";

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
        catch (Exception)
        {
            // Nothing worth interrupting the user for — Save is best-effort by contract. The
            // filter is deliberately everything: the historical crash here was an
            // ArgumentException (NaN in a double), not an IO error.
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    // WindowLeft/Top default to NaN ("not placed yet") — without this, serialising a settings
    // object before the first placement threw instead of writing.
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
public sealed partial class SettingsJson : JsonSerializerContext;
