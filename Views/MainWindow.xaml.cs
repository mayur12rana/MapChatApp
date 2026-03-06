using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ChatMapperApp.Models;
using ChatMapperApp.ViewModels;

namespace ChatMapperApp.Views;

public partial class MainWindow : Window
{
    private MainViewModel VM => (MainViewModel)DataContext;

    // Drag state for tree → field drag-drop
    private Point _dragStartPoint;
    private bool _isDragging;

    // Drag state for source node → field drag-drop
    private Point _sourceNodeDragStartPoint;
    private bool _isSourceNodeDragging;

    // Track dynamically added custom columns so we can remove them on re-extract
    private readonly List<DataGridColumn> _customColumns = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            VM.PropertyChanged += VM_PropertyChanged;
        };
    }

    private void VM_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ExtractedMessageCount))
            UpdateCustomColumns();
    }

    private void UpdateCustomColumns()
    {
        // Remove previously added custom columns
        foreach (var col in _customColumns)
            MessagesGrid.Columns.Remove(col);
        _customColumns.Clear();

        // Collect custom field names from both extracted messages and mapped custom FieldMappings
        var customFieldNames = VM.ExtractedMessages
            .SelectMany(m => m.CustomFields.Keys)
            .Concat(VM.FieldMappings
                .Where(m => m.IsCustom && m.IsMapped)
                .Select(m => m.TargetField))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();

        // Add a column for each custom field
        foreach (var fieldName in customFieldNames)
        {
            var col = new DataGridTextColumn
            {
                Header = fieldName,
                Binding = new Binding($"CustomFields[{fieldName}]"),
                Width = new DataGridLength(120),
            };
            MessagesGrid.Columns.Add(col);
            _customColumns.Add(col);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FILE DRAG-DROP (onto the window)
    // ═══════════════════════════════════════════════════════════════════

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else if (e.Data.GetDataPresent(typeof(FileTreeNode)) || e.Data.GetDataPresent(typeof(SourceNode)))
        {
            // These are internal drags, let them pass through
            return;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        if (files.Length == 0) return;

        var filePath = files[0];
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var supported = new HashSet<string>
            { ".json", ".xml", ".csv", ".tsv", ".html", ".htm", ".txt", ".msg", ".log" };

        if (!supported.Contains(ext))
        {
            MessageBox.Show($"Unsupported file type: {ext}\n\nSupported: JSON, XML, CSV, TSV, HTML, TXT",
                "Unsupported Format", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            VM.LoadFile(filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading file:\n{ex.Message}",
                "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  TREE SELECTION
    // ═══════════════════════════════════════════════════════════════════

    private void TreeViewItem_Selected(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem item && item.DataContext is FileTreeNode node)
        {
            VM.SelectedNode = node;
            e.Handled = true;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  TREE → FIELD DRAG-DROP
    //  Drag a node from the tree and drop it onto a field in the list
    // ═══════════════════════════════════════════════════════════════════

    private void FileTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _isDragging = false;
    }

    private void FileTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var diff = e.GetPosition(null) - _dragStartPoint;
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (_isDragging) return;

        // Find the TreeViewItem under the mouse
        var hit = e.OriginalSource as DependencyObject;
        while (hit != null && hit is not TreeViewItem)
            hit = VisualTreeHelper.GetParent(hit);

        if (hit is TreeViewItem tvi && tvi.DataContext is FileTreeNode node)
        {
            _isDragging = true;
            var data = new DataObject(typeof(FileTreeNode), node);
            DragDrop.DoDragDrop(FileTree, data, DragDropEffects.Link);
            _isDragging = false;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SOURCE NODE (B2) → FIELD (B3) DRAG-DROP
    // ═══════════════════════════════════════════════════════════════════

    private void SourceNodeList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _sourceNodeDragStartPoint = e.GetPosition(null);
        _isSourceNodeDragging = false;
    }

    private void SourceNodeList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var diff = e.GetPosition(null) - _sourceNodeDragStartPoint;
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (_isSourceNodeDragging) return;

        var hit = e.OriginalSource as DependencyObject;
        while (hit != null && hit is not ListBoxItem)
            hit = VisualTreeHelper.GetParent(hit);

        if (hit is ListBoxItem lbi && lbi.DataContext is SourceNode sourceNode)
        {
            _isSourceNodeDragging = true;
            var data = new DataObject(typeof(SourceNode), sourceNode);
            DragDrop.DoDragDrop(SourceNodeList, data, DragDropEffects.Link);
            _isSourceNodeDragging = false;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FIELD LIST — DROP TARGET (accepts both TreeNode and SourceNode)
    // ═══════════════════════════════════════════════════════════════════

    private void FieldList_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(FileTreeNode)) || e.Data.GetDataPresent(typeof(SourceNode)))
        {
            e.Effects = DragDropEffects.Link;

            // Highlight the item under cursor
            var pos = e.GetPosition(FieldList);
            var hitResult = VisualTreeHelper.HitTest(FieldList, pos);
            if (hitResult?.VisualHit != null)
            {
                var container = FindAncestor<ListBoxItem>(hitResult.VisualHit);
                if (container != null)
                    container.IsSelected = true;
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void FieldList_Drop(object sender, DragEventArgs e)
    {
        // Find which field item was dropped onto
        var pos = e.GetPosition(FieldList);
        var hitResult = VisualTreeHelper.HitTest(FieldList, pos);
        if (hitResult?.VisualHit == null) return;

        var container = FindAncestor<ListBoxItem>(hitResult.VisualHit);
        if (container?.DataContext is not FieldMapping mapping) return;

        if (e.Data.GetDataPresent(typeof(SourceNode)))
        {
            // Drop from B2 source node list
            var sourceNode = (SourceNode)e.Data.GetData(typeof(SourceNode))!;
            mapping.SourcePath = sourceNode.Path;
            mapping.SourceLevel = sourceNode.Level;
            mapping.ChildNodeKey = sourceNode.ChildKey;
            mapping.IsMapped = true;
            mapping.PreviewValue = sourceNode.SampleValue;

            VM.SelectedMapping = mapping;
            VM.MappedFieldCount = VM.FieldMappings.Count(m => m.IsMapped);
            VM.StatusMessage = $"Mapped:  {sourceNode.Name}  →  {mapping.TargetField}  ({sourceNode.Level})";
        }
        else if (e.Data.GetDataPresent(typeof(FileTreeNode)))
        {
            // Drop from tree
            var node = (FileTreeNode)e.Data.GetData(typeof(FileTreeNode))!;
            mapping.SourcePath = node.Path;
            mapping.IsMapped = true;

            // Determine level from node context (check all child paths)
            var matchedChildPath = VM.ChildNodePaths.FirstOrDefault(cp =>
                node.Path.Contains(cp.Split('/').Last().Split('[')[0]));
            if (matchedChildPath != null)
            {
                mapping.SourceLevel = SourceLevel.Child;
                mapping.ChildNodeKey = matchedChildPath.TrimStart('$', '.', '/').Split(new[] { '.', '/' }).Last().Split('[')[0];
            }
            else if (!string.IsNullOrEmpty(VM.ParentNodePath))
                mapping.SourceLevel = SourceLevel.Parent;

            if (node.SampleValues.Count > 0)
                mapping.PreviewValue = node.SampleValues[0];
            else if (!string.IsNullOrEmpty(node.Value))
                mapping.PreviewValue = node.Value;

            VM.SelectedMapping = mapping;
            VM.MappedFieldCount = VM.FieldMappings.Count(m => m.IsMapped);
            VM.StatusMessage = $"Mapped:  {node.Name}  →  {mapping.TargetField}";
        }

        e.Handled = true;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════════

    private static T? FindAncestor<T>(DependencyObject? obj) where T : DependencyObject
    {
        while (obj != null)
        {
            if (obj is T target) return target;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
    }
}
