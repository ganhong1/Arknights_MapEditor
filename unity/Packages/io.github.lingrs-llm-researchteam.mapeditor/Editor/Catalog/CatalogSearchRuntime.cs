#nullable enable
using System;
using Jint;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Catalog
{
    internal static class CatalogSearchRuntime
    {
        private const string Root = "Packages/io.github.lingrs-llm-researchteam.mapeditor/Editor/Catalog/";
        private static Engine? engine;
        private static (string Fuse, string Bridge)? scripts;

        private static (string Fuse, string Bridge) Scripts => scripts ??= (
            ReadAsset("SearchDependencies/Fuse.js.txt"), ReadAsset("SearchDependencies/TerrainSearch.js.txt"));

        internal static Engine Engine => engine ??= BuildEngine(Scripts);

        internal static CatalogSearch<T> CreateWorker<T>(Func<T, JObject> describe, Func<T[], string?>? prebuilt = null)
        {
            // Unity TextAssets are read here. Each worker owns its Jint engine and index.
            var source = Scripts;
            Engine? worker = null;
            return new CatalogSearch<T>(describe, () => worker ??= BuildEngine(source), prebuilt);
        }

        private static Engine BuildEngine((string Fuse, string Bridge) scripts)
        {
            var runtime = new Engine(options => options.TimeoutInterval(TimeSpan.FromSeconds(8)));
            runtime.Execute("var module = { exports: {} };");
            runtime.Execute(scripts.Fuse);
            runtime.SetValue("terrainBitapScore", new Func<string, string, double>(new TerrainBitap().Score));
            runtime.Execute(scripts.Bridge);
            return runtime;
        }

        internal static string ReadAsset(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + path);
            if (asset == null) throw new InvalidOperationException("缺少搜索资源 " + path);
            return asset.text;
        }
    }
}
