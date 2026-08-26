using System.Globalization;

namespace DraftPilot.Meta.OpGg;

public enum OpGgNodeKind
{
    /// <summary>The field was not in the payload at all.</summary>
    Missing,

    /// <summary>An explicit null.</summary>
    Null,

    Object,
    List,
    String,
    Number,
    Boolean,
}

/// <summary>
/// One node of an OP.GG response. The API answers in a compact typed-tuple format rather than
/// JSON, so this is the tree it gets parsed into: objects carry a type name and named fields,
/// lists carry items, and leaves carry a string, number or boolean.
/// <para>
/// Every accessor is forgiving. A field the API left out — it omits fields whose data is missing
/// rather than sending nulls — yields <see cref="Missing"/>, which reads as absent all the way
/// down instead of throwing.
/// </para>
/// </summary>
public sealed class OpGgNode
{
    private static readonly OpGgNode[] NoItems = [];

    private readonly Dictionary<string, OpGgNode>? _fields;
    private readonly string? _text;
    private readonly double _number;
    private readonly bool _boolean;

    private OpGgNode(
        OpGgNodeKind kind,
        string? typeName = null,
        Dictionary<string, OpGgNode>? fields = null,
        IReadOnlyList<OpGgNode>? items = null,
        string? text = null,
        double number = 0,
        bool boolean = false)
    {
        Kind = kind;
        TypeName = typeName;
        _fields = fields;
        Items = items ?? NoItems;
        _text = text;
        _number = number;
        _boolean = boolean;
    }

    /// <summary>A node that is not present. Indexing it keeps returning itself.</summary>
    public static OpGgNode Missing { get; } = new(OpGgNodeKind.Missing);

    /// <summary>An explicit null in the payload: present, but carrying no value.</summary>
    public static OpGgNode Null { get; } = new(OpGgNodeKind.Null);

    public OpGgNodeKind Kind { get; }

    /// <summary>True for anything other than <see cref="OpGgNodeKind.Missing"/>.</summary>
    public bool Exists => Kind != OpGgNodeKind.Missing;

    /// <summary>True when there is a usable value: present and not null.</summary>
    public bool HasValue => Kind is not (OpGgNodeKind.Missing or OpGgNodeKind.Null);

    /// <summary>The constructor name, e.g. <c>Top</c> or <c>Synergie</c>. Null for leaves and lists.</summary>
    public string? TypeName { get; }

    /// <summary>List entries; empty for every other kind.</summary>
    public IReadOnlyList<OpGgNode> Items { get; }

    /// <summary>Field access by name; unknown names yield <see cref="Missing"/>.</summary>
    public OpGgNode this[string field]
        => _fields is not null && _fields.TryGetValue(field, out var value) ? value : Missing;

    /// <summary>The string value, or <see langword="null"/> for anything that is not a string.</summary>
    public string? AsText() => Kind == OpGgNodeKind.String ? _text : null;

    public double AsNumber(double fallback = 0) => Kind switch
    {
        OpGgNodeKind.Number => _number,
        OpGgNodeKind.Boolean => _boolean ? 1 : 0,
        // OP.GG sends the odd numeric value quoted; accept it rather than silently reading zero.
        OpGgNodeKind.String when double.TryParse(_text, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => fallback,
    };

    public int AsInt(int fallback = 0)
    {
        var value = AsNumber(double.NaN);
        return double.IsNaN(value) ? fallback : (int)Math.Round(value);
    }

    public bool AsBool(bool fallback = false) => Kind switch
    {
        OpGgNodeKind.Boolean => _boolean,
        OpGgNodeKind.Number => _number != 0,
        _ => fallback,
    };

    internal static OpGgNode Object(string typeName, Dictionary<string, OpGgNode> fields)
        => new(OpGgNodeKind.Object, typeName: typeName, fields: fields);

    internal static OpGgNode List(IReadOnlyList<OpGgNode> items)
        => new(OpGgNodeKind.List, items: items);

    internal static OpGgNode String(string value) => new(OpGgNodeKind.String, text: value);

    internal static OpGgNode Numeric(double value) => new(OpGgNodeKind.Number, number: value);

    internal static OpGgNode Bool(bool value) => new(OpGgNodeKind.Boolean, boolean: value);
}
