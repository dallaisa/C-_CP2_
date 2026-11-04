using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExpenseHub.Api.Messaging;

/// <summary>The only message input: a persisted report ID, never identity or scope.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ReportMessage([property: JsonPropertyName("reportId")] Guid ReportId)
{
    /// <summary>Serializes only reportId to UTF-8 JSON.</summary>
    internal byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(this);

    /// <summary>Validates a delivery without accepting additional authority-bearing fields.</summary>
    internal static ReportMessage? Parse(ReadOnlyMemory<byte> body)
    {
        if (body.Length > 1024)
        {
            return null;
        }

        try
        {
            ReportMessage? message = JsonSerializer.Deserialize<ReportMessage>(body.Span);
            return message?.ReportId is null || message.ReportId == Guid.Empty ? null : message;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
