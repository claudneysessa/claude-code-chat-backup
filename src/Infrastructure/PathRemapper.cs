using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeChatBackup.Infrastructure;

public sealed record PathMapping(string From, string To);

/// <summary>
/// Rewrites absolute paths (and account/org ids) inside Claude's JSON files.
/// A JSON string is rewritten only when the whole value starts with a mapped folder, so paths quoted
/// inside chat text are left as they were.
/// </summary>
public sealed class PathRemapper
{
    static readonly JsonSerializerOptions Compact = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    static readonly JsonSerializerOptions Indented = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };
    static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    readonly List<PathMapping> _paths;
    readonly Dictionary<string, string> _ids;
    readonly string[] _needles;

    public PathRemapper(IEnumerable<PathMapping> paths, IReadOnlyDictionary<string, string>? ids = null)
    {
        _paths = paths
            .Select(m => new PathMapping(TrimSeparator(m.From), TrimSeparator(m.To)))
            .Where(m => m.From.Length > 0 && !m.From.Equals(m.To, StringComparison.Ordinal))
            .DistinctBy(m => m.From, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(m => m.From.Length)
            .ToList();
        _ids = new Dictionary<string, string>(
            (ids ?? new Dictionary<string, string>()).Where(kv => !kv.Key.Equals(kv.Value, StringComparison.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);
        // How each mapped folder appears in raw JSON text, used to skip lines with nothing to rewrite.
        _needles = _paths.Select(m => m.From.Replace("\\", "\\\\")).Concat(_ids.Keys).ToArray();
    }

    public IReadOnlyList<PathMapping> Paths => _paths;
    public IReadOnlyDictionary<string, string> Ids => _ids;
    public bool IsEmpty => _paths.Count == 0 && _ids.Count == 0;

    static string TrimSeparator(string p) => p.Length > 3 ? p.TrimEnd('\\', '/') : p;

    /// <summary>The remapped path, or null when no mapping applies.</summary>
    public string? MapPath(string value)
    {
        foreach (var m in _paths)
        {
            if (!value.StartsWith(m.From, StringComparison.OrdinalIgnoreCase)) continue;
            if (value.Length == m.From.Length) return m.To;
            var next = value[m.From.Length];
            if (next is '\\' or '/' || m.From[^1] is '\\' or '/') return m.To + value[m.From.Length..];
        }
        return null;
    }

    string? MapString(string value, bool ids) =>
        MapPath(value) ?? (ids && _ids.TryGetValue(value, out var to) ? to : null);

    /// <summary>Rewrites strings and property names in place. Returns true when anything changed.</summary>
    public bool Remap(JsonNode? node, bool ids)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var changed = false;
                var props = obj.ToList();
                if (props.Any(p => MapString(p.Key, ids) is not null))
                {
                    obj.Clear();
                    foreach (var (key, value) in props) obj[MapString(key, ids) ?? key] = value;
                    changed = true;
                }
                foreach (var key in props.Select(p => MapString(p.Key, ids) ?? p.Key).ToList())
                {
                    var child = obj[key];
                    if (AsString(child) is { } s)
                    {
                        if (MapString(s, ids) is { } mapped) { obj[key] = mapped; changed = true; }
                    }
                    else changed |= Remap(child, ids);
                }
                return changed;
            }
            case JsonArray arr:
            {
                var changed = false;
                for (var i = 0; i < arr.Count; i++)
                {
                    if (AsString(arr[i]) is { } s)
                    {
                        if (MapString(s, ids) is { } mapped) { arr[i] = mapped; changed = true; }
                    }
                    else changed |= Remap(arr[i], ids);
                }
                return changed;
            }
            default:
                return false;
        }
    }

    static string? AsString(JsonNode? node) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    bool MayContainMatch(string text) =>
        _needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));

    /// <summary>Rewrites a whole JSON document. Unchanged documents are copied byte-for-byte.</summary>
    public void RemapJsonFile(Stream input, Stream output, bool ids)
    {
        using var ms = new MemoryStream();
        input.CopyTo(ms);
        var bytes = ms.ToArray();
        var text = Utf8.GetString(bytes).TrimStart('﻿');

        if (!IsEmpty && MayContainMatch(text))
        {
            JsonNode? root = null;
            try { root = JsonNode.Parse(text); }
            catch (JsonException) { }

            if (root is not null && Remap(root, ids))
            {
                var json = root.ToJsonString(text.Contains('\n') ? Indented : Compact);
                output.Write(Utf8.GetBytes(json));
                return;
            }
        }
        output.Write(bytes);
    }

    /// <summary>Rewrites a JSON Lines file line by line. Lines with nothing to change keep their exact bytes.</summary>
    public void RemapJsonLinesFile(Stream input, Stream output, bool ids)
    {
        if (IsEmpty)
        {
            input.CopyTo(output);
            return;
        }

        // ReadLine drops the line terminator, so remember whether the source ended with one.
        // Streams that cannot seek (zip entries) always do: the backup keeps complete lines only.
        var endsWithNewline = !input.CanSeek || input.Length == 0 || LastByte(input) == '\n';

        using var reader = new StreamReader(input, Utf8, detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 16);
        using var writer = new StreamWriter(output, Utf8, bufferSize: 1 << 16, leaveOpen: true);

        var line = reader.ReadLine();
        while (line is not null)
        {
            var next = reader.ReadLine();
            writer.Write(RemapLine(line, ids));
            if (next is not null || endsWithNewline) writer.Write('\n');
            line = next;
        }
    }

    static int LastByte(Stream s)
    {
        var pos = s.Position;
        s.Position = s.Length - 1;
        var b = s.ReadByte();
        s.Position = pos;
        return b;
    }

    string RemapLine(string line, bool ids)
    {
        if (line.Length == 0 || !MayContainMatch(line)) return line;
        try
        {
            var node = JsonNode.Parse(line);
            return node is not null && Remap(node, ids) ? node.ToJsonString(Compact) : line;
        }
        catch (JsonException)
        {
            return line;
        }
    }
}
