using System.IO;
using System.Text;
using System.Xml.Linq;
using ChatMapperApp.Models;
using Newtonsoft.Json;

namespace ChatMapperApp.Services;

public static class ExportService
{
    public static void ExportToCsv(IReadOnlyList<ChatMessage> messages, string outputPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\"MessageId\",\"Timestamp\",\"SenderName\",\"SenderId\",\"RecipientName\",\"RecipientId\"," +
                       "\"MessageBody\",\"MessageType\",\"ChannelName\",\"ChannelId\",\"ThreadId\",\"ParentMessageId\"," +
                       "\"HasAttachment\",\"AttachmentNames\",\"SourcePlatform\",\"SourceFile\",\"IsEdited\",\"IsDeleted\",\"ExtendedProperties\"");

        foreach (var m in messages)
        {
            sb.AppendLine(string.Join(",",
                Esc(m.MessageId), Esc(m.Timestamp), Esc(m.SenderName), Esc(m.SenderId),
                Esc(m.RecipientName), Esc(m.RecipientId), Esc(m.MessageBody), Esc(m.MessageType),
                Esc(m.ChannelName), Esc(m.ChannelId), Esc(m.ThreadId), Esc(m.ParentMessageId),
                Esc(m.HasAttachment.ToString()), Esc(m.AttachmentNames), Esc(m.SourcePlatform),
                Esc(m.SourceFile), Esc(m.IsEdited.ToString()), Esc(m.IsDeleted.ToString()),
                Esc(m.ExtendedProperties)));
        }

        File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(true));
    }

    public static void ExportToJson(IReadOnlyList<ChatMessage> messages, string outputPath)
    {
        var envelope = new
        {
            ExportedAt = DateTime.UtcNow.ToString("o"),
            TotalMessages = messages.Count,
            Messages = messages
        };
        File.WriteAllText(outputPath, JsonConvert.SerializeObject(envelope, Formatting.Indented), Encoding.UTF8);
    }

    public static void ExportToXml(IReadOnlyList<ChatMessage> messages, string outputPath)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement("ChatExport",
                new XAttribute("ExportedAt", DateTime.UtcNow.ToString("o")),
                new XAttribute("TotalMessages", messages.Count),
                messages.Select(m => new XElement("Message",
                    Elem("MessageId", m.MessageId), Elem("Timestamp", m.Timestamp),
                    Elem("SenderName", m.SenderName), Elem("SenderId", m.SenderId),
                    Elem("RecipientName", m.RecipientName), Elem("RecipientId", m.RecipientId),
                    Elem("MessageBody", m.MessageBody), Elem("MessageType", m.MessageType),
                    Elem("ChannelName", m.ChannelName), Elem("ChannelId", m.ChannelId),
                    Elem("ThreadId", m.ThreadId), Elem("ParentMessageId", m.ParentMessageId),
                    new XElement("HasAttachment", m.HasAttachment),
                    Elem("AttachmentNames", m.AttachmentNames),
                    Elem("SourcePlatform", m.SourcePlatform), Elem("SourceFile", m.SourceFile),
                    new XElement("IsEdited", m.IsEdited), new XElement("IsDeleted", m.IsDeleted),
                    Elem("ExtendedProperties", m.ExtendedProperties)
                ))
            )
        );
        doc.Save(outputPath);
    }

    private static string Esc(string? v) => v == null ? "\"\"" : $"\"{v.Replace("\"", "\"\"")}\"";
    private static XElement Elem(string name, string? v) => new(name, v ?? "");
}
