#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Arknights.MapEditor.Catalog;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Arknights.MapEditor.Editor
{
    internal sealed partial class OfflineArtPreviewWindow : EditorWindow
    {
        private const string MenuPath = "Tools/Arknights/离线美术分层展示";
        [SerializeField] private string root = "";
        [SerializeField] private string key = "";
        [SerializeField] private bool overhead;
        private CancellationTokenSource? loading;
        private OfflineArtCatalog? queued;
        private OfflineArtPreviewScene? preview;
        private Texture2D? sceneFrame;
        private readonly OfflineArtSceneImageView sceneImageView = new OfflineArtSceneImageView();
        private OfflineArtBundles? bundles;
        private IReadOnlyList<OfflineArtSample> samples = Array.Empty<OfflineArtSample>();
        private readonly List<string> failures = new List<string>();
        private Vector2 scroll;
        private bool windowOpen;
        private string status = "";
        private Texture? frame;
        private Vector2 frameSize;
        private bool frameOverhead;
        private int selectedSample = -1;

        [MenuItem(MenuPath)]
        public static void OpenInstalledGallery()
        {
            var folder = MapEditorWindow.ArtPackFolder;
            if (folder.Length == 0) folder = EditorUtility.OpenFolderPanel("选择离线美术包", "", "");
            if (folder.Length == 0) return;

            try
            {
                var catalog = OfflineArtCatalog.Open(folder);
                EditorPrefs.SetString(MapEditorWindow.OfflineArtPackFolder, catalog.Root);
                var workspace = Resources.FindObjectsOfTypeAll<MapWorkspaceWindow>()
                    .FirstOrDefault(window => window.UsesNativeScenes);
                ShowGallery(catalog, workspace);
            }
            catch (Exception error) when (IsPreviewFailure(error))
            {
                EditorUtility.DisplayDialog("离线美术包", error.Message + "\n日志 " +
                    PluginLog.Failure("art-pack.open-gallery", error), "确定");
            }
        }

        internal static void ShowGallery(OfflineArtCatalog catalog, MapWorkspaceWindow? workspace)
        {
            if (workspace != null)
                workspace.ShowOfflineArtGalleryMap();
            Open(catalog, "");
        }

        internal static void ShowEntry(OfflineArtCatalog catalog, OfflineArtEntry entry) => Open(catalog, entry.Key);

        private static void Open(OfflineArtCatalog catalog, string entryKey)
        {
            var opened = Resources.FindObjectsOfTypeAll<OfflineArtPreviewWindow>().FirstOrDefault();
            var window = opened ?? GetWindow<OfflineArtPreviewWindow>();
            if (opened == null)
            {
                var display = Screen.currentResolution;
                var scale = EditorGUIUtility.pixelsPerPoint;
                var width = Mathf.Clamp(display.width / scale - 96f, 520f, 1120f);
                var height = Mathf.Clamp(display.height / scale - 96f, 320f, 720f);
                window.position = new Rect((display.width / scale - width) / 2f,
                    (display.height / scale - height) / 2f, width, height);
            }
            window.minSize = new Vector2(520, 320);
            window.titleContent = new GUIContent("离线美术预览");
            window.root = catalog.Root;
            window.key = entryKey;
            window.Queue(catalog);
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            windowOpen = true;
            EditorApplication.update += TickDemo;
            if (root.Length > 0) EditorApplication.delayCall += Restore;
        }

        private void Restore()
        {
            if (!windowOpen || loading != null || preview != null || sceneFrame != null || lab != null || flatTexture != null) return;
            try { Queue(OfflineArtCatalog.Open(root)); }
            catch (Exception error) when (IsPreviewFailure(error)) { ShowFailure(error); }
        }

        private void OnDisable()
        {
            windowOpen = false;
            EditorApplication.update -= TickDemo;
            EditorApplication.delayCall -= Restore;
            EditorApplication.delayCall -= RestoreCurrent;
            loading?.Cancel();
            queued = null;
            Release();
        }

        private void Queue(OfflineArtCatalog catalog)
        {
            queued = catalog;
            loading?.Cancel();
            selectedSample = -1;
            status = "正在读取 LZ4 AssetBundle…";
            Repaint();
            if (loading == null) ProcessQueue();
        }

        private async void ProcessQueue()
        {
            while (windowOpen && queued != null)
            {
                var catalog = queued;
                queued = null;
                var requested = key;
                Release();
                failures.Clear();
                samples = Array.Empty<OfflineArtSample>();
                using var token = new CancellationTokenSource();
                loading = token;
                var nextBundles = new OfflineArtBundles();
                OfflineArtPreviewScene? nextPreview = null;
                try
                {
                    if (requested.Length == 0)
                    {
                        samples = OfflineArtGallery.Select(catalog);
                        if (samples.Count == 0) throw new InvalidDataException("目录没有分层展示样本。");
                        await nextBundles.LoadAsync(catalog, samples.SelectMany(sample => sample.ModelMaterial == null
                            ? new[] { sample.Entry } : new[] { sample.Entry, sample.ModelMaterial! }), token.Token);
                        token.Token.ThrowIfCancellationRequested();
                        nextPreview = new OfflineArtPreviewScene();
                        foreach (var sample in samples)
                        {
                            var position = OfflineArtPreviewScene.Position((int)sample.Layer, sample.Column);
                            nextPreview.AddTile(position, sample.Entry.Key);
                            var asset = nextBundles.Load(sample.Entry);
                            var material = sample.ModelMaterial == null ? null : nextBundles.Load(sample.ModelMaterial) as Material;
                            if (asset == null || !nextPreview.AddAsset(asset, material, position, sample.Entry.Key,
                                sample.Layer == OfflineArtGalleryLayer.Effect))
                                failures.Add(sample.Entry.Key);
                        }
                        status = $"{OfflineArtCatalog.Baseline} · {nextPreview.RenderedAssets}/{samples.Count} 项已装入预览场景";
                    }
                    else
                    {
                        if (!catalog.TryGet(requested, out var entry)) throw new InvalidDataException("目录中缺少 " + requested);
                        if (entry.RequiresMetadata)
                        {
                            OpenRecord(catalog, entry);
                        }
                        else if (entry.Category == OfflineArtCategory.Scene)
                        {
                            status = "后台 Unity 正在渲染完整场景…\n" + entry.UnityPath;
                            Repaint();
                            var result = await OfflineArtScenePreview.RenderAsync(catalog, entry, token.Token);
                            if (token.IsCancellationRequested || !windowOpen)
                            {
                                DestroyImmediate(result.Image);
                                token.Token.ThrowIfCancellationRequested();
                                continue;
                            }
                            sceneFrame = result.Image;
                            status = $"完整场景静帧 · {result.Description}\n{entry.UnityPath}";
                        }
                        else
                        {
                            await nextBundles.LoadAsync(catalog, new[] { entry }, token.Token);
                            token.Token.ThrowIfCancellationRequested();
                            nextPreview = OpenAsset(nextBundles, entry);
                        }
                    }

                    if (lab != null || flatTexture != null) bundles = nextBundles;

                    if (nextPreview != null)
                    {
                        preview = nextPreview;
                        bundles = nextBundles;
                        nextPreview = null;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested || !windowOpen) { }
                catch (Exception error) when (IsPreviewFailure(error)) { ShowFailure(error); }
                finally
                {
                    nextPreview?.Dispose();
                    if (bundles != nextBundles) nextBundles.Dispose();
                    loading = null;
                    if (windowOpen) Repaint();
                }
            }
        }

        private OfflineArtPreviewScene? OpenAsset(OfflineArtBundles lease, OfflineArtEntry entry)
        {
            var asset = lease.Load(entry);
            if (asset == null)
                throw new InvalidDataException(lease.Contains(entry) ?
                    "资源地址已在包中登记，Unity 编辑器无法实例化此对象 " + entry.UnityPath :
                    "资源地址未在此 Bundle 登记 " + entry.UnityPath);
            status = $"{entry.SourceType} · {asset.GetType().Name}\n{entry.UnityPath}";
            if (asset is Cubemap cube)
            {
                OpenReflection(entry, cube);
                return null;
            }
            if (asset is Texture2D texture)
            {
                flatTexture = texture;
                status = $"贴图 · {texture.width}×{texture.height} · {texture.format}\n{entry.UnityPath}";
                return null;
            }
            var scene = new OfflineArtPreviewScene();
            try
            {
                if (scene.AddAsset(asset, null, Vector3.zero, entry.Key, entry.Category == OfflineArtCategory.Effect))
                {
                    scene.AddTile(Vector3.zero, entry.Key);
                    return scene;
                }
                status += "\n可在模型或动画控制器中引用。";
            }
            catch
            {
                scene.Dispose();
                throw;
            }
            scene.Dispose();
            return null;
        }

        private void OnGUI()
        {
            var isGallery = key.Length == 0;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(isGallery ? OfflineArtGallery.Title : "美术资源预览", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (preview != null)
                {
                    if (GUILayout.Button("立体", EditorStyles.toolbarButton)) overhead = false;
                    if (GUILayout.Button("俯视", EditorStyles.toolbarButton)) overhead = true;
                }
                if (root.Length > 0 && GUILayout.Button("重新加载", EditorStyles.toolbarButton))
                    EditorApplication.delayCall += RestoreCurrent;
            }
            EditorGUILayout.HelpBox(status, failures.Count == 0 ? MessageType.None : MessageType.Warning);
            if (sceneFrame != null)
            {
                sceneImageView.Draw(sceneFrame);
                return;
            }
            if (lab != null) { DrawLab(); return; }
            if (flatTexture != null) { DrawFlatTexture(); return; }
            if (preview == null) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                var area = GUILayoutUtility.GetRect(200, 140, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                if (Event.current.type == EventType.Repaint)
                    DrawFrame(area, isGallery);

                if (!isGallery) return;
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(Mathf.Clamp(position.width * 0.28f, 156, 280))))
                {
                    scroll = EditorGUILayout.BeginScrollView(scroll);
                    foreach (var group in samples.GroupBy(sample => sample.Layer))
                    {
                        EditorGUILayout.LabelField(LayerTitle(group.Key), EditorStyles.boldLabel);
                        foreach (var sample in group)
                        {
                            var index = (int)sample.Layer * OfflineArtGallery.Columns + sample.Column;
                            if (GUILayout.Toggle(selectedSample == index,
                                new GUIContent($"{sample.Column + 1}  {Path.GetFileName(sample.Entry.Key)}", sample.Entry.Key),
                                EditorStyles.miniButton))
                                selectedSample = index;
                        }
                        GUILayout.Space(5);
                    }
                    if (failures.Count != 0)
                        EditorGUILayout.HelpBox("无法预览\n" + string.Join("\n", failures), MessageType.Warning);
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private void DrawFrame(Rect area, bool gallery)
        {
            if (preview == null || area.width < 2 || area.height < 2) return;
            try
            {
                if (frame == null || frameSize != area.size || frameOverhead != overhead)
                {
                    frame = preview.Render(area, gallery, overhead);
                    frameSize = area.size;
                    frameOverhead = overhead;
                }
                if (frame != null) GUI.DrawTexture(area, frame, ScaleMode.StretchToFill, false);
                if (gallery && selectedSample >= 0)
                {
                    var cell = OfflineArtPreviewScene.Position(selectedSample / OfflineArtGallery.Columns,
                        selectedSample % OfflineArtGallery.Columns);
                    var point = preview.Camera.WorldToViewportPoint(cell + Vector3.up * 0.18f);
                    var label = new Rect(area.x + point.x * area.width - 11, area.y + (1f - point.y) * area.height - 9, 22, 18);
                    EditorGUI.DrawRect(label, new Color(0.12f, 0.37f, 0.52f, 0.93f));
                    GUI.Label(label, (selectedSample % OfflineArtGallery.Columns + 1).ToString(), EditorStyles.centeredGreyMiniLabel);
                }
            }
            catch (Exception error) when (IsPreviewFailure(error))
            {
                FailDrawing(error);
            }
        }

        private void FailDrawing(Exception error)
        {
            var failedPreview = preview;
            var failedLab = lab;
            playing = false;
            EditorApplication.delayCall += () =>
            {
                if (!windowOpen || preview != failedPreview || lab != failedLab) return;
                Release();
                ShowFailure(error);
                Repaint();
            };
        }

        private void OnLostFocus() => sceneImageView.EndDrag();

        private void RestoreCurrent()
        {
            try { Queue(OfflineArtCatalog.Open(root)); }
            catch (Exception error) when (IsPreviewFailure(error)) { ShowFailure(error); }
        }

        private void Release()
        {
            frame = null;
            ReleaseInspection();
            sceneImageView.Reset();
            if (sceneFrame != null) DestroyImmediate(sceneFrame);
            sceneFrame = null;
            preview?.Dispose();
            preview = null;
            bundles?.Dispose();
            bundles = null;
        }

        private void ShowFailure(Exception error) =>
            status = error.Message + "\n日志 " + PluginLog.Failure("art-pack.preview", error);

        private static bool IsPreviewFailure(Exception error) => error is IOException || error is InvalidDataException || error is InvalidOperationException ||
            error is UnauthorizedAccessException || error is ArgumentException || error is Newtonsoft.Json.JsonException ||
            error is UnityException || error is System.ComponentModel.Win32Exception;

        private static string LayerTitle(OfflineArtGalleryLayer layer) => layer switch
        {
            OfflineArtGalleryLayer.Texture => "贴图",
            OfflineArtGalleryLayer.Material => "材质",
            OfflineArtGalleryLayer.Model => "场景模型",
            OfflineArtGalleryLayer.Effect => "特效代表帧",
            _ => throw new ArgumentOutOfRangeException(nameof(layer))
        };
    }
}
