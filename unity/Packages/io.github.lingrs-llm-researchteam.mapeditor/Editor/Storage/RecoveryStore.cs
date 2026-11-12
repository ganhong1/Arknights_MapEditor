#nullable enable
using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Storage
{
    /// <summary>Persists only the explicitly selected recovery cache; source paths remain inert data.</summary>
    public static class RecoveryStore
    {
        public static string Save(string cachePath, RecoverySnapshot snapshot, string? expectedHash)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
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
            return DocumentFileStore.Save(cachePath, root.ToString(Formatting.None), expectedHash).Sha256;
        }

        public static RecoverySnapshot Read(string cachePath, out string hash)
        {
            hash = string.Empty;
            var file = DocumentFileStore.Read(cachePath);
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
                    hash = file.Sha256;
                    return snapshot;
                }
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Recovery envelope is not valid JSON.", exception);
            }
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
