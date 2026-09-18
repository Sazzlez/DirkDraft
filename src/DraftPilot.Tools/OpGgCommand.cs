using System.Text.Json;
using System.Text.Json.Nodes;
using DraftPilot.Meta.OpGg;

namespace DraftPilot.Tools;

/// <summary>
/// A window into OP.GG's MCP endpoint: which tools exist, which arguments they accept, and what a
/// given call actually answers.
/// <para>
/// Deliberately unopinionated. Every "can the source do X?" question in this project — rank
/// brackets, regions, ARAM, exact counters instead of rounded rates — used to be settled by reading
/// our own request code, which can only ever show what we already ask for. This command asks the
/// server instead, and changes nothing: no snapshot is written, no setting is touched.
/// </para>
/// </summary>
public static class OpGgCommand
{
    /// <summary>How much of a response goes to the console before it is worth a file instead.</summary>
    private const int PreviewLength = 3_000;

    public static async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        var verb = args.Length > 1 ? args[1].ToLowerInvariant() : "tools";

        using var client = new OpGgMcpClient();

        switch (verb)
        {
            case "tools":
                return await ListToolsAsync(client, args, ct);

            case "try":
                return await TryToolAsync(client, args, ct);

            default:
                Console.Error.WriteLine($"Unbekannter opgg-Unterbefehl: {verb}");
                PrintUsage();
                return 2;
        }
    }

    private static void PrintUsage() => Console.WriteLine("""
        opgg tools [name]        Werkzeugliste der Schnittstelle; mit Namen das vollstaendige
                                 Argument-Schema dieses Werkzeugs
        opgg try <werkzeug> [schluessel=wert ...] [--out datei.json]
                                 Werkzeug einmal aufrufen und die Antwort ansehen. Ein Wert mit
                                 @ davor ist eine Liste, z. B.
                                 desired_output_fields=@data.champions[].name,champion
        """);

    /// <summary>
    /// The catalogue. Prints one line per tool plus its argument names, because that is the level
    /// at which the open questions live; the full schema of a single tool is one argument away.
    /// </summary>
    private static async Task<int> ListToolsAsync(OpGgMcpClient client, string[] args, CancellationToken ct)
    {
        var wanted = args.Length > 2 ? args[2] : null;
        var tools = await client.ListToolsAsync(ct);

        if (tools.Count == 0)
        {
            Console.Error.WriteLine("Die Schnittstelle nennt keine Werkzeuge.");
            return 1;
        }

        if (wanted is not null)
        {
            var match = tools.FirstOrDefault(tool =>
                string.Equals(Name(tool), wanted, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                Console.Error.WriteLine($"Kein Werkzeug namens „{wanted}“. `opgg tools` listet alle.");
                return 2;
            }

            Console.WriteLine(match.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        Console.WriteLine($"{tools.Count} Werkzeuge:");
        Console.WriteLine();

        foreach (var tool in tools.OrderBy(Name, StringComparer.Ordinal))
        {
            Console.WriteLine(Name(tool));

            if (Describe(tool) is { Length: > 0 } description)
                Console.WriteLine($"    {description}");

            if (Arguments(tool) is { Length: > 0 } arguments)
                Console.WriteLine($"    Argumente: {arguments}");

            Console.WriteLine();
        }

        return 0;
    }

    /// <summary>
    /// One call, verbatim arguments, raw answer. The point is to see what comes back for a
    /// combination we do not use today — so nothing here filters, renames or interprets.
    /// </summary>
    private static async Task<int> TryToolAsync(OpGgMcpClient client, string[] args, CancellationToken ct)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("try braucht einen Werkzeugnamen.");
            PrintUsage();
            return 2;
        }

        var tool = args[2];
        var arguments = new JsonObject();
        string? outPath = null;

        for (var i = 3; i < args.Length; i++)
        {
            var argument = args[i];

            if (argument.Equals("--out", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("--out braucht einen Dateinamen.");
                    return 2;
                }

                outPath = args[++i];
                continue;
            }

            var separator = argument.IndexOf('=');
            if (separator <= 0)
            {
                Console.Error.WriteLine($"Argument „{argument}“ ist kein schluessel=wert.");
                return 2;
            }

            var key = argument[..separator];
            var value = argument[(separator + 1)..];

            // "@a,b,c" is a list. The shell eats the inner quotes of a real JSON array before the
            // process ever sees them, and the one argument that needs an array — the field list —
            // is exactly the one every probe uses.
            arguments[key] = value.StartsWith('@')
                ? List(value[1..])
                : TryParseJson(value) ?? JsonValue.Create(value);
        }

        Console.WriteLine($"{tool} {arguments.ToJsonString()}");

        string text;
        try
        {
            text = await client.CallToolRawAsync(tool, arguments, ct);
        }
        catch (OpGgApiException ex)
        {
            // The distinction the client draws is exactly the answer we came for: a rejected
            // combination is a "no", a transport failure says nothing about the combination.
            Console.Error.WriteLine(ex.Status is null
                ? $"Abgelehnt: {ex.Message}"
                : $"Verbindung: {ex.Message}");

            return 1;
        }

        Console.WriteLine($"{text.Length:N0} Zeichen Antwort.");

        if (outPath is not null)
        {
            await File.WriteAllTextAsync(outPath, text, ct);
            Console.WriteLine($"Vollstaendig geschrieben: {Path.GetFullPath(outPath)}");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine(text.Length <= PreviewLength ? text : text[..PreviewLength] + "\n… (gekuerzt, --out schreibt alles)");
        return 0;
    }

    private static JsonArray List(string value)
    {
        var array = new JsonArray();

        foreach (var entry in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            array.Add(entry);

        return array;
    }

    private static JsonNode? TryParseJson(string value)
    {
        try
        {
            return JsonNode.Parse(value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Name(JsonNode tool) => tool["name"]?.GetValue<string>() ?? "?";

    /// <summary>First line of the description; the full text is in the per-tool schema dump.</summary>
    private static string Describe(JsonNode tool)
    {
        var text = tool["description"]?.GetValue<string>() ?? string.Empty;
        var line = text.Split('\n', StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;

        return line.Length <= 140 ? line : line[..140] + "…";
    }

    /// <summary>Argument names, required ones marked — the level at which our open questions live.</summary>
    private static string Arguments(JsonNode tool)
    {
        if (tool["inputSchema"]?["properties"] is not JsonObject properties)
            return string.Empty;

        var required = tool["inputSchema"]?["required"] is JsonArray array
            ? array.Select(entry => entry?.GetValue<string>()).Where(name => name is not null).ToHashSet(StringComparer.Ordinal)
            : [];

        return string.Join(", ", properties.Select(property =>
            required.Contains(property.Key) ? property.Key + "*" : property.Key));
    }
}
