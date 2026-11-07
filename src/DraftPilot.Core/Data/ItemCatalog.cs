using System.Text.Json;

namespace DraftPilot.Core.Data;

/// <summary>
/// What kind of buy an item is, read from Data Dragon's <c>item.json</c> of the patch the games
/// were played on. A purchase log only says "item 3006 at 4:12"; whether that was the boots
/// decision, a finished item or a component on the way to one is what turns a log into a build.
/// <para>
/// Rules, checked against 16.22.1:
/// <list type="bullet">
/// <item>Boots are the second tier: tagged <c>Boots</c> and built from the basic Boots (1001). The
/// third tier (Gunmetal Greaves and so on) builds from those, and is not always tagged <c>Boots</c>
/// itself — Gunmetal Greaves is not — so it is recognised by what it is built from.</item>
/// <item>A finished item builds into nothing purchasable any more and costs at least 1.000 gold.
/// Components always build into something, and every starter below 1.000 gold (Doran's, Cull,
/// Dark Seal) is cut by the price — while Manamune, which turns into a Muramana nobody can buy,
/// and Mejai's at 1.500 stay in.</item>
/// <item>The support quest ends in a choice built from Bounty of Worlds (3867); that choice is its
/// own decision and never one of the finished items.</item>
/// </list>
/// </para>
/// </summary>
public sealed class ItemCatalog
{
    /// <summary>Basic Boots, the one item every pair of boots is built from.</summary>
    public const int BasicBoots = 1001;

    /// <summary>The last free step of the support quest; the choice after it is the decision.</summary>
    public const int SupportQuestBase = 3867;

    private const int FinishedItemGold = 1000;

    private readonly Dictionary<int, Entry> _items;

    private ItemCatalog(Dictionary<int, Entry> items, string version)
    {
        _items = items;
        Version = version;
    }

    /// <summary>The Data Dragon version the catalog was read from, e.g. <c>16.22.1</c>.</summary>
    public string Version { get; }

    public int Count => _items.Count;

    public static ItemCatalog Parse(string itemJson)
    {
        using var document = JsonDocument.Parse(itemJson);
        var root = document.RootElement;

        var version = root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;

        var items = new Dictionary<int, Entry>();

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in data.EnumerateObject())
            {
                if (!int.TryParse(property.Name, out var id))
                    continue;

                var item = property.Value;
                items[id] = new Entry(
                    Name: item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() ?? string.Empty : string.Empty,
                    Gold: item.TryGetProperty("gold", out var gold) && gold.TryGetProperty("total", out var total) && total.TryGetInt32(out var price) ? price : 0,
                    Purchasable: !item.TryGetProperty("gold", out var g) || !g.TryGetProperty("purchasable", out var p) || p.ValueKind != JsonValueKind.False,
                    Tags: Strings(item, "tags"),
                    From: Ids(item, "from"),
                    Into: Ids(item, "into"),
                    Armor: Stat(item, "FlatArmorMod"),
                    MagicResist: Stat(item, "FlatSpellBlockMod"));
            }
        }

        return new ItemCatalog(items, version);
    }

    public string Name(int id) => _items.TryGetValue(id, out var entry) ? entry.Name : string.Empty;

    public bool Knows(int id) => _items.ContainsKey(id);

    /// <summary>A finished pair of boots: the second tier, the one actually chosen.</summary>
    public bool IsBoots(int id)
        => _items.TryGetValue(id, out var entry)
            && entry.Tags.Contains("Boots", StringComparer.OrdinalIgnoreCase)
            && entry.From.Contains(BasicBoots);

    /// <summary>The third tier, an upgrade of a pair of boots already counted.</summary>
    public bool IsBootsUpgrade(int id)
        => _items.TryGetValue(id, out var entry) && entry.From.Any(IsBoots);

    /// <summary>The support quest's final choice (Celestial Opposition, Solstice Sleigh, …).</summary>
    public bool IsSupportChoice(int id)
        => _items.TryGetValue(id, out var entry) && entry.From.Contains(SupportQuestBase);

    public bool IsTrinket(int id)
        => _items.TryGetValue(id, out var entry) && entry.Tags.Contains("Trinket", StringComparer.OrdinalIgnoreCase);

    /// <summary>A finished item: the slots a build is made of after the boots.</summary>
    public bool IsFinished(int id)
    {
        if (!_items.TryGetValue(id, out var entry))
            return false;

        if (entry.Gold < FinishedItemGold || id == BasicBoots || IsBoots(id) || IsBootsUpgrade(id) || IsSupportChoice(id))
            return false;

        if (entry.Tags.Contains("Consumable", StringComparer.OrdinalIgnoreCase) || IsTrinket(id))
            return false;

        // Built into something a player could still buy: a component, not an end point.
        return !entry.Into.Any(next => _items.TryGetValue(next, out var into) && into.Purchasable);
    }

    private static List<string> Strings(JsonElement item, string property)
        => item.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array
            ? [.. array.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString() ?? string.Empty)]
            : [];

    private static List<int> Ids(JsonElement item, string property)
        => [.. Strings(item, property).Select(text => int.TryParse(text, out var id) ? id : 0).Where(id => id > 0)];

    /// <summary>Armor the item gives (Data Dragon stats.FlatArmorMod), 0 when unknown.</summary>
    public int Armor(int id) => _items.TryGetValue(id, out var entry) ? entry.Armor : 0;

    /// <summary>Magic resist the item gives (stats.FlatSpellBlockMod), 0 when unknown.</summary>
    public int MagicResist(int id) => _items.TryGetValue(id, out var entry) ? entry.MagicResist : 0;

    /// <summary>Whether the item heals its owner from damage dealt: Riot's LifeSteal or SpellVamp tag.</summary>
    public bool IsSustain(int id)
        => _items.TryGetValue(id, out var entry)
            && entry.Tags.Any(tag => tag.Equals("LifeSteal", StringComparison.OrdinalIgnoreCase) || tag.Equals("SpellVamp", StringComparison.OrdinalIgnoreCase));

    /// <summary>What counts of an enemy's inventory for the situations: finished items and boots.</summary>
    public bool CountsForSituation(int id) => IsFinished(id) || IsBoots(id);

    private static int Stat(JsonElement item, string name)
        => item.TryGetProperty("stats", out var stats)
            && stats.ValueKind == JsonValueKind.Object
            && stats.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number)
                ? (int)Math.Round(number)
                : 0;

    private sealed record Entry(string Name, int Gold, bool Purchasable, List<string> Tags, List<int> From, List<int> Into, int Armor, int MagicResist);
}
