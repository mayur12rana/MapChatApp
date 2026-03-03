using System.Text.RegularExpressions;
using System.Xml.Linq;
using ChatMapperApp.Models;
using Newtonsoft.Json.Linq;

namespace ChatMapperApp.Services;

/// <summary>
/// Extracts data by directly finding nodes/elements via path.
/// No regex — the source path points directly to the value.
/// </summary>
public static class MappingService
{
    public static List<ChatMessage> ExtractMessages(
        string rawContent,
        FileFormat format,
        string startNodePath,
        string endNodePath,
        IReadOnlyList<FieldMapping> mappings)
    {
        return format switch
        {
            FileFormat.Json => ExtractFromJson(rawContent, startNodePath, mappings),
            FileFormat.Xml or FileFormat.Html => ExtractFromXml(rawContent, startNodePath, mappings),
            FileFormat.Csv or FileFormat.Tsv => ExtractFromCsv(rawContent, mappings, format == FileFormat.Tsv),
            _ => new List<ChatMessage>(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    //  JSON — navigate by path, directly read value
    // ═══════════════════════════════════════════════════════════════════

    private static List<ChatMessage> ExtractFromJson(
        string content, string startPath, IReadOnlyList<FieldMapping> mappings)
    {
        var messages = new List<ChatMessage>();
        var root = JToken.Parse(content);

        // Resolve the array of repeating items
        var items = ResolveJsonArray(root, startPath);
        if (items == null) return messages;

        // For each item, figure out the relative field paths
        // startPath e.g. "$.messages" → items are at $.messages[0], $.messages[1], ...
        // mapping source e.g. "$.messages[0].text" → relative key = "text"
        var activeMappings = mappings
            .Where(m => m.IsEnabled && m.IsMapped && !string.IsNullOrWhiteSpace(m.SourcePath))
            .ToList();

        foreach (var item in items)
        {
            if (item is not JObject obj && item is not JValue) continue;
            var msg = new ChatMessage();

            foreach (var mapping in activeMappings)
            {
                var relativeKey = GetRelativeJsonKey(mapping.SourcePath, startPath);
                var value = ReadJsonValue(item, relativeKey);

                if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(mapping.DefaultValue))
                    value = mapping.DefaultValue;

                SetField(msg, mapping.TargetField, value);
            }

            if (!IsEmpty(msg)) messages.Add(msg);
        }

        return messages;
    }

    private static IEnumerable<JToken>? ResolveJsonArray(JToken root, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "$")
        {
            // Root is the array itself
            if (root is JArray arr) return arr;
            return null;
        }

        // Navigate down: "$.messages" → root["messages"]
        var clean = path.TrimStart('$', '.');
        JToken? current = root;

        foreach (var seg in SplitPath(clean))
        {
            if (current == null) return null;

            var indexMatch = Regex.Match(seg, @"^(.+?)\[(\d+)\]$");
            if (indexMatch.Success)
            {
                current = current[indexMatch.Groups[1].Value];
                if (current is JArray a)
                {
                    var idx = int.Parse(indexMatch.Groups[2].Value);
                    current = idx < a.Count ? a[idx] : null;
                }
                else return null;
            }
            else if (Regex.IsMatch(seg, @"^\[\d+\]$"))
            {
                var idx = int.Parse(seg.Trim('[', ']'));
                if (current is JArray a && idx < a.Count)
                    current = a[idx];
                else return null;
            }
            else
            {
                current = current[seg];
            }
        }

        if (current is JArray finalArr) return finalArr;
        if (current is JObject) return new[] { current };
        return null;
    }

    /// <summary>
    /// Given a full source path like "$.messages[0].user_profile.real_name"
    /// and a start path like "$.messages", extract the relative key: "user_profile.real_name"
    /// </summary>
    private static string GetRelativeJsonKey(string sourcePath, string startPath)
    {
        var src = sourcePath.TrimStart('$', '.');
        var start = startPath.TrimStart('$', '.');

        if (src.StartsWith(start, StringComparison.OrdinalIgnoreCase))
        {
            var relative = src[start.Length..];
            // Strip leading [0]. or [0] or .
            relative = Regex.Replace(relative, @"^\[\d+\]\.?", "");
            relative = relative.TrimStart('.');
            return relative;
        }

        return src;
    }

    /// <summary>
    /// Read a value from a JToken by navigating a dotted relative path.
    /// Handles nested objects like "user_profile.real_name".
    /// </summary>
    private static string? ReadJsonValue(JToken item, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return item.Type == JTokenType.Object ? null : item.ToString();

        JToken? current = item;
        foreach (var part in SplitPath(relativePath))
        {
            if (current == null) return null;

            var idxMatch = Regex.Match(part, @"^(.+?)\[(\d+)\]$");
            if (idxMatch.Success)
            {
                current = current[idxMatch.Groups[1].Value];
                if (current is JArray a)
                {
                    var idx = int.Parse(idxMatch.Groups[2].Value);
                    current = idx < a.Count ? a[idx] : null;
                }
            }
            else if (Regex.IsMatch(part, @"^\[\d+\]$"))
            {
                var idx = int.Parse(part.Trim('[', ']'));
                if (current is JArray a && idx < a.Count)
                    current = a[idx];
                else return null;
            }
            else
            {
                current = current[part];
            }
        }

        if (current == null) return null;
        if (current.Type == JTokenType.Object || current.Type == JTokenType.Array)
            return current.ToString(Newtonsoft.Json.Formatting.None);

        return current.ToString();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  XML — navigate by path, directly read element text / attribute
    // ═══════════════════════════════════════════════════════════════════

    private static List<ChatMessage> ExtractFromXml(
        string content, string startPath, IReadOnlyList<FieldMapping> mappings)
    {
        var messages = new List<ChatMessage>();
        var doc = XDocument.Parse(content);

        var elements = ResolveXmlElements(doc, startPath);
        if (elements == null) return messages;

        var activeMappings = mappings
            .Where(m => m.IsEnabled && m.IsMapped && !string.IsNullOrWhiteSpace(m.SourcePath))
            .ToList();

        foreach (var element in elements)
        {
            var msg = new ChatMessage();

            foreach (var mapping in activeMappings)
            {
                var relKey = GetRelativeXmlKey(mapping.SourcePath, startPath);
                var value = ReadXmlValue(element, relKey);

                if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(mapping.DefaultValue))
                    value = mapping.DefaultValue;

                SetField(msg, mapping.TargetField, value);
            }

            if (!IsEmpty(msg)) messages.Add(msg);
        }

        return messages;
    }

    private static IEnumerable<XElement>? ResolveXmlElements(XDocument doc, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || doc.Root == null)
            return doc.Root?.Elements();

        var parts = path.Trim('/').Split('/');
        XElement? current = doc.Root;

        // Skip the root element name if it matches the first part
        int startIdx = parts[0].Equals(doc.Root.Name.LocalName, StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        // Navigate to the parent of the repeating element
        for (int i = startIdx; i < parts.Length - 1; i++)
        {
            if (current == null) return null;
            var name = parts[i].Split('[')[0];
            current = current.Elements()
                .FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        if (current == null) return null;

        var lastPart = parts.Last().Split('[')[0];
        return current.Elements()
            .Where(e => e.Name.LocalName.Equals(lastPart, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetRelativeXmlKey(string sourcePath, string startPath)
    {
        if (sourcePath.StartsWith(startPath, StringComparison.OrdinalIgnoreCase))
        {
            var rel = sourcePath[startPath.Length..].TrimStart('/');
            // Strip index like [0]/
            rel = Regex.Replace(rel, @"^\[\d+\]/", "");
            rel = Regex.Replace(rel, @"^\[\d+\]$", "");
            return rel;
        }
        return sourcePath.Trim('/');
    }

    private static string? ReadXmlValue(XElement element, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return element.Value.Trim();

        // Attribute reference: @AttrName
        if (relativePath.StartsWith("@"))
            return element.Attribute(relativePath[1..])?.Value;

        var parts = relativePath.Split('/');
        XElement? current = element;

        foreach (var part in parts)
        {
            if (current == null) return null;

            if (part.StartsWith("@"))
                return current.Attribute(part[1..])?.Value;

            var name = part.Split('[')[0];
            current = current.Elements()
                .FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        return current?.Value.Trim();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CSV — direct column mapping
    // ═══════════════════════════════════════════════════════════════════

    private static List<ChatMessage> ExtractFromCsv(
        string content, IReadOnlyList<FieldMapping> mappings, bool isTsv)
    {
        var messages = new List<ChatMessage>();
        var delimiter = isTsv ? '\t' : DetectDelimiter(content);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return messages;

        var headers = SplitCsv(lines[0], delimiter).Select(h => h.Trim().Trim('"')).ToArray();

        var activeMappings = mappings
            .Where(m => m.IsEnabled && m.IsMapped && !string.IsNullOrWhiteSpace(m.SourcePath))
            .ToList();

        for (int r = 1; r < lines.Length; r++)
        {
            var cols = SplitCsv(lines[r], delimiter).Select(c => c.Trim().Trim('"')).ToArray();
            var msg = new ChatMessage();

            foreach (var mapping in activeMappings)
            {
                var colName = mapping.SourcePath
                    .Replace("$.columns.", "")
                    .Replace("$.rows[*].", "")
                    .Trim();
                // Remove index if present e.g. "$.columns[2]" → find by index
                var idxMatch = Regex.Match(mapping.SourcePath, @"\[(\d+)\]");

                string? value = null;
                // Try by column name
                var colIdx = Array.FindIndex(headers, h =>
                    h.Equals(colName, StringComparison.OrdinalIgnoreCase));
                if (colIdx >= 0 && colIdx < cols.Length)
                    value = cols[colIdx];
                // Try by index
                else if (idxMatch.Success)
                {
                    var idx = int.Parse(idxMatch.Groups[1].Value);
                    if (idx < cols.Length) value = cols[idx];
                }

                if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(mapping.DefaultValue))
                    value = mapping.DefaultValue;

                SetField(msg, mapping.TargetField, value);
            }

            if (!IsEmpty(msg)) messages.Add(msg);
        }

        return messages;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Shared
    // ═══════════════════════════════════════════════════════════════════

    private static void SetField(ChatMessage msg, string fieldName, string? value)
    {
        if (value == null) return;
        switch (fieldName)
        {
            case "MessageId": msg.MessageId = value; break;
            case "Timestamp": msg.Timestamp = value; break;
            case "SenderName": msg.SenderName = value; break;
            case "SenderId": msg.SenderId = value; break;
            case "RecipientName": msg.RecipientName = value; break;
            case "RecipientId": msg.RecipientId = value; break;
            case "MessageBody": msg.MessageBody = value; break;
            case "MessageType": msg.MessageType = value; break;
            case "ChannelName": msg.ChannelName = value; break;
            case "ChannelId": msg.ChannelId = value; break;
            case "ThreadId": msg.ThreadId = value; break;
            case "ParentMessageId": msg.ParentMessageId = value; break;
            case "HasAttachment": msg.HasAttachment = value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
            case "AttachmentNames": msg.AttachmentNames = value; break;
            case "SourcePlatform": msg.SourcePlatform = value; break;
            case "SourceFile": msg.SourceFile = value; break;
            case "IsEdited": msg.IsEdited = value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
            case "IsDeleted": msg.IsDeleted = value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
            case "ExtendedProperties": msg.ExtendedProperties = value; break;
        }
    }

    private static bool IsEmpty(ChatMessage msg) =>
        string.IsNullOrWhiteSpace(msg.MessageBody) &&
        string.IsNullOrWhiteSpace(msg.SenderName) &&
        string.IsNullOrWhiteSpace(msg.Timestamp);

    private static string[] SplitPath(string path)
    {
        var parts = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inBracket = false;

        foreach (char c in path)
        {
            if (c == '[') { if (sb.Length > 0) { parts.Add(sb.ToString()); sb.Clear(); } inBracket = true; sb.Append(c); }
            else if (c == ']') { sb.Append(c); parts.Add(sb.ToString()); sb.Clear(); inBracket = false; }
            else if (c == '.' && !inBracket) { if (sb.Length > 0) { parts.Add(sb.ToString()); sb.Clear(); } }
            else sb.Append(c);
        }
        if (sb.Length > 0) parts.Add(sb.ToString());
        return parts.ToArray();
    }

    private static char DetectDelimiter(string content)
    {
        var line = content.Split('\n')[0];
        var t = line.Count(c => c == '\t');
        var cm = line.Count(c => c == ',');
        if (t > cm) return '\t';
        return ',';
    }

    private static string[] SplitCsv(string line, char d)
    {
        if (d != ',') return line.Split(d);
        var r = new List<string>(); bool q = false; var sb = new System.Text.StringBuilder();
        foreach (char c in line)
        {
            if (c == '"') { q = !q; }
            else if (c == d && !q) { r.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        r.Add(sb.ToString());
        return r.ToArray();
    }
}
