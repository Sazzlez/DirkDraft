using System.Globalization;

namespace DraftPilot.Meta.OpGg;

/// <summary>Thrown when a payload does not match the documented shape at all.</summary>
public sealed class OpGgParseException(string message) : Exception(message);

/// <summary>
/// Parses OP.GG's compact typed-tuple responses. A payload looks like this:
/// <code>
/// class LolListChampions: data
/// class Data: champions
/// class Champion: champion_id,key,name
///
/// LolListChampions(Data([Champion(1,"Annie","Annie"),Champion(2,"Olaf","Olaf")]))
/// </code>
/// The header block declares each constructor's field order, so arguments can be mapped onto
/// names. Structurally identical types reuse the first declared name — every lane list in the
/// tier list is emitted as <c>Top(...)</c> — which positional mapping handles naturally.
/// </summary>
public static class OpGgResponseParser
{
    private const string ClassPrefix = "class ";

    public static OpGgNode Parse(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new OpGgParseException("Leere Antwort.");

        var (schema, expression) = SplitHeader(payload);

        if (string.IsNullOrWhiteSpace(expression))
            throw new OpGgParseException("Antwort enthält keinen Datenausdruck.");

        var cursor = new Cursor(expression, schema);
        var value = cursor.ReadValue();
        cursor.SkipWhitespace();

        if (!cursor.AtEnd)
            throw new OpGgParseException($"Unerwarteter Inhalt ab Position {cursor.Position}.");

        return value;
    }

    /// <summary>
    /// Splits the <c>class …</c> declarations from the single expression that follows them.
    /// </summary>
    private static (Dictionary<string, string[]> Schema, string Expression) SplitHeader(string payload)
    {
        var schema = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var reader = new StringReader(payload);
        var expression = new List<string>();
        var inHeader = true;

        while (reader.ReadLine() is { } line)
        {
            if (inHeader)
            {
                if (line.Length == 0)
                    continue;

                if (line.StartsWith(ClassPrefix, StringComparison.Ordinal))
                {
                    AddClass(schema, line);
                    continue;
                }

                inHeader = false;
            }

            expression.Add(line);
        }

        return (schema, string.Join('\n', expression));
    }

    private static void AddClass(Dictionary<string, string[]> schema, string line)
    {
        var separator = line.IndexOf(':', StringComparison.Ordinal);
        if (separator < 0)
            return;

        var name = line[ClassPrefix.Length..separator].Trim();
        if (name.Length == 0)
            return;

        var fields = line[(separator + 1)..]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        // A repeated declaration would mean the payload redefined a type mid-stream; keep the first.
        schema.TryAdd(name, fields);
    }

    private ref struct Cursor(string text, Dictionary<string, string[]> schema)
    {
        private readonly string _text = text;
        private readonly Dictionary<string, string[]> _schema = schema;
        private int _index;

        public int Position => _index;

        public bool AtEnd => _index >= _text.Length;

        public void SkipWhitespace()
        {
            while (_index < _text.Length && char.IsWhiteSpace(_text[_index]))
                _index++;
        }

        /// <summary>Nesting level of the current ReadValue call, bounded below.</summary>
        private int _depth;

        /// <summary>
        /// Network input decides the recursion depth here, and a stack overflow is not catchable —
        /// it kills the process. Far above anything a real answer nests (~6 levels).
        /// </summary>
        private const int MaxDepth = 64;

        public OpGgNode ReadValue()
        {
            SkipWhitespace();

            if (AtEnd)
                throw new OpGgParseException("Ausdruck endet unerwartet.");

            if (_depth >= MaxDepth)
                throw new OpGgParseException($"Antwort ist tiefer als {MaxDepth} Ebenen verschachtelt.");

            var current = _text[_index];

            if (current == '[')
                return ReadList();

            if (current is '"' or '\'')
                return OpGgNode.String(ReadQuoted(current));

            if (current == '-' || char.IsDigit(current))
                return OpGgNode.Numeric(ReadNumber());

            if (char.IsLetter(current) || current == '_')
                return ReadIdentifierValue();

            throw new OpGgParseException($"Unerwartetes Zeichen '{current}' an Position {_index}.");
        }

        private OpGgNode ReadList()
        {
            _depth++;
            try
            {
            Expect('[');
            var items = new List<OpGgNode>();

            SkipWhitespace();
            if (Peek() == ']')
            {
                _index++;
                return OpGgNode.List(items);
            }

            while (true)
            {
                items.Add(ReadValue());
                SkipWhitespace();

                var next = Peek();
                if (next == ',')
                {
                    _index++;
                    continue;
                }

                if (next == ']')
                {
                    _index++;
                    return OpGgNode.List(items);
                }

                throw new OpGgParseException($"Liste nicht geschlossen an Position {_index}.");
            }
            }
            finally
            {
                _depth--;
            }
        }

        /// <summary>
        /// Reads either a constructor call or one of the bare literals the API uses for empty
        /// values (<c>null</c>, <c>None</c>, <c>true</c>, <c>false</c>).
        /// </summary>
        private OpGgNode ReadIdentifierValue()
        {
            var start = _index;
            while (_index < _text.Length && (char.IsLetterOrDigit(_text[_index]) || _text[_index] == '_'))
                _index++;

            var name = _text[start.._index];

            SkipWhitespace();
            if (Peek() != '(')
            {
                return name switch
                {
                    "null" or "None" => OpGgNode.Null,
                    "true" or "True" => OpGgNode.Bool(true),
                    "false" or "False" => OpGgNode.Bool(false),
                    // An unquoted word that is not a literal is still data; keep it as text.
                    _ => OpGgNode.String(name),
                };
            }

            return ReadConstructor(name);
        }

        private OpGgNode ReadConstructor(string typeName)
        {
            _depth++;
            try
            {
            Expect('(');

            var arguments = new List<OpGgNode>();

            SkipWhitespace();
            if (Peek() == ')')
            {
                _index++;
            }
            else
            {
                while (true)
                {
                    arguments.Add(ReadValue());
                    SkipWhitespace();

                    var next = Peek();
                    if (next == ',')
                    {
                        _index++;
                        continue;
                    }

                    if (next == ')')
                    {
                        _index++;
                        break;
                    }

                    throw new OpGgParseException($"Konstruktor '{typeName}' nicht geschlossen an Position {_index}.");
                }
            }

            var fields = _schema.TryGetValue(typeName, out var declared) ? declared : [];
            var mapped = new Dictionary<string, OpGgNode>(arguments.Count, StringComparer.Ordinal);

            for (var i = 0; i < arguments.Count; i++)
            {
                // Beyond the declared fields we can only address arguments positionally. That only
                // happens if OP.GG changes a type without updating its header, so keep the data
                // reachable instead of dropping it.
                var key = i < fields.Length ? fields[i] : i.ToString(CultureInfo.InvariantCulture);
                mapped[key] = arguments[i];
            }

            return OpGgNode.Object(typeName, mapped);
            }
            finally
            {
                _depth--;
            }
        }

        private string ReadQuoted(char quote)
        {
            Expect(quote);
            var builder = new System.Text.StringBuilder();

            while (_index < _text.Length)
            {
                var current = _text[_index++];

                if (current == '\\' && _index < _text.Length)
                {
                    var escaped = _text[_index++];
                    builder.Append(escaped switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        _ => escaped,
                    });
                    continue;
                }

                if (current == quote)
                    return builder.ToString();

                builder.Append(current);
            }

            throw new OpGgParseException("Zeichenkette nicht geschlossen.");
        }

        private double ReadNumber()
        {
            var start = _index;

            if (Peek() is '-' or '+')
                _index++;

            while (_index < _text.Length && (char.IsDigit(_text[_index]) || _text[_index] is '.' or 'e' or 'E' or '-' or '+'))
            {
                // Stop before a sign that belongs to the next token rather than an exponent.
                if (_text[_index] is '-' or '+' && _text[_index - 1] is not ('e' or 'E'))
                    break;

                _index++;
            }

            var slice = _text[start.._index];
            if (!double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw new OpGgParseException($"Zahl '{slice}' nicht lesbar an Position {start}.");

            return value;
        }

        private char Peek() => _index < _text.Length ? _text[_index] : '\0';

        private void Expect(char expected)
        {
            if (Peek() != expected)
                throw new OpGgParseException($"'{expected}' erwartet an Position {_index}.");

            _index++;
        }
    }
}
