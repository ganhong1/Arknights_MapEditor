#nullable enable
using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Storage
{
    /// <summary>Persists only the explicitly selected recovery cache; source paths remain inert data.</summary>
    public static class RecoveryStore
    {
        private const int Mebibyte = 1024 * 1024;
        // The project draft contains both the current and saved art JSON, already escaped.
        private const int MaximumProjectBytes = 128 * Mebibyte + 64 * 1024;
        // The escaped envelope has a separate ceiling from its individual fields.
        private const int MaximumEnvelopeBytes = 256 * Mebibyte;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static string Save(string cachePath, RecoverySnapshot snapshot, string? expectedHash)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            CheckPayloads(snapshot);
            var root = new JObject
            {
                ["version"] = 2,
                [nameof(snapshot.DocumentJson)] = snapshot.DocumentJson,
                [nameof(snapshot.DraftJson)] = snapshot.DraftJson,
                [nameof(snapshot.SavedJson)] = snapshot.SavedJson,
                [nameof(snapshot.SourcePath)] = snapshot.SourcePath,
                [nameof(snapshot.SourceHash)] = snapshot.SourceHash,
                [nameof(snapshot.ProjectJson)] = snapshot.ProjectJson,
                [nameof(snapshot.InspectorJson)] = snapshot.InspectorJson
            };
            return DocumentFileStore.Save(cachePath, root.ToString(Formatting.None), expectedHash, MaximumEnvelopeBytes).Sha256;
        }

        public static RecoverySnapshot Read(string cachePath, out string hash)
        {
            hash = string.Empty;
            var file = DocumentFileStore.Read(cachePath, MaximumEnvelopeBytes);
            try
            {
                using (var text = new StringReader(file.Text))
                using (var reader = new JsonTextReader(text) { MaxDepth = 4, DateParseHandling = DateParseHandling.None })
                {
                    var root = JObject.Load(reader, new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        CommentHandling = CommentHandling.Load
                    });
                    if (reader.Read()) throw new InvalidDataException("Recovery envelope has trailing content.");
                    var version = root["version"]?.Type == JTokenType.Integer ? root["version"]!.ToString(Formatting.None) : "";
                    if ((version != "1" && version != "2") || root.Count != (version == "1" ? 6 : 8))
                        throw new InvalidDataException("Unsupported or malformed recovery envelope version.");
                    var snapshot = new RecoverySnapshot(
                        Field(root, "DocumentJson"), Field(root, "DraftJson"), Field(root, "SavedJson"),
                        Field(root, "SourcePath"), Field(root, "SourceHash"),
                        version == "1" ? "" : Field(root,"ProjectJson"),
                        version == "1" ? "" : Field(root,"InspectorJson"));
                    CheckPayloads(snapshot);
                    hash = file.Sha256;
                    return snapshot;
                }
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Recovery envelope is not valid JSON.", exception);
            }
        }

        internal static string ReadHash(string path) => DocumentFileStore.ReadHash(path, MaximumEnvelopeBytes);

        private static void CheckPayloads(RecoverySnapshot snapshot)
        {
            CheckSize(snapshot.DocumentJson, nameof(snapshot.DocumentJson));
            CheckSize(snapshot.DraftJson, nameof(snapshot.DraftJson));
            CheckSize(snapshot.SavedJson, nameof(snapshot.SavedJson));
            CheckSize(snapshot.ProjectJson, nameof(snapshot.ProjectJson), MaximumProjectBytes);
            CheckSize(snapshot.InspectorJson, nameof(snapshot.InspectorJson));
            CheckSize(snapshot.SourcePath, nameof(snapshot.SourcePath), 32 * 1024);
            CheckSize(snapshot.SourceHash, nameof(snapshot.SourceHash), 32 * 1024);
        }

        private static void CheckSize(string value, string name, int limit = DocumentFileStore.MaximumDocumentBytes)
        {
            if (value.Length > limit || StrictUtf8.GetByteCount(value) > limit)
                throw new InvalidDataException("Recovery field exceeds its size limit: " + name);
        }

        private static string Field(JObject root, string name)
        {
            var token = root[name];
            if (token == null || token.Type != JTokenType.String)
                throw new InvalidDataException("Recovery field must be a string: " + name);
            return token.Value<string>()!;
        }
    }
}
