using System.Text.Json.Serialization;

namespace DraftPilot.Core.Lcu.Models;

/// <summary>One rune page as the client reports it (<c>/lol-perks/v1/pages</c>).</summary>
/// <remarks>
/// The string and list properties coalesce <see langword="null"/> in their setters: the LCU is
/// known to send explicit nulls, and System.Text.Json writes those over any initializer default.
/// </remarks>
public sealed class LcuRunePage
{
    private string _name = string.Empty;
    private List<int> _selectedPerkIds = [];

    public long Id { get; set; }

    public string Name
    {
        get => _name;
        set => _name = value ?? string.Empty;
    }

    /// <summary>Currently selected page.</summary>
    public bool Current { get; set; }

    /// <summary>False for the preset pages Riot ships; those occupy no owned slot.</summary>
    public bool IsEditable { get; set; }

    /// <summary>
    /// Whether DELETE would succeed on this page. Null when the client did not send the field —
    /// then <see cref="IsEditable"/> is the best available answer (see <see cref="CanDelete"/>).
    /// </summary>
    public bool? IsDeletable { get; set; }

    /// <summary>Last-modified timestamp in Unix milliseconds; 0 when the client omits it.</summary>
    public long LastModified { get; set; }

    public int PrimaryStyleId { get; set; }

    public int SubStyleId { get; set; }

    public List<int> SelectedPerkIds
    {
        get => _selectedPerkIds;
        set => _selectedPerkIds = value ?? [];
    }

    /// <summary>Whether the import may delete this page at all.</summary>
    [JsonIgnore]
    public bool CanDelete => IsDeletable ?? IsEditable;
}

/// <summary>Body for creating a page (<c>POST /lol-perks/v1/pages</c>).</summary>
public sealed class LcuRunePageRequest
{
    public string Name { get; set; } = string.Empty;

    public int PrimaryStyleId { get; set; }

    public int SubStyleId { get; set; }

    public List<int> SelectedPerkIds { get; set; } = [];

    public bool Current { get; set; } = true;
}

/// <summary>The slice of <c>/lol-perks/v1/inventory</c> the import needs.</summary>
public sealed class LcuPerksInventory
{
    public int OwnedPageCount { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(List<LcuRunePage>))]
[JsonSerializable(typeof(LcuRunePageRequest))]
[JsonSerializable(typeof(LcuPerksInventory))]
[JsonSerializable(typeof(long))]
public sealed partial class LcuPerksJson : JsonSerializerContext;
