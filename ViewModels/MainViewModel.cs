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

    // Mapping
    [ObservableProperty] private string _startNodePath = string.Empty;
    [ObservableProperty] private string _endNodePath = string.Empty;
    [ObservableProperty] private FieldMapping? _selectedMapping;

    // Results
    [ObservableProperty] private int _extractedMessageCount;
    [ObservableProperty] private int _mappedFieldCount;
    [ObservableProperty] private string _profileName = string.Empty;

    public ObservableCollection<FileTreeNode> TreeNodes { get; } = new();
    public ObservableCollection<FieldMapping> FieldMappings { get; } = new();
    public ObservableCollection<ChatMessage> ExtractedMessages { get; } = new();

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

        // Reset all mappings
        StartNodePath = string.Empty;
        EndNodePath = string.Empty;
        foreach (var m in FieldMappings)
        {
            m.SourcePath = string.Empty;
            m.DefaultValue = string.Empty;
            m.IsMapped = false;
            m.PreviewValue = string.Empty;
        }
        ExtractedMessages.Clear();
        ExtractedMessageCount = 0;
        MappedFieldCount = 0;

        StatusMessage = $"Loaded {LoadedFileName} — {FormatDisplayName}, {RawContentLineCount:N0} lines";
    }

    // ═══════════════════════════════════════════════════════════════════
    //  START / END NODE
    // ═══════════════════════════════════════════════════════════════════

    [RelayCommand]
    private void SetStartNode()
    {
        if (SelectedNode == null) return;
        ClearNodeFlags(TreeNodes, isStart: true);
        SelectedNode.IsStartNode = true;
        StartNodePath = SelectedNode.Path;
        StatusMessage = $"Start node ▶ {StartNodePath}";
    }

    [RelayCommand]
    private void SetEndNode()
    {
        if (SelectedNode == null) return;
        ClearNodeFlags(TreeNodes, isStart: false);
        SelectedNode.IsEndNode = true;
        EndNodePath = SelectedNode.Path;
        StatusMessage = $"End node ■ {EndNodePath}";
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ASSIGN / CLEAR FIELD
    // ═══════════════════════════════════════════════════════════════════

    [RelayCommand]
    private void AssignToField()
    {
        if (SelectedNode == null || SelectedMapping == null) return;

        SelectedMapping.SourcePath = SelectedNode.Path;
        SelectedMapping.IsMapped = true;

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
        }
        StartNodePath = string.Empty;
        EndNodePath = string.Empty;
        ClearNodeFlags(TreeNodes, true);
        ClearNodeFlags(TreeNodes, false);
        ExtractedMessages.Clear();
        ExtractedMessageCount = 0;
        MappedFieldCount = 0;
        StatusMessage = "All mappings cleared.";
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
            var messages = MappingService.ExtractMessages(
                RawContent, DetectedFormat, StartNodePath, EndNodePath, FieldMappings.ToList());

            foreach (var msg in messages)
            {
                if (string.IsNullOrEmpty(msg.SourceFile)) msg.SourceFile = LoadedFileName;
                if (string.IsNullOrEmpty(msg.MessageId)) msg.MessageId = Guid.NewGuid().ToString("N")[..12];
            }

            ExtractedMessages.Clear();
            foreach (var msg in messages) ExtractedMessages.Add(msg);
            ExtractedMessageCount = messages.Count;
            StatusMessage = $"Extracted {messages.Count} messages from {LoadedFileName}";
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
            StartNodePath = StartNodePath,
            EndNodePath = EndNodePath,
            Mappings = FieldMappings.Select(m => new FieldMappingSerialized
            {
                TargetField = m.TargetField,
                SourcePath = m.SourcePath,
                DefaultValue = m.DefaultValue,
                IsEnabled = m.IsEnabled,
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

            StartNodePath = profile.StartNodePath;
            EndNodePath = profile.EndNodePath;
            ProfileName = profile.Name;

            foreach (var saved in profile.Mappings)
            {
                var target = FieldMappings.FirstOrDefault(m =>
                    m.TargetField.Equals(saved.TargetField, StringComparison.OrdinalIgnoreCase));
                if (target == null) continue;
                target.SourcePath = saved.SourcePath;
                target.DefaultValue = saved.DefaultValue;
                target.IsEnabled = saved.IsEnabled;
                target.IsMapped = !string.IsNullOrWhiteSpace(saved.SourcePath);
            }

            MappedFieldCount = FieldMappings.Count(m => m.IsMapped);
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

    private void ClearNodeFlags(ObservableCollection<FileTreeNode> nodes, bool isStart)
    {
        foreach (var node in nodes)
        {
            if (isStart) node.IsStartNode = false;
            else node.IsEndNode = false;
            ClearNodeFlags(node.Children, isStart);
        }
    }
}
