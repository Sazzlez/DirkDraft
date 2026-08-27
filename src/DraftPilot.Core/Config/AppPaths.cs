namespace DraftPilot.Core.Config;

/// <summary>
/// Where the tool keeps its files. Generated data goes under LocalAppData rather than next to the
/// executable, so the update button works no matter where the program is installed — including
/// under Program Files, where the program directory is not writable.
/// </summary>
public static class AppPaths
{
    private const string ProductFolder = "DraftPilot";

    /// <summary>Generated data: the meta snapshot and the champion icon cache.</summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductFolder, "data");

    /// <summary>User settings and manual overrides.</summary>
    public static string SettingsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ProductFolder);

    /// <summary>Read-only files shipped alongside the executable.</summary>
    public static string BundledDataDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "data");

    public static string SnapshotPath => Path.Combine(DataDirectory, "snapshot.json");

    public static string IconDirectory => Path.Combine(DataDirectory, "icons");

    /// <summary>Item icons, one PNG per item id, fetched on demand with a build plan.</summary>
    public static string ItemIconDirectory => Path.Combine(DataDirectory, "items");

    /// <summary>Rune icons, one PNG per perk id, fetched with the big update.</summary>
    public static string RuneIconDirectory => Path.Combine(DataDirectory, "runes");

    /// <summary>Summoner-spell icons, one PNG per spell id, fetched with the big update.</summary>
    public static string SpellIconDirectory => Path.Combine(DataDirectory, "spells");

    /// <summary>
    /// Localised display names for items, runes and spells, one file per language. The locale
    /// lands in a file name and comes from user-editable JSON, so it is validated here:
    /// "..\\evil" or "de:DE" must not leave the data directory or crash File.Create.
    /// </summary>
    public static string NamesPath(string language)
    {
        var safe = language.Length == 5
            && char.IsAsciiLetterLower(language[0]) && char.IsAsciiLetterLower(language[1])
            && language[2] == '_'
            && char.IsAsciiLetterUpper(language[3]) && char.IsAsciiLetterUpper(language[4]);

        return Path.Combine(DataDirectory, $"names-{(safe ? language : "de_DE")}.json");
    }

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static string OverridesPath => Path.Combine(SettingsDirectory, "overrides.json");

    /// <summary>Resolves a bundled data file, e.g. <c>champion_traits.json</c>.</summary>
    public static string Bundled(string fileName) => Path.Combine(BundledDataDirectory, fileName);

    public static void EnsureDataDirectory() => Directory.CreateDirectory(DataDirectory);

    public static void EnsureSettingsDirectory() => Directory.CreateDirectory(SettingsDirectory);
}
