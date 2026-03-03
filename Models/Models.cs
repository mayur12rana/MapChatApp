using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChatMapperApp.Models;

// ═══════════════════════════════════════════════════════════════════════════
//  ENUMS
// ═══════════════════════════════════════════════════════════════════════════

public enum NodeType
{
    Root,
    Object,
    Array,
    Property,
    Value,
    Attribute,
    Element,
    CsvColumn,
    Text
}

public enum FileFormat
{
    Unknown,
    Json,
    Xml,
    Csv,
    Tsv,
    Html,
    PlainText
}

/// <summary>Whether a source field comes from the parent or child level.</summary>
public enum SourceLevel
{
    None,
    Parent,
    Child
}

/// <summary>Extraction mode: two-level parent→child or flat single-level.</summary>
public enum HierarchyMode
{
    TwoLevel,
    Flat
}

// ═══════════════════════════════════════════════════════════════════════════
//  TREE NODE — represents a JSON node / XML element / CSV column
// ═══════════════════════════════════════════════════════════════════════════

public partial class FileTreeNode : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _path = string.Empty;
    [ObservableProperty] private string _value = string.Empty;
    [ObservableProperty] private NodeType _nodeType = NodeType.Object;
    [ObservableProperty] private int _depth;
    [ObservableProperty] private int _childCount;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isParentNode;
    [ObservableProperty] private bool _isChildNode;

    public ObservableCollection<FileTreeNode> Children { get; } = new();
    public FileTreeNode? Parent { get; set; }

    /// <summary>Sample values collected from the first few matching entries.</summary>
    public List<string> SampleValues { get; set; } = new();

    public string FullPath => Path;
}

// ═══════════════════════════════════════════════════════════════════════════
//  SOURCE NODE — a discovered field available for mapping (Zone B2)
// ═══════════════════════════════════════════════════════════════════════════

public partial class SourceNode : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _path = string.Empty;
    [ObservableProperty] private string _sampleValue = string.Empty;
    [ObservableProperty] private SourceLevel _level;
    [ObservableProperty] private NodeType _nodeType = NodeType.Value;
    [ObservableProperty] private string _groupName = string.Empty;
    [ObservableProperty] private int _repeatCount;
    [ObservableProperty] private bool _isSelected;
}

// ═══════════════════════════════════════════════════════════════════════════
//  FIELD MAPPING — maps a source node path directly to a ChatMessage field
//  (No regex — direct node/element value extraction)
// ═══════════════════════════════════════════════════════════════════════════

public partial class FieldMapping : ObservableObject
{
    [ObservableProperty] private string _targetField = string.Empty;
    [ObservableProperty] private string _sourcePath = string.Empty;
    [ObservableProperty] private string _defaultValue = string.Empty;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private bool _isMapped;
    [ObservableProperty] private string _previewValue = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private bool _isRequired;
    [ObservableProperty] private SourceLevel _sourceLevel;
}

// ═══════════════════════════════════════════════════════════════════════════
//  CHAT MESSAGE — normalized output row
// ═══════════════════════════════════════════════════════════════════════════

public class ChatMessage
{
    public string MessageId { get; set; } = string.Empty;
    public string? Timestamp { get; set; }
    public string? SenderName { get; set; }
    public string? SenderId { get; set; }
    public string? RecipientName { get; set; }
    public string? RecipientId { get; set; }
    public string? MessageBody { get; set; }
    public string? MessageType { get; set; }
    public string? ChannelName { get; set; }
    public string? ChannelId { get; set; }
    public string? ThreadId { get; set; }
    public string? ParentMessageId { get; set; }
    public bool HasAttachment { get; set; }
    public string? AttachmentNames { get; set; }
    public string? SourcePlatform { get; set; }
    public string? SourceFile { get; set; }
    public bool IsEdited { get; set; }
    public bool IsDeleted { get; set; }
    public string? ExtendedProperties { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════════
//  MAPPING PROFILE — saved configuration for reuse
// ═══════════════════════════════════════════════════════════════════════════

public class MappingProfile
{
    public string Name { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public FileFormat Format { get; set; }
    public HierarchyMode Mode { get; set; } = HierarchyMode.TwoLevel;

    // Current property names
    public string ParentNodePath { get; set; } = string.Empty;
    public string ChildNodePath { get; set; } = string.Empty;

    // Backward compat: old profiles may have these
    public string? StartNodePath { get; set; }
    public string? EndNodePath { get; set; }

    public List<FieldMappingSerialized> Mappings { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>After deserialization, migrate old field names if present.</summary>
    public void MigrateOldFields()
    {
        if (!string.IsNullOrEmpty(StartNodePath) && string.IsNullOrEmpty(ParentNodePath))
            ParentNodePath = StartNodePath;
        if (!string.IsNullOrEmpty(EndNodePath) && string.IsNullOrEmpty(ChildNodePath))
            ChildNodePath = EndNodePath;
    }
}

/// <summary>Serializable DTO for profile saving (no ObservableObject overhead).</summary>
public class FieldMappingSerialized
{
    public string TargetField { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string DefaultValue { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public SourceLevel SourceLevel { get; set; } = SourceLevel.None;
}
