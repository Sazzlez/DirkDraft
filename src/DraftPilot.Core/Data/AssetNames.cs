using System.Text.Json;
using System.Text.Json.Serialization;
using DraftPilot.Core.Config;

namespace DraftPilot.Core.Data;

/// <summary>
/// Localised display names for items, runes and summoner spells, keyed by Riot's ids.
/// <para>
/// OP.GG's guide delivers English names; the shop and the rune screen in the user's client are
/// usually German. Written once per patch by the big update from Data Dragon, read at start-up.
/// </para>
/// </summary>
public sealed class AssetNames
{
    public string Patch { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public Dictionary<int, string> Items { get; set; } = [];

    public Dictionary<int, string> Runes { get; set; } = [];

    public Dictionary<int, string> Spells { get; set; } = [];

    /// <summary>A fresh instance per call, never a shared singleton: the dictionaries are mutable
    /// (the update merges into a loaded instance), and mutating a shared Empty would silently
    /// corrupt the fallback for every later caller.</summary>
    public static AssetNames Empty => new();

    public string Item(int id, string fallback) => Items.TryGetValue(id, out var name) ? name : fallback;

    public string Rune(int id, string fallback) => Runes.TryGetValue(id, out var name) ? name : fallback;

    public string Spell(int id, string fallback) => Spells.TryGetValue(id, out var name) ? name : fallback;

    public static AssetNames Load(string language)
    {
        try
        {
            var path = AppPaths.NamesPath(language);
            if (!File.Exists(path))
                return Empty;

            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, AssetNamesJson.Default.AssetNames) ?? Empty;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // English names from the guide remain as fallback.
            return Empty;
        }
    }

    /// <summary>Writes atomically; failures are the caller's warning, not an abort.</summary>
    public void Save()
    {
        AppPaths.EnsureDataDirectory();
        var path = AppPaths.NamesPath(Language);

        // Process-unique like the icon downloaders': app and CLI tool can both update.
        var temporary = $"{path}.{Environment.ProcessId}.tmp";

        using (var stream = File.Create(temporary))
        {
            JsonSerializer.Serialize(stream, this, AssetNamesJson.Default.AssetNames);
        }

        File.Move(temporary, path, overwrite: true);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AssetNames))]
public sealed partial class AssetNamesJson : JsonSerializerContext;
