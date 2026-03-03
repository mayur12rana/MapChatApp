using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChatMapperApp.Models;

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
    [ObservableProperty] private bool _isStartNode;
    [ObservableProperty] private bool _isEndNode;

    public ObservableCollection<FileTreeNode> Children { get; } = new();
    public FileTreeNode? Parent { get; set; }

    /// <summary>Sample values collected from the first few matching entries.</summary>
    public List<string> SampleValues { get; set; } = new();

    public string FullPath => Path;
}

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
//  FILE FORMAT
// ═══════════════════════════════════════════════════════════════════════════

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

// ═══════════════════════════════════════════════════════════════════════════
//  MAPPING PROFILE — saved configuration for reuse
// ═══════════════════════════════════════════════════════════════════════════

public class MappingProfile
{
    public string Name { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public FileFormat Format { get; set; }
    public string StartNodePath { get; set; } = string.Empty;
    public string EndNodePath { get; set; } = string.Empty;
    public List<FieldMappingSerialized> Mappings { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Serializable DTO for profile saving (no ObservableObject overhead).</summary>
public class FieldMappingSerialized
{
    public string TargetField { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string DefaultValue { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}
