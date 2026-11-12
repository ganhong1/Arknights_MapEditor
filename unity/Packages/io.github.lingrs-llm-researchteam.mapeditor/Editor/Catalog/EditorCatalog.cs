#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Catalog
{
    public sealed class EditorCatalog
    {
        public const string AssetPath="Packages/io.github.lingrs-llm-researchteam.mapeditor/Editor/Catalog/catalog.json";
        public IReadOnlyList<CatalogEntry> Entries { get; }
        private EditorCatalog(List<CatalogEntry> entries) { Entries=new ReadOnlyCollection<CatalogEntry>(entries); }

        public static EditorCatalog LoadBundled()
        {
            var asset=AssetDatabase.LoadAssetAtPath<TextAsset>(AssetPath);
            if(asset==null)throw new FileNotFoundException("Bundled editor catalog was not imported.",AssetPath);
            return LoadJson(asset.text);
        }

        public static EditorCatalog LoadJson(string text)
        {
            var root=JObject.Parse(text,new JsonLoadSettings {DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
            if(root["version"]?.Type!=JTokenType.Integer || root["version"]!.Value<int>()!=1 || !(root["entries"] is JArray entries))
                throw new InvalidDataException("Unsupported editor catalog.");
            var result=new List<CatalogEntry>();var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var token in entries)
            {
                if(!(token is JObject entry))throw new InvalidDataException("Catalog entry must be object.");
                var id=Field(entry,"id");var category=Field(entry,"category");
                if(string.IsNullOrWhiteSpace(id)||!ids.Add(id))throw new InvalidDataException("Empty or duplicate catalog ID.");
                var template=entry["template"];
                if(template==null || (template.Type!=JTokenType.Null && template.Type!=JTokenType.Object))throw new InvalidDataException("Invalid catalog template.");
                if(entry["visualDefinitionAvailable"]?.Type!=JTokenType.Boolean)throw new InvalidDataException("Missing visual flag.");
                if(template is JObject tile && (category!="terrain" || tile["tileKey"]?.Type!=JTokenType.String || tile["tileKey"]!.Value<string>()!=id))
                    throw new InvalidDataException("Template identity does not match terrain entry.");
                var family = entry["family"] == null ? null : Field(entry, "family");
                var visualOnly = entry["visualOnly"];
                if (family != null && string.IsNullOrWhiteSpace(family))
                    throw new InvalidDataException("Empty terrain family.");
                if (visualOnly != null && visualOnly.Type != JTokenType.Boolean)
                    throw new InvalidDataException("Invalid visual-only flag.");
                result.Add(new CatalogEntry(id, Field(entry, "name"), Field(entry, "description"), category, template as JObject,
                    entry["visualDefinitionAvailable"]!.Value<bool>(), family, visualOnly?.Value<bool>() ?? false));
            }
            return new EditorCatalog(result);
        }

        public CatalogEntry? Find(string id) => Entries.FirstOrDefault(x=>string.Equals(x.Id,id,StringComparison.Ordinal));
        private static readonly CatalogSearch<CatalogEntry> EntrySearch = new CatalogSearch<CatalogEntry>(
            TerrainSearchRuntime.Terrain);

        public IReadOnlyList<CatalogEntry> Search(string query, string? category = null)
            => Search(query, category, out _);

        internal IReadOnlyList<CatalogEntry> Search(string query, string? category, out string? error)
        {
            return EntrySearch.Filter(Entries, query, out error)
                .Where(entry => category == null || entry.Category == category).ToList().AsReadOnly();
        }

        public static IReadOnlyList<ImportedObjectEntry> DocumentObjects(JObject level)
        {
            if(level==null)throw new ArgumentNullException(nameof(level));
            var result=new List<ImportedObjectEntry>();
            foreach(var scope in new[]{"predefines","hardPredefines"})
            foreach(var kind in new[]{"characterInsts","tokenInsts","characterCards","tokenCards"})
            {
                if(!(level[scope]?[kind] is JArray items))continue;
                foreach(var item in items.OfType<JObject>())
                {
                    var id=item["inst"]?["characterKey"]?.Type==JTokenType.String ? item["inst"]!["characterKey"]!.Value<string>()! : "(未提供ID)";
                    var category=kind.EndsWith("Cards",StringComparison.Ordinal)?"card":item["hidden"]?.Type==JTokenType.Boolean && item["hidden"]!.Value<bool>()?"hidden":"preplaced";
                    result.Add(new ImportedObjectEntry(id,category,item.Path,item));
                }
            }
            foreach(var action in level.SelectTokens("$.waves[*].fragments[*].actions[*]").Concat(level.SelectTokens("$.branches.*.phases[*].actions[*]")).OfType<JObject>())
            {
                var type=action["actionType"]?.ToString();
                if(type!="TRIGGER_PREDEFINED" && type!="ACTIVATE_PREDEFINED" && type!="WITHDRAW_PREDEFINED" && type!="6" && type!="8" && type!="10")continue;
                var id=action["key"]?.Type==JTokenType.String?action["key"]!.Value<string>()!:"(未提供key)";
                result.Add(new ImportedObjectEntry(id,"native-action",action.Path,action));
            }
            return result.AsReadOnly();
        }

        private static string Field(JObject root,string key)
        { if(root[key]?.Type!=JTokenType.String)throw new InvalidDataException("Missing catalog string: "+key);return root[key]!.Value<string>()!; }
    }
}
