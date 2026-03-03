# Chat Export Mapper v2.0

WPF desktop application for visually mapping chat export fields to a normalized schema.  
Uses **Windows native theme** — no custom dark theme.

## What Changed (v2 vs v1)

| Area | v1 | v2 |
|------|----|----|
| Theme | Custom dark theme (AppTheme.xaml) | **Windows native theme** — standard WPF controls |
| Regex | Per-field regex pattern, group, tester | **Removed** — direct node/element value extraction |
| Drag & Drop | File drop only on overlay | **File drop anywhere** on window + **tree node → field** drag-drop |
| Tree View | Emoji icons, custom colors | **Clean typed icons** ({ }, [ ], =, @, < >) with semantic colors |
| Field Mapping | Regex + Transform + Group | **Source path + Default value** only |
| Model | FieldMapping had 12 properties | **Simplified to 7** properties |

## Layout

```
┌─────────────────────────────────────────────────────────────────────┐
│  📂 Open  │ 📥 Load Profile │ 📤 Save │ ⚡ Extract │ 💾 Export │  │
├────────────────────────┬────────────────────────────────────────────┤
│                        │                                            │
│  📁 File Structure     │  ⚙ Field Mapping (12 mapped)              │
│                        │                                            │
│  slack_export.json     │  Field List      │  Detail Panel           │
│  ├ { } [0]             │  ● Timestamp     │  ┌──────────────────┐  │
│  │ ├ ts = 17065...     │  ● SenderName    │  │ Source Path      │  │
│  │ ├ text = Hey...     │  ● MessageBody   │  │ $.msg[0].text    │  │
│  │ └ user = U012...    │  ○ ChannelName   │  │                  │  │
│  ├ { } [1]             │  ○ MessageId     │  │ Default Value    │  │
│  └ …                   │                  │  │ (empty)          │  │
│                        │                  │  │                  │  │
│  ▶ START: $.messages   │                  │  │ Preview:         │  │
│                        │                  │  │ "Hello team..."  │  │
├────────────────────────┴────────────────────────────────────────────┤
│  📊 Extracted Messages (45 rows)                                    │
│  MsgId  │ Timestamp │ Sender  │ MessageBody             │ Channel  │
│  abc123 │ 2024-01.. │ Sarah   │ Hey team, quarterly...  │ general  │
└─────────────────────────────────────────────────────────────────────┘
```

## Drag & Drop

1. **Drop a file** anywhere on the window to load it
2. **Drag a tree node** from the left panel and **drop it on a field** in the right panel to assign the mapping
3. Or select both and click **🔗 Assign Node**

## Workflow

1. **Open / drag** a chat export file (JSON, XML, CSV, HTML, TXT)
2. **Browse** the tree — nodes show type, name, value
3. **Select** the repeating array node → click **▶ Start**
4. **Map fields** — drag tree nodes onto fields, or select + Assign
5. Click **⚡ Extract** to parse all messages
6. Review in DataGrid, then **💾 Export** to CSV/JSON/XML

## Normalized Fields

| Field | Description |
|---|---|
| MessageId | Unique message identifier |
| Timestamp | Message timestamp |
| SenderName | Sender display name |
| SenderId | Platform sender ID |
| RecipientName | Recipient display name |
| RecipientId | Platform recipient ID |
| MessageBody | Plain-text content |
| MessageType | Text / File / Image / System |
| ChannelName | Channel or room name |
| ChannelId | Platform channel ID |
| ThreadId | Thread identifier |
| ParentMessageId | Reply-to message ID |
| HasAttachment | true/false |
| AttachmentNames | Semicolon-delimited filenames |
| SourcePlatform | Platform name |
| SourceFile | Original file path |
| IsEdited | true/false |
| IsDeleted | true/false |
| ExtendedProperties | Additional JSON metadata |

## Build

```bash
dotnet build -c Release
dotnet run
```

## Requirements

- Windows 10/11  
- .NET 8.0 SDK
