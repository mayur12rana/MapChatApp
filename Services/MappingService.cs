using System.Text.RegularExpressions;
using System.Xml.Linq;
using ChatMapperApp.Models;
using Newtonsoft.Json.Linq;

namespace ChatMapperApp.Services;

/// <summary>
/// Extracts data by directly finding nodes/elements via path.
/// No regex — the source path points directly to the value.
/// Also provides auto-discovery of source fields for two-level hierarchy.
/// </summary>
public static class MappingService
{
    // ═══════════════════════════════════════════════════════════════════
    //  MAIN ENTRY — routes to format-specific extractor
    // ═══════════════════════════════════════════════════════════════════

    public static List<ChatMessage> ExtractMessages(
        string rawContent,
        FileFormat format,
        string parentNodePath,
        IReadOnlyList<string> childNodePaths,
        HierarchyMode mode,
        IReadOnlyList<FieldMapping> mappings)
    {
        if (mode == HierarchyMode.TwoLevel && childNodePaths.Count > 0)
        {
            return format switch
            {
                FileFormat.Xml or FileFormat.Html => ExtractTwoLevelXml(rawContent, parentNodePath, childNodePaths, mappings),
                FileFormat.Json => ExtractTwoLevelJson(rawContent, parentNodePath, childNodePaths, mappings),
                _ => ExtractFlat(rawContent, format, parentNodePath, mappings),
            };
        }

        return ExtractFlat(rawContent, format, parentNodePath, mappings);
    }

    /// <summary>Flat (single-level) extraction — original behavior.</summary>
    private static List<ChatMessage> ExtractFlat(
        string rawContent, FileFormat format, string startPath, IReadOnlyList<FieldMapping> mappings)
    {
        return format switch
        {
            FileFormat.Json => ExtractFromJson(rawContent, startPath, mappings),
            FileFormat.Xml or FileFormat.Html => ExtractFromXml(rawContent, startPath, mappings),
            FileFormat.Csv or FileFormat.Tsv => ExtractFromCsv(rawContent, mappings, format == FileFormat.Tsv),
            _ => new List<ChatMessage>(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    //  AUTO-DISCOVERY — discover available source fields
    // ═══════════════════════════════════════════════════════════════════

    public static List<SourceNode> DiscoverSourceNodes(
        string rawContent, FileFormat format, string parentPath, string childPath)
    {
        return format switch
        {
            FileFormat.Xml or FileFormat.Html => DiscoverXmlSourceNodes(rawContent, parentPath, childPath),
            FileFormat.Json => DiscoverJsonSourceNodes(rawContent, parentPath, childPath),
            _ => new List<SourceNode>(),
        };
    }

    public static (int parentCount, int avgChildCount) CountItems(
        string rawContent, FileFormat format, string parentPath, string childPath)
    {
        try
        {
            return format switch
            {
                FileFormat.Xml or FileFormat.Html => CountXmlItems(rawContent, parentPath, childPath),
                FileFormat.Json => CountJsonItems(rawContent, parentPath, childPath),
                _ => (0, 0),
            };
        }
        catch { return (0, 0); }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  XML DISCOVERY
    // ═══════════════════════════════════════════════════════════════════

    private static List<SourceNode> DiscoverXmlSourceNodes(
        string content, string parentPath, string childPath)
    {
        var nodes = new List<SourceNode>();
        var doc = XDocument.Parse(content);

        var parentElements = ResolveXmlElements(doc, parentPath);
        if (parentElements == null) return nodes;

        var firstParent = parentElements.FirstOrDefault();
        if (firstParent == null) return nodes;

        // Extract child node name from childPath
        var childNodeName = childPath.Trim('/').Split('/').Last().Split('[')[0];

        // Collect ALL complex child element names (those with sub-elements) to exclude from parent-level discovery
        var complexChildNames = firstParent.Elements()
            .Where(e => e.HasElements || e.HasAttributes)
            .Select(e => e.Name.LocalName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Discover parent-level fields, excluding all complex child elements
        DiscoverXmlFields(firstParent, complexChildNames, SourceLevel.Parent, "", nodes);

        // Discover child-level fields
        var firstChild = firstParent.Elements()
            .FirstOrDefault(e => e.Name.LocalName.Equals(childNodeName, StringComparison.OrdinalIgnoreCase));
        if (firstChild != null)
        {
            DiscoverXmlFields(firstChild, null, SourceLevel.Child, "", nodes);
        }

        return nodes;
    }

    private static void DiscoverXmlFields(
        XElement element, HashSet<string>? excludeChildNames, SourceLevel level, string pathPrefix, List<SourceNode> nodes)
    {
        // Attributes
        foreach (var attr in element.Attributes())
        {
            var name = string.IsNullOrEmpty(pathPrefix) ? $"@{attr.Name.LocalName}" : $"{pathPrefix}/@{attr.Name.LocalName}";
            nodes.Add(new SourceNode
            {
                Name = name,
                Path = name,
                SampleValue = Truncate(attr.Value, 80),
                Level = level,
                NodeType = NodeType.Attribute,
                GroupName = pathPrefix,
            });
        }

        var groups = element.Elements().GroupBy(e => e.Name.LocalName).ToList();

        foreach (var group in groups)
        {
            var elName = group.Key;

            // Skip excluded child element names at the parent level
            if (excludeChildNames != null && excludeChildNames.Contains(elName))
                continue;

            var first = group.First();

            if (first.HasElements)
            {
                // Nested element with children — recurse with path prefix
                var nestedPrefix = string.IsNullOrEmpty(pathPrefix) ? elName : $"{pathPrefix}/{elName}";
                var repeatCount = group.Count();

                // Discover nested fields from first instance
                DiscoverXmlFields(first, null, level, nestedPrefix, nodes);

                // Tag repeat count on discovered nodes in this group and all descendants
                if (repeatCount > 1)
                {
                    foreach (var n in nodes.Where(n =>
                        n.GroupName.Equals(nestedPrefix, StringComparison.OrdinalIgnoreCase) ||
                        n.GroupName.StartsWith(nestedPrefix + "/", StringComparison.OrdinalIgnoreCase)))
                        n.RepeatCount = repeatCount;
                }
            }
            else
            {
                // Leaf element
                var name = string.IsNullOrEmpty(pathPrefix) ? elName : $"{pathPrefix}/{elName}";
                var val = first.Value.Trim();
                nodes.Add(new SourceNode
                {
                    Name = name,
                    Path = name,
                    SampleValue = Truncate(val, 80),
                    Level = level,
                    NodeType = NodeType.Value,
                    GroupName = pathPrefix,
                    RepeatCount = group.Count() > 1 ? group.Count() : 0,
                });
            }
        }
    }

    private static (int, int) CountXmlItems(string content, string parentPath, string childPath)
    {
        var doc = XDocument.Parse(content);
        var parents = ResolveXmlElements(doc, parentPath)?.ToList();
        if (parents == null || parents.Count == 0) return (0, 0);

        var childNodeName = childPath.Trim('/').Split('/').Last().Split('[')[0];
        var sampleCount = Math.Min(parents.Count, 10);
        var totalChildren = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            totalChildren += parents[i].Elements()
                .Count(e => e.Name.LocalName.Equals(childNodeName, StringComparison.OrdinalIgnoreCase));
        }

        return (parents.Count, sampleCount > 0 ? totalChildren / sampleCount : 0);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  JSON DISCOVERY
    // ═══════════════════════════════════════════════════════════════════

    private static List<SourceNode> DiscoverJsonSourceNodes(
        string content, string parentPath, string childPath)
    {
        var nodes = new List<SourceNode>();
        var root = JToken.Parse(content);

        var parentItems = ResolveJsonArray(root, parentPath);
        if (parentItems == null) return nodes;

        var firstParent = parentItems.FirstOrDefault();
        if (firstParent is not JObject parentObj) return nodes;

        // Extract child key from childPath relative to parent
        var childKey = GetRelativeJsonKey(childPath, parentPath);
        // If childPath looks like "$.conversations[0].messages", extract "messages"
        if (string.IsNullOrEmpty(childKey))
        {
            var parts = childPath.TrimStart('$', '.').Split('.');
            childKey = parts.Last().Split('[')[0];
        }
        // Strip trailing array index (e.g. "messages[0]" → "messages")
        childKey = Regex.Replace(childKey, @"\[\d+\]$", "");

        // Discover parent-level fields
        DiscoverJsonFields(parentObj, childKey, SourceLevel.Parent, "", nodes);

        // Discover child-level fields
        var childToken = parentObj[childKey];
        JObject? firstChild = null;
        if (childToken is JArray childArr && childArr.Count > 0)
            firstChild = childArr[0] as JObject;
        else if (childToken is JObject co)
            firstChild = co;

        if (firstChild != null)
        {
            DiscoverJsonFields(firstChild, null, SourceLevel.Child, "", nodes);
        }

        return nodes;
    }

    private static void DiscoverJsonFields(
        JObject obj, string? excludeChildKey, SourceLevel level, string pathPrefix, List<SourceNode> nodes)
    {
        foreach (var prop in obj.Properties())
        {
            if (excludeChildKey != null && prop.Name.Equals(excludeChildKey, StringComparison.OrdinalIgnoreCase))
                continue;

            var name = string.IsNullOrEmpty(pathPrefix) ? prop.Name : $"{pathPrefix}/{prop.Name}";

            if (prop.Value is JObject nested)
            {
                // Recurse into nested object
                DiscoverJsonFields(nested, null, level, name, nodes);
            }
            else if (prop.Value is JArray arr)
            {
                // Array of primitives or objects
                if (arr.Count > 0 && arr[0] is JObject arrObj)
                {
                    DiscoverJsonFields(arrObj, null, level, name, nodes);
                    foreach (var n in nodes.Where(n => n.GroupName == name))
                        n.RepeatCount = arr.Count;
                }
                else
                {
                    var val = arr.Count > 0 ? arr[0]?.ToString() ?? "" : "";
                    nodes.Add(new SourceNode
                    {
                        Name = name,
                        Path = name,
                        SampleValue = Truncate(val, 80),
                        Level = level,
                        NodeType = NodeType.Array,
                        GroupName = pathPrefix,
                        RepeatCount = arr.Count,
                    });
                }
            }
            else
            {
                var val = prop.Value?.ToString() ?? "";
                nodes.Add(new SourceNode
                {
                    Name = name,
                    Path = name,
                    SampleValue = Truncate(val, 80),
                    Level = level,
                    NodeType = prop.Value?.Type == JTokenType.Boolean ? NodeType.Value :
                               prop.Value?.Type == JTokenType.Integer || prop.Value?.Type == JTokenType.Float ? NodeType.Value :
                               NodeType.Value,
                    GroupName = pathPrefix,
                });
            }
        }
    }

    private static (int, int) CountJsonItems(string content, string parentPath, string childPath)
    {
        var root = JToken.Parse(content);
        var parents = ResolveJsonArray(root, parentPath)?.ToList();
        if (parents == null || parents.Count == 0) return (0, 0);

        var childKey = GetRelativeJsonKey(childPath, parentPath);
        if (string.IsNullOrEmpty(childKey))
        {
            var parts = childPath.TrimStart('$', '.').Split('.');
            childKey = parts.Last().Split('[')[0];
        }
        // Strip trailing array index (e.g. "messages[0]" → "messages")
        childKey = Regex.Replace(childKey, @"\[\d+\]$", "");

        var sampleCount = Math.Min(parents.Count, 10);
        var totalChildren = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            if (parents[i] is JObject obj && obj[childKey] is JArray arr)
                totalChildren += arr.Count;
        }

        return (parents.Count, sampleCount > 0 ? totalChildren / sampleCount : 0);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  TWO-LEVEL EXTRACTION — XML
    // ═══════════════════════════════════════════════════════════════════

    private static List<ChatMessage> ExtractTwoLevelXml(
        string content, string parentPath, IReadOnlyList<string> childPaths, IReadOnlyList<FieldMapping> mappings)
    {
        var messages = new List<ChatMessage>();
        var doc = XDocument.Parse(content);

        var parentElements = ResolveXmlElements(doc, parentPath);
        if (parentElements == null) return messages;

        var childNodeNames = childPaths
            .Select(cp => cp.Trim('/').Split('/').Last().Split('[')[0])
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var parentMappings = mappings
            .Where(m => m.IsEnabled && m.IsMapped && m.SourceLevel == SourceLevel.Parent)
            .ToList();
        var childMappings = mappings
            .Where(m => m.IsEnabled && m.IsMapped && m.SourceLevel == SourceLevel.Child)
            .ToList();
        var defaultMappings = mappings
            .Where(m => m.IsEnabled && m.IsMapped && m.SourceLevel == SourceLevel.None)
            .ToList();

        foreach (var parentEl in parentElements)
        {
            // Read parent-level values once
            var parentValues = new Dictionary<string, string>();
            foreach (var pm in parentMappings)
            {
                var value = ReadXmlValue(parentEl, pm.SourcePath);
                if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(pm.DefaultValue))
                    value = pm.DefaultValue;
                if (value != null)
                    parentValues[pm.TargetField] = value;
            }

            // Find child elements from ALL child node types
            var childElements = parentEl.Elements()
                .Where(e => childNodeNames.Any(cn =>
                    e.Name.LocalName.Equals(cn, StringComparison.OrdinalIgnoreCase)));

            foreach (var childEl in childElements)
            {
                var msg = new ChatMessage();
                var childElName = childEl.Name.LocalName;

                // Apply parent values (cascade)
                foreach (var kv in parentValues)
                    SetField(msg, kv.Key, kv.Value);

                // Filter child mappings: only apply those belonging to this child element type (or untagged)
                var relevantChildMappings = childMappings
                    .Where(cm => string.IsNullOrEmpty(cm.ChildNodeKey) ||
                                 cm.ChildNodeKey.Equals(childElName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Apply only relevant child values for this child type
                foreach (var cm in relevantChildMappings)
                {
                    var value = ReadXmlValue(childEl, cm.SourcePath);
                    if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(cm.DefaultValue))
                        value = cm.DefaultValue;
                    SetField(msg, cm.TargetField, value);
                }

                // Apply default-level mappings (neither parent nor child)
                foreach (var dm in defaultMappings)
                {
                    if (!string.IsNullOrEmpty(dm.DefaultValue))
                        SetField(msg, dm.TargetField, dm.DefaultValue);
                }

                if (!IsEmpty(msg)) messages.Add(msg);
            }
        }

        return messages;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  TWO-LEVEL EXTRACTION — JSON
    // ═══════════════════════════════════════════════════════════════════

    private static List<ChatMessage> ExtractTwoLevelJson(
        string content, string parentPath, IReadOnlyList<string> childPaths, IReadOnlyList<FieldMapping> mappings)
    {
        var messages = new List<ChatMessage>();
        var root = JToken.Parse(content);

        var parentItems = ResolveJsonArray(root, parentPath);
        if (parentItems == null) return messages;

        // Resolve all child keys, strip trailing [index], and deduplicate
        var childKeys = childPaths.Select(cp =>
        {
            var key = GetRelativeJsonKey(cp, parentPath);
            if (string.IsNullOrEmpty(key))
            {
                var parts = cp.TrimStart('$', '.').Split('.');
                key = parts.Last().Split('[')[0];
            }
            // Strip trailing array index (e.g. "messages[0]" → "messages")
            key = Regex.Replace(key, @"\[\d+\]$", "");
            return key;
        }).Where(k => !string.IsNullOrEmpty(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var parentMappings = mappings
            .Where(m => m.IsEnabled && m.IsMapped && m.SourceLevel == SourceLevel.Parent)
            .ToList();
        var childMappings = mappings
            .Where(m => m.IsEnabled && m.IsMapped && m.SourceLevel == SourceLevel.Child)
            .ToList();
        var defaultMappings = mappings
            .Where(m => m.IsEnabled && m.IsMapped && m.SourceLevel == SourceLevel.None)
            .ToList();

        foreach (var parentItem in parentItems)
        {
            if (parentItem is not JObject parentObj) continue;

            // Read parent-level values once
            var parentValues = new Dictionary<string, string>();
            foreach (var pm in parentMappings)
            {
                var value = ReadJsonValue(parentObj, pm.SourcePath);
                if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(pm.DefaultValue))
                    value = pm.DefaultValue;
                if (value != null)
                    parentValues[pm.TargetField] = value;
            }

            // Iterate over ALL child keys
            foreach (var childKey in childKeys)
            {
                var childToken = parentObj[childKey];
                IEnumerable<JToken>? childItems = null;
                if (childToken is JArray arr) childItems = arr;
                else if (childToken != null) childItems = new[] { childToken };

                if (childItems == null) continue;

                // Filter child mappings: only apply those belonging to this child key (or untagged for backward compat)
                var relevantChildMappings = childMappings
                    .Where(cm => string.IsNullOrEmpty(cm.ChildNodeKey) ||
                                 cm.ChildNodeKey.Equals(childKey, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var childItem in childItems)
                {
                    var msg = new ChatMessage();

                    // Apply parent values (cascade)
                    foreach (var kv in parentValues)
                        SetField(msg, kv.Key, kv.Value);

                    // Apply only relevant child values for this child type
                    foreach (var cm in relevantChildMappings)
                    {
                        var value = ReadJsonValue(childItem, cm.SourcePath);
                        if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(cm.DefaultValue))
                            value = cm.DefaultValue;
                        SetField(msg, cm.TargetField, value);
                    }

                    // Apply default-level mappings
                    foreach (var dm in defaultMappings)
                    {
                        if (!string.IsNullOrEmpty(dm.DefaultValue))
                            SetField(msg, dm.TargetField, dm.DefaultValue);
                    }

                    if (!IsEmpty(msg)) messages.Add(msg);
                }
            }
        }

        return messages;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FLAT JSON — navigate by path, directly read value
    // ═══════════════════════════════════════════════════════════════════

    private static List<ChatMessage> ExtractFromJson(
        string content, string startPath, IReadOnlyList<FieldMapping> mappings)
    {
        var messages = new List<ChatMessage>();
        var root = JToken.Parse(content);

        var items = ResolveJsonArray(root, startPath);
        if (items == null) return messages;

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
            if (root is JArray arr) return arr;
            return null;
        }

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

    private static string GetRelativeJsonKey(string sourcePath, string startPath)
    {
        var src = sourcePath.TrimStart('$', '.');
        var start = startPath.TrimStart('$', '.');

        if (src.StartsWith(start, StringComparison.OrdinalIgnoreCase))
        {
            var relative = src[start.Length..];
            relative = Regex.Replace(relative, @"^\[\d+\]\.?", "");
            relative = relative.TrimStart('.');
            return relative;
        }

        return src;
    }

    private static string? ReadJsonValue(JToken item, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return item.Type == JTokenType.Object ? null : item.ToString();

        JToken? current = item;

        // Support slash-based paths (from discovery) by converting to dot-based
        var normalizedPath = relativePath.Replace('/', '.');

        foreach (var part in SplitPath(normalizedPath))
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
    //  FLAT XML — navigate by path, directly read element text / attribute
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

        int startIdx = parts[0].Equals(doc.Root.Name.LocalName, StringComparison.OrdinalIgnoreCase) ? 1 : 0;

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

        // Support slash-based paths from discovery
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
                var idxMatch = Regex.Match(mapping.SourcePath, @"\[(\d+)\]");

                string? value = null;
                var colIdx = Array.FindIndex(headers, h =>
                    h.Equals(colName, StringComparison.OrdinalIgnoreCase));
                if (colIdx >= 0 && colIdx < cols.Length)
                    value = cols[colIdx];
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
            default:
                // Custom fields go into the dictionary
                msg.CustomFields[fieldName] = value;
                break;
        }
    }

    private static bool IsEmpty(ChatMessage msg) =>
        string.IsNullOrWhiteSpace(msg.MessageBody) &&
        string.IsNullOrWhiteSpace(msg.SenderName) &&
        string.IsNullOrWhiteSpace(msg.Timestamp) &&
        msg.CustomFields.Count == 0;

    private static string Truncate(string value, int maxLen)
        => value.Length > maxLen ? value[..maxLen] + "…" : value;

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
