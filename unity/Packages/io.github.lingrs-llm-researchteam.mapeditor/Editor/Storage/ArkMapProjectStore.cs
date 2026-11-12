#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Storage
{
    public static class ArkMapProjectStore
    {
        public static string Save(string path, ArkMapProject project, string? expectedHash)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            SafeProjectPath.RejectLinks(path);
            var items = new JArray();
            foreach (var item in project.ArtMappings)
                items.Add(new JObject { ["key"] = item.Key, ["resourcePath"] = item.ResourcePath, ["unityGuid"] = item.UnityGuid });
            var root = new JObject { ["version"] = 1, ["levelDataPath"] = project.LevelDataPath, ["artMappings"] = items };
            return DocumentFileStore.Save(path, root.ToString(Formatting.Indented), expectedHash).Sha256;
        }

        public static ArkMapProject Read(string path, out string hash)
        {
            hash = string.Empty; SafeProjectPath.RejectLinks(path);
            var file = DocumentFileStore.Read(path);
            try
            {
                using (var text = new StringReader(file.Text))
                using (var reader = new JsonTextReader(text) { MaxDepth = 8, DateParseHandling = DateParseHandling.None })
                {
                    var root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    if (reader.Read() || root.Count != 3 || root["version"]?.Type != JTokenType.Integer || root["version"]!.ToString(Formatting.None) != "1")
                        throw new InvalidDataException("Unsupported or malformed arkmap envelope.");
                    if (!(root["artMappings"] is JArray items)) throw new InvalidDataException("artMappings must be an array.");
                    var mappings = new List<ArtMapping>();
                    foreach (var token in items)
                    {
                        if (!(token is JObject item) || item.Count != 3 || item["unityGuid"] == null)
                            throw new InvalidDataException("Malformed art mapping.");
                        var guid = item["unityGuid"]!.Type == JTokenType.Null ? null : Field(item, "unityGuid");
                        mappings.Add(new ArtMapping(Field(item,"key"), Field(item,"resourcePath"),guid));
                    }
                    var project = new ArkMapProject(Field(root,"levelDataPath"),mappings);
                    hash = file.Sha256; return project;
                }
            }
            catch (JsonException exception) { throw new InvalidDataException("Malformed arkmap JSON.",exception); }
            catch (ArgumentException exception) { throw new InvalidDataException("Invalid arkmap reference.",exception); }
        }

        public static string ResolveLevelDataPath(string projectFilePath, ArkMapProject project)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            return Resolve(projectFilePath,project.LevelDataPath);
        }

        public static string ResolveResourcePath(string projectFilePath, ArtMapping mapping)
        {
            if (mapping == null) throw new ArgumentNullException(nameof(mapping));
            return Resolve(projectFilePath,mapping.ResourcePath);
        }

        private static string Resolve(string projectFilePath,string relative)
        {
            var root = Path.GetDirectoryName(Path.GetFullPath(projectFilePath))!;
            var result = Path.GetFullPath(Path.Combine(root,SafeProjectPath.Validate(relative).Replace('/',Path.DirectorySeparatorChar)));
            SafeProjectPath.RejectLinks(result);
            return result;
        }

        private static string Field(JObject root,string name)
        {
            if(root[name]?.Type != JTokenType.String) throw new InvalidDataException("Expected string field: " + name);
            return root[name]!.Value<string>()!;
        }
    }
}
