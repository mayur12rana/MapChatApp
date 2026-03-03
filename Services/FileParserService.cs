using System.IO;
using System.Xml.Linq;
using ChatMapperApp.Models;
using Newtonsoft.Json.Linq;

namespace ChatMapperApp.Services;

/// <summary>
/// Parses uploaded files into a FileTreeNode hierarchy for the tree view.
/// </summary>
public static class FileParserService
{
    public static FileFormat DetectFormat(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext == ".json") return FileFormat.Json;
        if (ext == ".xml") return FileFormat.Xml;
        if (ext == ".csv") return FileFormat.Csv;
        if (ext == ".tsv") return FileFormat.Tsv;
        if (ext is ".html" or ".htm") return FileFormat.Html;
        if (ext is ".txt" or ".msg" or ".log") return FileFormat.PlainText;

        try
        {
            using var sr = new StreamReader(filePath);
            var buf = new char[512];
            sr.Read(buf, 0, 512);
            var text = new string(buf).TrimStart();
            if (text.StartsWith('{') || text.StartsWith('[')) return FileFormat.Json;
            if (text.StartsWith('<')) return text.Contains("html", StringComparison.OrdinalIgnoreCase)
                ? FileFormat.Html : FileFormat.Xml;
        }
        catch { }

        return FileFormat.Unknown;
    }

    public static (FileTreeNode root, string rawContent) ParseFile(string filePath)
    {
        var format = DetectFormat(filePath);
        var rawContent = File.ReadAllText(filePath);

        var root = format switch
        {
            FileFormat.Json => ParseJson(rawContent, filePath),
            FileFormat.Xml or FileFormat.Html => ParseXml(rawContent, filePath),
            FileFormat.Csv or FileFormat.Tsv => ParseCsv(rawContent, filePath, format == FileFormat.Tsv),
            _ => ParsePlainText(rawContent, filePath),
        };

        root.NodeType = NodeType.Root;
        return (root, rawContent);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  JSON
    // ═══════════════════════════════════════════════════════════════════

    private static FileTreeNode ParseJson(string content, string filePath)
    {
        var token = JToken.Parse(content);
        var root = new FileTreeNode
        {
            Name = Path.GetFileName(filePath),
            DisplayName = Path.GetFileName(filePath),
            Path = "$",
        };

        BuildJsonTree(token, root, "$");
        return root;
    }

    private static void BuildJsonTree(JToken token, FileTreeNode parent, string path)
    {
        switch (token.Type)
        {
            case JTokenType.Object:
                var obj = (JObject)token;
                parent.ChildCount = obj.Count;
                if (string.IsNullOrEmpty(parent.DisplayName))
                    parent.DisplayName = "{ }";

                foreach (var prop in obj.Properties())
                {
                    var childPath = $"{path}.{prop.Name}";
                    var child = new FileTreeNode
                    {
                        Name = prop.Name,
                        Path = childPath,
                        Parent = parent,
                        Depth = parent.Depth + 1,
                    };

                    if (prop.Value.Type == JTokenType.Object)
                    {
                        child.DisplayName = $"\"{prop.Name}\": {{ }}";
                        child.NodeType = NodeType.Object;
                        child.ChildCount = ((JObject)prop.Value).Count;
                        BuildJsonTree(prop.Value, child, childPath);
                    }
                    else if (prop.Value.Type == JTokenType.Array)
                    {
                        var arr = (JArray)prop.Value;
                        child.DisplayName = $"\"{prop.Name}\": [{arr.Count}]";
                        child.NodeType = NodeType.Array;
                        child.ChildCount = arr.Count;
                        BuildJsonArray(arr, child, childPath);
                    }
                    else
                    {
                        var val = prop.Value.ToString();
                        var preview = val.Length > 60 ? val[..60] + "…" : val;
                        var jsonVal = FormatJsonValueDisplay(prop.Value, preview);
                        child.DisplayName = $"\"{prop.Name}\": {jsonVal}";
                        child.NodeType = NodeType.Value;
                        child.Value = val;
                        child.SampleValues.Add(val);
                    }

                    parent.Children.Add(child);
                }
                break;

            case JTokenType.Array:
                var rootArr = (JArray)token;
                parent.ChildCount = rootArr.Count;
                if (string.IsNullOrEmpty(parent.DisplayName))
                    parent.DisplayName = $"Array [{rootArr.Count}]";
                parent.NodeType = NodeType.Array;
                BuildJsonArray(rootArr, parent, path);
                break;

            default:
                parent.Value = token.ToString();
                parent.NodeType = NodeType.Value;
                break;
        }
    }

    private static void BuildJsonArray(JArray array, FileTreeNode parent, string path)
    {
        var limit = Math.Min(array.Count, 50);
        for (int i = 0; i < limit; i++)
        {
            var item = array[i];
            var itemPath = $"{path}[{i}]";
            var child = new FileTreeNode
            {
                Name = $"[{i}]",
                Path = itemPath,
                Parent = parent,
                Depth = parent.Depth + 1,
            };

            if (item.Type == JTokenType.Object)
            {
                child.DisplayName = $"[{i}]: {{ }}";
                child.NodeType = NodeType.Object;
                child.ChildCount = ((JObject)item).Count;
                BuildJsonTree(item, child, itemPath);
            }
            else if (item.Type == JTokenType.Array)
            {
                child.DisplayName = $"[{i}]: [{((JArray)item).Count}]";
                child.NodeType = NodeType.Array;
                BuildJsonArray((JArray)item, child, itemPath);
            }
            else
            {
                var val = item.ToString();
                var preview = val.Length > 60 ? val[..60] + "…" : val;
                child.DisplayName = $"[{i}]: {FormatJsonValueDisplay(item, preview)}";
                child.NodeType = NodeType.Value;
                child.Value = val;
            }

            parent.Children.Add(child);
        }

        if (array.Count > limit)
        {
            parent.Children.Add(new FileTreeNode
            {
                Name = "...",
                DisplayName = $"… ({array.Count - limit} more items)",
                NodeType = NodeType.Text,
                Parent = parent,
                Depth = parent.Depth + 1,
            });
        }

        // Collect sample values from array items for property nodes
        CollectJsonSamples(array, parent);
    }

    private static void CollectJsonSamples(JArray array, FileTreeNode arrayNode)
    {
        var sampleCount = Math.Min(array.Count, 5);
        for (int i = 0; i < sampleCount; i++)
        {
            if (array[i] is not JObject obj) continue;
            // For the first object child in the tree, populate samples on its value children
            if (i < arrayNode.Children.Count && arrayNode.Children[i].Children.Count > 0)
            {
                foreach (var fieldNode in arrayNode.Children[i].Children)
                {
                    if (fieldNode.NodeType == NodeType.Value && fieldNode.SampleValues.Count < 5)
                    {
                        // Also look in other array items for the same field name
                        for (int j = 0; j < sampleCount; j++)
                        {
                            if (j == i) continue;
                            if (array[j] is JObject otherObj && otherObj[fieldNode.Name] != null)
                                fieldNode.SampleValues.Add(otherObj[fieldNode.Name]!.ToString());
                        }
                    }
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  XML
    // ═══════════════════════════════════════════════════════════════════

    private static FileTreeNode ParseXml(string content, string filePath)
    {
        var doc = XDocument.Parse(content);
        var root = new FileTreeNode
        {
            Name = Path.GetFileName(filePath),
            DisplayName = Path.GetFileName(filePath),
            Path = "/",
        };

        if (doc.Root != null)
            BuildXmlTree(doc.Root, root, "");

        return root;
    }

    private static void BuildXmlTree(XElement element, FileTreeNode parent, string parentPath)
    {
        var path = $"{parentPath}/{element.Name.LocalName}";
        var node = new FileTreeNode
        {
            Name = element.Name.LocalName,
            Path = path,
            Parent = parent,
            Depth = parent.Depth + 1,
        };

        // Attributes
        foreach (var attr in element.Attributes())
        {
            var attrPath = $"{path}/@{attr.Name.LocalName}";
            var attrPreview = attr.Value.Length > 60 ? attr.Value[..60] + "…" : attr.Value;
            node.Children.Add(new FileTreeNode
            {
                Name = $"@{attr.Name.LocalName}",
                DisplayName = $"@{attr.Name.LocalName}=\"{attrPreview}\"",
                Path = attrPath,
                NodeType = NodeType.Attribute,
                Value = attr.Value,
                Parent = node,
                Depth = node.Depth + 1,
                SampleValues = { attr.Value },
            });
        }

        if (element.HasElements)
        {
            var groups = element.Elements().GroupBy(e => e.Name.LocalName).ToList();
            node.DisplayName = $"<{element.Name.LocalName}>";
            node.NodeType = NodeType.Element;
            node.ChildCount = element.Elements().Count();

            foreach (var group in groups)
            {
                if (group.Count() > 1)
                {
                    // Repeating elements — show as group
                    var groupNode = new FileTreeNode
                    {
                        Name = group.Key,
                        DisplayName = $"<{group.Key}> ×{group.Count()}",
                        Path = $"{path}/{group.Key}",
                        NodeType = NodeType.Array,
                        ChildCount = group.Count(),
                        Parent = node,
                        Depth = node.Depth + 1,
                    };

                    int idx = 0;
                    foreach (var child in group.Take(50))
                    {
                        BuildXmlTree(child, groupNode, $"{path}/{group.Key}[{idx}]");
                        idx++;
                    }
                    node.Children.Add(groupNode);
                }
                else
                {
                    foreach (var child in group)
                        BuildXmlTree(child, node, path);
                }
            }

            // Closing tag
            node.Children.Add(new FileTreeNode
            {
                Name = $"/{element.Name.LocalName}",
                DisplayName = $"</{element.Name.LocalName}>",
                Path = path,
                NodeType = NodeType.Text,
                Parent = node,
                Depth = node.Depth + 1,
            });
        }
        else
        {
            // Leaf element with text content
            var val = element.Value.Trim();
            var preview = val.Length > 60 ? val[..60] + "…" : val;
            node.DisplayName = string.IsNullOrEmpty(val)
                ? $"<{element.Name.LocalName} />"
                : $"<{element.Name.LocalName}>{preview}</{element.Name.LocalName}>";
            node.NodeType = NodeType.Value;
            node.Value = val;
            node.SampleValues.Add(val);
        }

        parent.Children.Add(node);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CSV / TSV
    // ═══════════════════════════════════════════════════════════════════

    private static FileTreeNode ParseCsv(string content, string filePath, bool isTsv)
    {
        var delimiter = isTsv ? '\t' : DetectCsvDelimiter(content);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var root = new FileTreeNode
        {
            Name = Path.GetFileName(filePath),
            DisplayName = $"{Path.GetFileName(filePath)}  ({lines.Length} rows)",
            Path = "$",
            ChildCount = lines.Length,
        };

        if (lines.Length < 1) return root;

        var headers = SplitCsvLine(lines[0], delimiter);

        // Columns node
        var columnsNode = new FileTreeNode
        {
            Name = "Columns",
            DisplayName = $"Columns  ({headers.Length})",
            Path = "$.columns",
            NodeType = NodeType.Object,
            Parent = root,
            Depth = 1,
            ChildCount = headers.Length,
        };

        for (int c = 0; c < headers.Length; c++)
        {
            var colName = headers[c].Trim().Trim('"');
            var colNode = new FileTreeNode
            {
                Name = colName,
                DisplayName = colName,
                Path = $"$.columns[{c}]",
                NodeType = NodeType.CsvColumn,
                Parent = columnsNode,
                Depth = 2,
            };

            for (int r = 1; r < Math.Min(lines.Length, 6); r++)
            {
                var row = SplitCsvLine(lines[r], delimiter);
                if (c < row.Length)
                    colNode.SampleValues.Add(row[c].Trim().Trim('"'));
            }
            if (colNode.SampleValues.Count > 0)
                colNode.Value = colNode.SampleValues[0];

            columnsNode.Children.Add(colNode);
        }

        root.Children.Add(columnsNode);

        // Sample rows (first 20)
        var rowsNode = new FileTreeNode
        {
            Name = "Rows",
            DisplayName = $"Rows  ({lines.Length - 1})",
            Path = "$.rows",
            NodeType = NodeType.Array,
            Parent = root,
            Depth = 1,
            ChildCount = lines.Length - 1,
        };

        for (int r = 1; r < Math.Min(lines.Length, 21); r++)
        {
            var row = SplitCsvLine(lines[r], delimiter);
            var rowNode = new FileTreeNode
            {
                Name = $"Row {r}",
                DisplayName = $"Row {r}",
                Path = $"$.rows[{r - 1}]",
                NodeType = NodeType.Object,
                Parent = rowsNode,
                Depth = 2,
            };

            for (int c = 0; c < Math.Min(headers.Length, row.Length); c++)
            {
                var val = row[c].Trim().Trim('"');
                var hdr = headers[c].Trim().Trim('"');
                rowNode.Children.Add(new FileTreeNode
                {
                    Name = hdr,
                    DisplayName = $"{hdr} = {(val.Length > 50 ? val[..50] + "…" : val)}",
                    Path = $"$.rows[{r - 1}].{hdr}",
                    NodeType = NodeType.Value,
                    Value = val,
                    Parent = rowNode,
                    Depth = 3,
                });
            }

            rowsNode.Children.Add(rowNode);
        }

        root.Children.Add(rowsNode);
        return root;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Plain Text
    // ═══════════════════════════════════════════════════════════════════

    private static FileTreeNode ParsePlainText(string content, string filePath)
    {
        var lines = content.Split('\n');
        var root = new FileTreeNode
        {
            Name = Path.GetFileName(filePath),
            DisplayName = $"{Path.GetFileName(filePath)}  ({lines.Length} lines)",
            Path = "$",
            NodeType = NodeType.Root,
            ChildCount = lines.Length,
        };

        for (int i = 0; i < Math.Min(lines.Length, 200); i++)
        {
            var line = lines[i].TrimEnd('\r');
            var preview = line.Length > 80 ? line[..80] + "…" : line;
            root.Children.Add(new FileTreeNode
            {
                Name = $"Line {i + 1}",
                DisplayName = $"Line {i + 1}: {preview}",
                Path = $"$.lines[{i}]",
                NodeType = NodeType.Text,
                Value = line,
                Parent = root,
                Depth = 1,
            });
        }

        return root;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════

    private static string FormatJsonValueDisplay(JToken token, string preview)
    {
        return token.Type switch
        {
            JTokenType.String => $"\"{preview}\"",
            JTokenType.Null => "null",
            JTokenType.Boolean => token.ToString().ToLowerInvariant(),
            _ => preview, // numbers, etc. displayed as-is
        };
    }

    private static char DetectCsvDelimiter(string content)
    {
        var line = content.Split('\n')[0];
        var t = line.Count(c => c == '\t');
        var cm = line.Count(c => c == ',');
        var p = line.Count(c => c == '|');
        var s = line.Count(c => c == ';');
        var max = Math.Max(Math.Max(t, cm), Math.Max(p, s));
        if (max == t && t > 0) return '\t';
        if (max == p && p > 0) return '|';
        if (max == s && s > 0) return ';';
        return ',';
    }

    private static string[] SplitCsvLine(string line, char delimiter)
    {
        if (delimiter != ',') return line.Split(delimiter);
        var result = new List<string>();
        bool q = false;
        var sb = new System.Text.StringBuilder();
        foreach (char c in line)
        {
            if (c == '"') { q = !q; continue; }
            if (c == delimiter && !q) { result.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(c);
        }
        result.Add(sb.ToString());
        return result.ToArray();
    }
}
