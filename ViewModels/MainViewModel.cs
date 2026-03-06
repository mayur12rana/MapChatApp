using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using ChatMapperApp.Models;
using ChatMapperApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace ChatMapperApp.ViewModels;

public partial class MainViewModel : ObservableObject
{
    // ═══════════════════════════════════════════════════════════════════
    //  STATE
    // ═══════════════════════════════════════════════════════════════════

    [ObservableProperty] private string _windowTitle = "Chat Export Mapper";
    [ObservableProperty] private string _statusMessage = "Ready — Open or drag-drop a chat export file to begin.";
    [ObservableProperty] private bool _isFileLoaded;
    [ObservableProperty] private string _loadedFilePath = string.Empty;
    [ObservableProperty] private string _loadedFileName = string.Empty;
    [ObservableProperty] private FileFormat _detectedFormat;
    [ObservableProperty] private string _formatDisplayName = string.Empty;
    [ObservableProperty] private string _rawContent = string.Empty;
    [ObservableProperty] private int _rawContentLineCount;

    // Tree
    [ObservableProperty] private FileTreeNode? _selectedNode;
    [ObservableProperty] private string _selectedNodePath = string.Empty;
    [ObservableProperty] private string _selectedNodeValue = string.Empty;
    [ObservableProperty] private string _selectedNodeSamples = string.Empty;

    // Hierarchy (Parent/Child)
    [ObservableProperty] private string _parentNodePath = string.Empty;
    [ObservableProperty] private string _childNodePath = string.Empty;
    [ObservableProperty] private HierarchyMode _hierarchyMode = HierarchyMode.TwoLevel;
    [ObservableProperty] private bool _isTwoLevelMode = true;
    [ObservableProperty] private int _parentItemCount;
    [ObservableProperty] private int _childItemCountSample;
    [ObservableProperty] private string _resultEstimate = string.Empty;
    [ObservableProperty] private string _childNodePathsDisplay = string.Empty;

    // Mapping
    [ObservableProperty] private FieldMapping? _selectedMapping;
    [ObservableProperty] private SourceNode? _selectedSourceNode;
    [ObservableProperty] private string _sourceNodeFilter = string.Empty;

    // Results
    [ObservableProperty] private int _extractedMessageCount;
    [ObservableProperty] private int _mappedFieldCount;
    [ObservableProperty] private string _profileName = string.Empty;
    [ObservableProperty] private int _parentConversationCount;

    public ObservableCollection<FileTreeNode> TreeNodes { get; } = new();
    public ObservableCollection<FieldMapping> FieldMappings { get; } = new();
    public ObservableCollection<ChatMessage> ExtractedMessages { get; } = new();
    public ObservableCollection<SourceNode> DiscoveredSourceNodes { get; } = new();
    public ObservableCollection<string> ChildNodePaths { get; } = new();

    // ═══════════════════════════════════════════════════════════════════
    //  CONSTRUCTOR
    // ═══════════════════════════════════════════════════════════════════

    public MainViewModel()
    {
        InitializeFieldMappings();
    }

    private void InitializeFieldMappings()
    {
        FieldMappings.Clear();
        var fields = new (string name, string desc, bool required)[]
        {
            ("MessageId",         "Unique message identifier", false),
            ("Timestamp",         "Message timestamp (UTC preferred)", true),
            ("SenderName",        "Display name of the sender", true),
            ("SenderId",          "Platform-specific sender ID", false),
            ("RecipientName",     "Display name of the recipient", false),
            ("RecipientId",       "Platform-specific recipient ID", false),
            ("MessageBody",       "Plain-text message content", true),
            ("MessageType",       "Type: Text, File, Image, System, etc.", false),
            ("ChannelName",       "Channel / room / conversation name", false),
            ("ChannelId",         "Platform-specific channel ID", false),
            ("ThreadId",          "Thread identifier", false),
            ("ParentMessageId",   "Reply-to message ID", false),
            ("HasAttachment",     "Whether attachments exist (true/false)", false),
            ("AttachmentNames",   "Semicolon-delimited filenames", false),
            ("SourcePlatform",    "Originating platform name", false),
            ("SourceFile",        "Original source file path", false),
            ("IsEdited",          "Message was edited (true/false)", false),
            ("IsDeleted",         "Message was deleted (true/false)", false),
            ("ExtendedProperties","Additional metadata (JSON)", false),
        };

        foreach (var (name, desc, required) in fields)
        {
            FieldMappings.Add(new FieldMapping
            {
                TargetField = name,
                Description = desc,
                IsRequired = required,
                IsEnabled = true,
            });
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  HIERARCHY MODE TOGGLE
    // ═══════════════════════════════════════════════════════════════════

    partial void OnIsTwoLevelModeChanged(bool value)
    {
        HierarchyMode = value ? HierarchyMode.TwoLevel : HierarchyMode.Flat;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  OPEN / LOAD
    // ═══════════════════════════════════════════════════════════════════

    [RelayCommand]
    private void OpenFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open Chat Export File",
            Filter = "All Supported|*.json;*.xml;*.csv;*.tsv;*.html;*.htm;*.txt;*.msg;*.log|" +
                     "JSON|*.json|XML|*.xml|CSV|*.csv;*.tsv|HTML|*.html;*.htm|" +
                     "Text|*.txt;*.msg;*.log|All Files|*.*",
        };

        if (dlg.ShowDialog() == true)
        {
            try { LoadFile(dlg.FileName); }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading file:\n{ex.Message}", "Load Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public void LoadFile(string filePath)
    {
        StatusMessage = "Loading file…";

        var (root, raw) = FileParserService.ParseFile(filePath);

        TreeNodes.Clear();
        TreeNodes.Add(root);
        root.IsExpanded = true;
        foreach (var child in root.Children.Take(5))
            child.IsExpanded = true;

        RawContent = raw;
        RawContentLineCount = raw.Split('\n').Length;
        LoadedFilePath = filePath;
        LoadedFileName = Path.GetFileName(filePath);
        DetectedFormat = FileParserService.DetectFormat(filePath);
        FormatDisplayName = DetectedFormat.ToString().ToUpper();
        IsFileLoaded = true;
        WindowTitle = $"Chat Export Mapper — {LoadedFileName}";

        // Reset all mappings and hierarchy
        ParentNodePath = string.Empty;
        ChildNodePath = string.Empty;
        ChildNodePaths.Clear();
        ChildNodePathsDisplay = string.Empty;
        ParentItemCount = 0;
        ChildItemCountSample = 0;
        ResultEstimate = string.Empty;
        DiscoveredSourceNodes.Clear();

        foreach (var m in FieldMappings)
        {
            m.SourcePath = string.Empty;
            m.DefaultValue = string.Empty;
            m.IsMapped = false;
            m.PreviewValue = string.Empty;
            m.SourceLevel = SourceLevel.None;
            m.ChildNodeKey = string.Empty;
        }
        ExtractedMessages.Clear();
        ExtractedMessageCount = 0;
        MappedFieldCount = 0;
        ParentConversationCount = 0;

        StatusMessage = $"Loaded {LoadedFileName} — {FormatDisplayName}, {RawContentLineCount:N0} lines";
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PARENT / CHILD NODE
    // ═══════════════════════════════════════════════════════════════════

    [RelayCommand]
    private void SetParentNode()
    {
        if (SelectedNode == null) return;
        ClearNodeFlags(TreeNodes, isParent: true);
        SelectedNode.IsParentNode = true;
        ParentNodePath = SelectedNode.Path;
        StatusMessage = $"Parent node 🟢 {ParentNodePath}";

        TryAutoDiscover();
    }

    [RelayCommand]
    private void SetChildNode()
    {
        if (SelectedNode == null) return;

        // Validate: child should be inside parent
        if (!string.IsNullOrEmpty(ParentNodePath) &&
            !SelectedNode.Path.StartsWith(ParentNodePath, StringComparison.OrdinalIgnoreCase) &&
            !IsDescendantOfParent(SelectedNode))
        {
            MessageBox.Show("The child node should be inside the parent node.\nSelect a repeating element within the parent.",
                "Invalid Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Toggle: if already a child node, remove it; otherwise add it
        if (SelectedNode.IsChildNode)
        {
            SelectedNode.IsChildNode = false;
            ChildNodePaths.Remove(SelectedNode.Path);
        }
        else
        {
            SelectedNode.IsChildNode = true;
            if (!ChildNodePaths.Contains(SelectedNode.Path))
                ChildNodePaths.Add(SelectedNode.Path);
        }

        UpdateChildPathDisplay();
        TryAutoDiscover();
    }

    [RelayCommand]
    private void RemoveChildNode()
    {
        if (SelectedNode == null || !SelectedNode.IsChildNode) return;
        SelectedNode.IsChildNode = false;
        ChildNodePaths.Remove(SelectedNode.Path);
        UpdateChildPathDisplay();
        TryAutoDiscover();
    }

    private void UpdateChildPathDisplay()
    {
        // Keep ChildNodePath as the first child for backward compat
        ChildNodePath = ChildNodePaths.Count > 0 ? ChildNodePaths[0] : string.Empty;
        ChildNodePathsDisplay = ChildNodePaths.Count switch
        {
            0 => string.Empty,
            1 => ChildNodePaths[0],
            _ => string.Join("  |  ", ChildNodePaths)
        };
        StatusMessage = ChildNodePaths.Count > 0
            ? $"Child nodes 🔵 {ChildNodePaths.Count} selected"
            : "Child node cleared";
    }

    private bool IsDescendantOfParent(FileTreeNode node)
    {
        var current = node.Parent;
        while (current != null)
        {
            if (current.IsParentNode) return true;
            current = current.Parent;
        }
        return false;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  AUTO-DISCOVERY
    // ═══════════════════════════════════════════════════════════════════

    private void TryAutoDiscover()
    {
        if (string.IsNullOrWhiteSpace(ParentNodePath) || ChildNodePaths.Count == 0)
            return;
        if (string.IsNullOrWhiteSpace(RawContent))
            return;

        try
        {
            StatusMessage = "Discovering source fields…";

            // Collect all child node names for filtering
            var childNodeNames = ChildNodePaths
                .Select(cp => cp.TrimStart('$', '.', '/').Split(new[] { '.', '/' }).Last().Split('[')[0])
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Discover available source nodes from all child paths
            var allNodes = new List<SourceNode>();
            foreach (var childPath in ChildNodePaths)
            {
                var nodes = MappingService.DiscoverSourceNodes(
                    RawContent, DetectedFormat, ParentNodePath, childPath);
                foreach (var node in nodes)
                {
                    // Avoid duplicate parent-level nodes
                    if (node.Level == SourceLevel.Parent &&
                        allNodes.Any(n => n.Level == SourceLevel.Parent && n.Path == node.Path))
                        continue;
                    // Skip parent-level nodes from repeating container elements
                    // (e.g. Message×13, ParticipantEntered×2 — these are child-type siblings)
                    if (node.Level == SourceLevel.Parent && node.RepeatCount > 0)
                        continue;
                    // Skip parent-level nodes that match a toggled child type name
                    if (node.Level == SourceLevel.Parent && childNodeNames.Any(cn =>
                        node.Path.Equals(cn, StringComparison.OrdinalIgnoreCase) ||
                        node.Path.StartsWith(cn + "/", StringComparison.OrdinalIgnoreCase)))
                        continue;
                    // Tag child-level nodes with their child group and prefix display name
                    if (node.Level == SourceLevel.Child)
                    {
                        var childName = childPath.TrimStart('$', '.', '/').Split(new[] { '.', '/' }).Last().Split('[')[0];
                        node.ChildKey = childName;
                        // Prefix the child node name to Name for display (Path stays relative for extraction)
                        node.Name = $"{childName}/{node.Name}";
                        if (ChildNodePaths.Count > 1)
                            node.GroupName = childName;
                    }
                    allNodes.Add(node);
                }
            }

            DiscoveredSourceNodes.Clear();
            foreach (var node in allNodes)
                DiscoveredSourceNodes.Add(node);

            // Count items for result estimate (sum across all child paths)
            var totalChildCount = 0;
            var parentCount = 0;
            foreach (var childPath in ChildNodePaths)
            {
                var (pc, avgChild) = MappingService.CountItems(
                    RawContent, DetectedFormat, ParentNodePath, childPath);
                parentCount = pc; // same parent count
                totalChildCount += avgChild;
            }

            ParentItemCount = parentCount;
            ChildItemCountSample = totalChildCount;

            if (parentCount > 0 && totalChildCount > 0)
                ResultEstimate = $"~{parentCount * totalChildCount:N0}+ message rows";
            else if (parentCount > 0)
                ResultEstimate = $"{parentCount} parent items";
            else
                ResultEstimate = string.Empty;

            StatusMessage = $"Discovered {allNodes.Count} source fields — {allNodes.Count(n => n.Level == SourceLevel.Parent)} parent, {allNodes.Count(n => n.Level == SourceLevel.Child)} child ({ChildNodePaths.Count} child types)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Discovery error: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  MAP SOURCE → TARGET
    // ═══════════════════════════════════════════════════════════════════

    [RelayCommand]
    private void MapSourceToTarget()
    {
        if (SelectedSourceNode == null || SelectedMapping == null) return;

        SelectedMapping.SourcePath = SelectedSourceNode.Path;
        SelectedMapping.SourceLevel = SelectedSourceNode.Level;
        SelectedMapping.ChildNodeKey = SelectedSourceNode.ChildKey;
        SelectedMapping.IsMapped = true;
        SelectedMapping.PreviewValue = SelectedSourceNode.SampleValue;

        MappedFieldCount = FieldMappings.Count(m => m.IsMapped);
        StatusMessage = $"Mapped:  {SelectedSourceNode.Name}  →  {SelectedMapping.TargetField}  ({SelectedSourceNode.Level})";
    }

    [RelayCommand]
    private void AssignToField()
    {
        if (SelectedNode == null || SelectedMapping == null) return;

        SelectedMapping.SourcePath = SelectedNode.Path;
        SelectedMapping.IsMapped = true;

        // Determine level based on whether the node is under any child path
        var matchedChildPath = ChildNodePaths.FirstOrDefault(cp =>
            SelectedNode.Path.Contains(cp.Split('/').Last().Split('[')[0]));
        if (matchedChildPath != null)
        {
            SelectedMapping.SourceLevel = SourceLevel.Child;
            SelectedMapping.ChildNodeKey = matchedChildPath.TrimStart('$', '.', '/').Split(new[] { '.', '/' }).Last().Split('[')[0];
        }
        else if (!string.IsNullOrEmpty(ParentNodePath))
            SelectedMapping.SourceLevel = SourceLevel.Parent;

        if (SelectedNode.SampleValues.Count > 0)
            SelectedMapping.PreviewValue = SelectedNode.SampleValues[0];
        else if (!string.IsNullOrEmpty(SelectedNode.Value))
            SelectedMapping.PreviewValue = SelectedNode.Value;

        MappedFieldCount = FieldMappings.Count(m => m.IsMapped);
        StatusMessage = $"Mapped:  {SelectedNode.Name}  →  {SelectedMapping.TargetField}";
    }

    [RelayCommand]
    private void ClearMapping()
    {
        if (SelectedMapping == null) return;
        SelectedMapping.SourcePath = string.Empty;
        SelectedMapping.DefaultValue = string.Empty;
        SelectedMapping.IsMapped = false;
        SelectedMapping.PreviewValue = string.Empty;
        SelectedMapping.SourceLevel = SourceLevel.None;
        SelectedMapping.ChildNodeKey = string.Empty;
        MappedFieldCount = FieldMappings.Count(m => m.IsMapped);
        StatusMessage = $"Cleared mapping for {SelectedMapping.TargetField}";
    }

    [RelayCommand]
    private void ClearAllMappings()
    {
        foreach (var m in FieldMappings)
        {
            m.SourcePath = string.Empty;
            m.DefaultValue = string.Empty;
            m.IsMapped = false;
            m.PreviewValue = string.Empty;
            m.SourceLevel = SourceLevel.None;
            m.ChildNodeKey = string.Empty;
        }
        ParentNodePath = string.Empty;
        ChildNodePath = string.Empty;
        ChildNodePaths.Clear();
        ChildNodePathsDisplay = string.Empty;
        ParentItemCount = 0;
        ChildItemCountSample = 0;
        ResultEstimate = string.Empty;
        DiscoveredSourceNodes.Clear();
        ClearNodeFlags(TreeNodes, true);
        ClearNodeFlags(TreeNodes, false);
        ExtractedMessages.Clear();
        ExtractedMessageCount = 0;
        MappedFieldCount = 0;
        ParentConversationCount = 0;
        StatusMessage = "All mappings cleared.";
    }

    // ═══════════════════════════════════════════════════════════════════
    //  DYNAMIC TARGET OUTPUT FIELDS
    // ═══════════════════════════════════════════════════════════════════

    [ObservableProperty] private string _newTargetFieldName = string.Empty;

    [RelayCommand]
    private void AddTargetField()
    {
        var name = NewTargetFieldName?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;

        // Check for duplicate
        if (FieldMappings.Any(m => m.TargetField.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show($"A field named '{name}' already exists.", "Duplicate Field",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        FieldMappings.Add(new FieldMapping
        {
            TargetField = name,
            Description = "Custom field",
            IsEnabled = true,
            IsCustom = true,
        });

        NewTargetFieldName = string.Empty;
        StatusMessage = $"Added custom target field: {name}";
    }

    [RelayCommand]
    private void RemoveTargetField()
    {
        if (SelectedMapping == null) return;
        if (!SelectedMapping.IsCustom)
        {
            MessageBox.Show("Cannot remove built-in fields. Only custom fields can be removed.",
                "Cannot Remove", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var name = SelectedMapping.TargetField;
        FieldMappings.Remove(SelectedMapping);
        SelectedMapping = null;
        MappedFieldCount = FieldMappings.Count(m => m.IsMapped);
        StatusMessage = $"Removed custom target field: {name}";
    }

    // ═══════════════════════════════════════════════════════════════════
    //  EXTRACT
    // ═══════════════════════════════════════════════════════════════════

    [RelayCommand]
    private void ExtractMessages()
    {
        if (!IsFileLoaded)
        {
            MessageBox.Show("Please load a file first.", "No File", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (FieldMappings.All(m => !m.IsMapped))
        {
            MessageBox.Show("Map at least one field before extracting.", "No Mappings",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StatusMessage = "Extracting messages…";
        try
        {
            var childPaths = ChildNodePaths.Count > 0 ? ChildNodePaths.ToList() : new List<string>();
            var messages = MappingService.ExtractMessages(
                RawContent, DetectedFormat, ParentNodePath, childPaths,
                HierarchyMode, FieldMappings.ToList());

            foreach (var msg in messages)
            {
                if (string.IsNullOrEmpty(msg.SourceFile)) msg.SourceFile = LoadedFileName;
                if (string.IsNullOrEmpty(msg.MessageId)) msg.MessageId = Guid.NewGuid().ToString("N")[..12];
            }

            ExtractedMessages.Clear();
            foreach (var msg in messages) ExtractedMessages.Add(msg);
            ExtractedMessageCount = messages.Count;
            ParentConversationCount = ParentItemCount;

            var convInfo = ParentConversationCount > 0 ? $" from {ParentConversationCount} conversations" : "";
            StatusMessage = $"Extracted {messages.Count} messages{convInfo} from {LoadedFileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            MessageBox.Show($"Extraction error:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  EXPORT
    // ═══════════════════════════════════════════════════════════════════

    [RelayCommand]
    private void ExportResults()
    {
        if (ExtractedMessages.Count == 0)
        {
            MessageBox.Show("No messages to export. Run Extract first.", "Nothing to Export",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Export Normalized Messages",
            Filter = "CSV|*.csv|JSON|*.json|XML|*.xml",
            FileName = $"normalized_{Path.GetFileNameWithoutExtension(LoadedFileName)}",
        };

        if (dlg.ShowDialog() != true) return;
        try
        {
            var ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
            var msgs = ExtractedMessages.ToList();
            switch (ext)
            {
                case ".json": ExportService.ExportToJson(msgs, dlg.FileName); break;
                case ".xml": ExportService.ExportToXml(msgs, dlg.FileName); break;
                default: ExportService.ExportToCsv(msgs, dlg.FileName); break;
            }
            StatusMessage = $"Exported {msgs.Count} messages → {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export error:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PROFILES
    // ═══════════════════════════════════════════════════════════════════

    [RelayCommand]
    private void SaveProfile()
    {
        if (string.IsNullOrWhiteSpace(ProfileName))
        {
            MessageBox.Show("Enter a profile name first.", "Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var profile = new MappingProfile
        {
            Name = ProfileName,
            Format = DetectedFormat,
            Mode = HierarchyMode,
            ParentNodePath = ParentNodePath,
            ChildNodePath = ChildNodePath,
            ChildNodePaths = ChildNodePaths.ToList(),
            Mappings = FieldMappings.Select(m => new FieldMappingSerialized
            {
                TargetField = m.TargetField,
                SourcePath = m.SourcePath,
                DefaultValue = m.DefaultValue,
                IsEnabled = m.IsEnabled,
                SourceLevel = m.SourceLevel,
                IsCustom = m.IsCustom,
                ChildNodeKey = m.ChildNodeKey,
            }).ToList(),
        };

        var dlg = new SaveFileDialog
        {
            Title = "Save Mapping Profile",
            Filter = "JSON Profile|*.profile.json",
            FileName = $"{ProfileName.Replace(" ", "_")}.profile",
        };
        if (dlg.ShowDialog() != true) return;

        File.WriteAllText(dlg.FileName, JsonConvert.SerializeObject(profile, Formatting.Indented));
        StatusMessage = $"Profile '{ProfileName}' saved.";
    }

    [RelayCommand]
    private void LoadProfile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Load Mapping Profile",
            Filter = "JSON Profile|*.profile.json;*.json",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var profile = JsonConvert.DeserializeObject<MappingProfile>(File.ReadAllText(dlg.FileName));
            if (profile == null) return;

            // Backward compat: migrate old field names
            profile.MigrateOldFields();

            ParentNodePath = profile.ParentNodePath;
            ChildNodePath = profile.ChildNodePath;
            ChildNodePaths.Clear();
            foreach (var cp in profile.ChildNodePaths)
                ChildNodePaths.Add(cp);
            UpdateChildPathDisplay();
            HierarchyMode = profile.Mode;
            IsTwoLevelMode = profile.Mode == HierarchyMode.TwoLevel;
            ProfileName = profile.Name;

            foreach (var saved in profile.Mappings)
            {
                var target = FieldMappings.FirstOrDefault(m =>
                    m.TargetField.Equals(saved.TargetField, StringComparison.OrdinalIgnoreCase));
                if (target == null)
                {
                    // Custom field from profile — add it
                    if (saved.IsCustom)
                    {
                        target = new FieldMapping
                        {
                            TargetField = saved.TargetField,
                            Description = "Custom field",
                            IsCustom = true,
                        };
                        FieldMappings.Add(target);
                    }
                    else continue;
                }
                target.SourcePath = saved.SourcePath;
                target.DefaultValue = saved.DefaultValue;
                target.IsEnabled = saved.IsEnabled;
                target.SourceLevel = saved.SourceLevel;
                target.ChildNodeKey = saved.ChildNodeKey;
                target.IsMapped = !string.IsNullOrWhiteSpace(saved.SourcePath);
            }

            MappedFieldCount = FieldMappings.Count(m => m.IsMapped);

            // Trigger auto-discovery if both paths are set
            TryAutoDiscover();

            StatusMessage = $"Profile '{profile.Name}' loaded — {MappedFieldCount} mappings.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error:\n{ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SELECTION CHANGED
    // ═══════════════════════════════════════════════════════════════════

    partial void OnSelectedNodeChanged(FileTreeNode? value)
    {
        if (value == null)
        {
            SelectedNodePath = string.Empty;
            SelectedNodeValue = string.Empty;
            SelectedNodeSamples = string.Empty;
            return;
        }

        SelectedNodePath = value.Path;
        SelectedNodeValue = value.Value;

        if (value.SampleValues.Count > 0)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Math.Min(value.SampleValues.Count, 5); i++)
            {
                var v = value.SampleValues[i];
                sb.AppendLine($"  [{i}]  {(v.Length > 100 ? v[..100] + "…" : v)}");
            }
            SelectedNodeSamples = sb.ToString().TrimEnd();
        }
        else
        {
            SelectedNodeSamples = string.Empty;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════════

    private void ClearNodeFlags(ObservableCollection<FileTreeNode> nodes, bool isParent)
    {
        foreach (var node in nodes)
        {
            if (isParent) node.IsParentNode = false;
            else node.IsChildNode = false;
            ClearNodeFlags(node.Children, isParent);
        }
    }
}
