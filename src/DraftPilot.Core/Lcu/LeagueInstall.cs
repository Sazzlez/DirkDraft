using System.Text.Json;

namespace DraftPilot.Core.Lcu;

/// <summary>
/// Finds the League of Legends install directory without WMI or extra dependencies, by reading
/// the metadata the Riot Client itself writes.
/// </summary>
public static class LeagueInstall
{
    private const string ProductSettingsRelative =
        @"Riot Games\Metadata\league_of_legends.live\league_of_legends.live.product_settings.yaml";

    private const string InstallManifestRelative = @"Riot Games\RiotClientInstalls.json";

    private const string InstallPathKey = "product_install_full_path:";

    /// <summary>
    /// Returns the install directory, or <see langword="null"/> if League cannot be located.
    /// Tries the Riot product settings first, then the install manifest, then common defaults.
    /// </summary>
    public static string? Locate()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        return FromProductSettings(Path.Combine(programData, ProductSettingsRelative))
            ?? FromInstallManifest(Path.Combine(programData, InstallManifestRelative))
            ?? FromDefaults();
    }

    /// <summary>The lockfile path for a given install directory.</summary>
    public static string LockfilePath(string installDirectory) => Path.Combine(installDirectory, "lockfile");

    private static string? FromProductSettings(string path)
    {
        foreach (var line in ReadLinesSafe(path))
        {
            var index = line.IndexOf(InstallPathKey, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                continue;

            var value = line[(index + InstallPathKey.Length)..].Trim().Trim('"');
            if (Directory.Exists(value))
                return Path.GetFullPath(value);
        }

        return null;
    }

    private static string? FromInstallManifest(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("associated_client", out var associated))
                return null;

            foreach (var entry in associated.EnumerateObject())
            {
                if (!entry.Name.Contains("League of Legends", StringComparison.OrdinalIgnoreCase))
                    continue;

                var directory = entry.Name.TrimEnd('/', '\\');
                if (Directory.Exists(directory))
                    return Path.GetFullPath(directory);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    private static string? FromDefaults()
    {
        string[] suffixes =
        [
            @":\Riot Games\League of Legends",
            @":\Program Files\Riot Games\League of Legends",
            @":\Games\Riot Games\League of Legends",
        ];

        foreach (var drive in new[] { "C", "D", "E" })
        {
            foreach (var suffix in suffixes)
            {
                var candidate = drive + suffix;
                if (Directory.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static string[] ReadLinesSafe(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllLines(path) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
