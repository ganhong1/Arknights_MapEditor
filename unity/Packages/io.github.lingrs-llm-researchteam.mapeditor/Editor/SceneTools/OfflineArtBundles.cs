#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Arknights.MapEditor.Catalog;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Editor
{
    internal sealed class OfflineArtBundles : IDisposable
    {
        private readonly Dictionary<string, AssetBundle> bundles = new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);
        private readonly List<AssetBundle> acquired = new List<AssetBundle>();
        private AssetBundleCreateRequest? pending;

        internal OfflineArtBundles() => AssemblyReloadEvents.beforeAssemblyReload += Dispose;

        internal async Task LoadAsync(OfflineArtCatalog catalog, IEnumerable<OfflineArtEntry> entries, CancellationToken cancellation)
        {
            foreach (var path in catalog.LoadOrder(entries))
            {
                cancellation.ThrowIfCancellationRequested();
                if (bundles.ContainsKey(path)) continue;
                var file = catalog.BundleFile(path);
                if (!File.Exists(file)) throw new FileNotFoundException("美术包文件缺失 " + path, file);
                var request = pending = AssetBundle.LoadFromFileAsync(file);
                if (!request.isDone)
                {
                    var completion = new TaskCompletionSource<bool>();
                    request.completed += _ => completion.TrySetResult(true);
                    await completion.Task;
                }
                if (pending != request) throw new OperationCanceledException();
                pending = null;
                var bundle = request.assetBundle;
                if (bundle == null) throw new InvalidDataException("Unity 无法读取美术包 " + path);
                acquired.Add(bundle);
                bundles.Add(path, bundle);
            }
            cancellation.ThrowIfCancellationRequested();
        }

        internal bool Contains(OfflineArtEntry entry) => bundles[entry.Bundle].Contains(entry.UnityPath);



        internal UnityEngine.Object? Load(OfflineArtEntry entry) =>
            bundles[entry.Bundle].LoadAsset<UnityEngine.Object>(entry.UnityPath);

        public void Dispose()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            var request = pending;
            pending = null;
            if (request != null)
            {
                // Finishing an outstanding native request on reload releases bundles that outlive managed code.
                var bundle = request.assetBundle;
                if (bundle != null) bundle.Unload(true);
            }
            for (var index = acquired.Count - 1; index >= 0; index--)
                if (acquired[index] != null) acquired[index].Unload(true);
            acquired.Clear();
            bundles.Clear();
        }
    }
}
