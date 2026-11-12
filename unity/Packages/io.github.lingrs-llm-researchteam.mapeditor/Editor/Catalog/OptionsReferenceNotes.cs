#nullable enable
using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Catalog
{
    /// <summary>Reviewed context notes only; never a whitelist or an automatic conversion rule.</summary>
    internal static class OptionsReferenceNotes
    {
        private const string Path = "Packages/io.github.lingrs-llm-researchteam.mapeditor/Editor/Catalog/options-blackboard-notes.json";
        private static JObject? notes;
        private static string? loadError;

        internal static string SearchText(string key)
        {
            ForKey(key);
            return notes?.GetValue(key, StringComparison.Ordinal)?.ToString() ?? key;
        }

        internal static string ForKey(string key)
        {
            if (notes == null && loadError == null)
            {
                try
                {
                    var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(Path);
                    if (asset == null) throw new InvalidOperationException("缺少黑板说明资源。");
                    var root = JObject.Parse(asset.text);
                    if (root.Value<int>("version") != 1 || !(root["keys"] is JObject entries))
                        throw new FormatException("黑板说明格式不匹配。");
                    notes = entries;
                }
                catch (Exception error) when (error is InvalidOperationException || error is FormatException || error is Newtonsoft.Json.JsonException)
                { loadError = "逐键说明暂不可用：" + error.Message; }
            }
            if (loadError != null) return loadError;
            if (!(notes!.GetValue(key, StringComparison.Ordinal) is JObject record))
                return "此键未收录说明来源。";
            return record.Value<string>("status") == "sample-only"
                ? "来源：原版关卡 LevelData。"
                : "取值来源：原版关卡 LevelData。\n读取逻辑来源：PC 2.7.81 客户端。";
        }
    }
}
