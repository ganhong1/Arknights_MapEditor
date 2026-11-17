#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Arknights.MapEditor.Storage;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Editor
{
    internal sealed class DiagnosticEvent
    {
        internal string Id { get; set; } = Guid.NewGuid().ToString("N");
        internal DateTimeOffset Utc { get; set; } = DateTimeOffset.UtcNow;
        internal DateTimeOffset LastUtc { get; set; }
        internal int Count { get; set; } = 1;
        internal string Operation { get; set; } = "";
        internal string Level { get; set; } = "info";
        internal string Category { get; set; } = "operation";
        internal string Code { get; set; } = "OPERATION_COMPLETED";
        internal string OperationId { get; set; } = "";
        internal string? ParentOperationId { get; set; }
        internal string? Session { get; set; }
        internal string? DocumentId { get; set; }
        internal string? Revision { get; set; }
        internal string? PreviousRevision { get; set; }
        internal string Outcome { get; set; } = "success";
        internal double DurationMs { get; set; }
        internal string[] ExceptionTypes { get; set; } = Array.Empty<string>();
        internal string[] Stack { get; set; } = Array.Empty<string>();
        internal string[] CleanupExceptionTypes { get; set; } = Array.Empty<string>();
        internal string[] RelatedEventIds { get; set; } = Array.Empty<string>();
        internal string[] RelatedOperationIds { get; set; } = Array.Empty<string>();

        internal DiagnosticEvent Clone() => FromJson(ToJson());
        internal JObject ToJson() => new()
        {
            ["schemaVersion"] = 1, ["utc"] = Utc.ToString("O"), ["lastUtc"] = LastUtc.ToString("O"),
            ["id"] = Id, ["count"] = Count, ["operation"] = Operation, ["level"] = Level,
            ["category"] = Category, ["code"] = Code, ["operationId"] = OperationId,
            ["parentOperationId"] = ParentOperationId, ["previousRevision"] = PreviousRevision,
            ["session"] = Session, ["documentId"] = DocumentId, ["revision"] = Revision,
            ["outcome"] = Outcome, ["durationMs"] = DurationMs,
            ["exceptionTypes"] = new JArray(ExceptionTypes), ["stack"] = new JArray(Stack),
            ["cleanupExceptionTypes"] = new JArray(CleanupExceptionTypes),
            ["relatedEventIds"] = new JArray(RelatedEventIds), ["relatedOperationIds"] = new JArray(RelatedOperationIds)
        };

        internal static DiagnosticEvent FromJson(JObject value)
        {
            string Text(string key, string fallback = "") => value[key]?.Type == JTokenType.String ? value.Value<string>(key)! : fallback;
            string? Optional(string key) => value[key]?.Type == JTokenType.String ? value.Value<string>(key) : null;
            string[] Items(string key, int limit = 32) => (value[key] as JArray)?.Values<string>().OfType<string>().Take(limit).ToArray() ?? Array.Empty<string>();
            DateTimeOffset.TryParse(Text("utc"), out var utc);
            DateTimeOffset.TryParse(Text("lastUtc"), out var last);
            return new DiagnosticEvent
            {
                Id = Text("id"), Utc = utc, LastUtc = last == default ? utc : last,
                Count = value["count"]?.Type == JTokenType.Integer ? Math.Max(1, value.Value<int>("count")) : 1,
                Operation = Text("operation"), Level = Text("level", "info"), Category = Text("category"),
                Code = Text("code"), OperationId = Text("operationId"), Session = Optional("session"),
                ParentOperationId = Optional("parentOperationId"), PreviousRevision = Optional("previousRevision"),
                DocumentId = Optional("documentId"), Revision = Optional("revision"), Outcome = Text("outcome"),
                DurationMs = value["durationMs"]?.Type is JTokenType.Integer or JTokenType.Float ? value.Value<double>("durationMs") : 0,
                ExceptionTypes = Items("exceptionTypes"), Stack = Items("stack"), CleanupExceptionTypes = Items("cleanupExceptionTypes"),
                RelatedEventIds = Items("relatedEventIds", 64), RelatedOperationIds = Items("relatedOperationIds", 64)
            };
        }

        internal static DiagnosticEvent Failure(string operation, Exception error, string? session = null)
        {
            var category = error switch
            {
                OperationCanceledException => "cancelled",
                EditorOperationRejectedException => "validation",
                DocumentFileConflictException => "conflict",
                InvalidDataException or FormatException or JsonException or ArgumentException => "validation",
                IOException or UnauthorizedAccessException => "io",
                TimeoutException => "timeout",
                _ => "internal"
            };
            var codes = new Dictionary<string, string>
            {
                ["cancelled"] = "OPERATION_CANCELLED", ["conflict"] = "DOCUMENT_CONFLICT",
                ["validation"] = "VALIDATION_FAILED", ["io"] = "STORAGE_FAILED",
                ["timeout"] = "OPERATION_TIMEOUT", ["internal"] = "INTERNAL_ERROR"
            };
            var types = new List<string>(); var frames = new List<string>();
            for (var current = error; current != null && types.Count < 4; current = current.InnerException)
            {
                types.Add(current.GetType().FullName ?? current.GetType().Name);
                foreach (var frame in new StackTrace(current, false).GetFrames() ?? Array.Empty<StackFrame>())
                {
                    var method = frame.GetMethod();
                    if (method != null && frames.Count < 32) frames.Add(method.DeclaringType?.FullName + "." + method.Name);
                }
            }
            var result = new DiagnosticEvent
            {
                Operation = operation, Category = category, Code = codes[category], Session = session,
                Level = category is "validation" or "cancelled" ? "info" : category == "internal" ? "error" : "warning",
                Outcome = category == "cancelled" ? "cancelled" : "failed", ExceptionTypes = types.ToArray(), Stack = frames.ToArray()
            };
            if (error.Data["TemporaryCleanupFailure"] is Exception cleanup)
                result.CleanupExceptionTypes = new[] { cleanup.GetType().FullName ?? cleanup.GetType().Name };
            result.LastUtc = result.Utc;
            return result;
        }
    }

    internal sealed class DiagnosticHealth
    {
        internal int Pending { get; set; }
        internal long Dropped { get; set; }
        internal long WriteFailures { get; set; }
        internal string LastWriteError { get; set; } = "";
        internal bool IsHealthy => LastWriteError.Length == 0 && Pending >= 0;
    }
}
