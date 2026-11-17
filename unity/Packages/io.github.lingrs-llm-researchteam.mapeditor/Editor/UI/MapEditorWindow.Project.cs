#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Arknights.MapEditor.Storage;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        [SerializeField] private string projectPath = "";
        [SerializeField] private string projectHash = "";
        [SerializeField] private string projectLevelPath = "level.json";
        [SerializeField] private string artJson = "[]";
        [SerializeField] private string savedProjectLevel = "level.json";
        [SerializeField] private string savedProjectArt = "[]";
        private TextField? projectLevelField;
        private TextField? projectArtField;
        private bool ProjectDirty => projectLevelPath != savedProjectLevel || artJson != savedProjectArt;
        private bool CanReplaceProject() => !ProjectDirty || Dialogs.Discard("未保存编辑工程", "丢弃当前工程路径和美术映射修改？");
        private void ResetProjectState()
        {
            projectPath = projectHash = "";
            finishedLevelOrigin = "";
            projectLevelPath = savedProjectLevel = "level.json";
            artJson = savedProjectArt = "[]";
            SyncProjectFields();
        }
        private void SyncProjectFields()
        {
            projectLevelField?.SetValueWithoutNotify(projectLevelPath);
            projectArtField?.SetValueWithoutNotify(artJson);
        }
        private bool SaveProjectChanges()
        {
            RequireWorkspaceEditable();
            var target = projectPath;
            if (target.Length == 0) target = Dialogs.Save("保存独立编辑工程", "", "project.arkmap", "arkmap");
            if (target.Length == 0) return false;
            var mappings = new List<ArtMapping>();
            foreach (var token in ParseArt())
            {
                if (!(token is JObject item) || item["key"]?.Type != JTokenType.String || item["resourcePath"]?.Type != JTokenType.String ||
                    (item["unityGuid"] != null && item["unityGuid"]!.Type != JTokenType.Null && item["unityGuid"]!.Type != JTokenType.String))
                    throw new FormatException("映射需要 key、resourcePath 字符串和可选字符串GUID。");
                mappings.Add(new ArtMapping((string)item["key"]!, (string)item["resourcePath"]!, (string?)item["unityGuid"]));
            }
            var project = new ArkMapProject(projectLevelPath, mappings);
            var levelFile = ArkMapProjectStore.ResolveLevelDataPath(target, project);
            ValidateProjectLevelBinding(levelFile, session!.SourcePath);
            if (!File.Exists(levelFile)) throw new FileNotFoundException("请先将 LevelData 保存至工程引用的位置。", levelFile);
            projectHash = ArkMapProjectStore.Save(target, project, projectHash.Length == 0 ? null : projectHash); projectPath = target;
            savedProjectLevel = projectLevelPath; savedProjectArt = artJson;
            Refresh();
            if (status != null) status.text = "编辑工程已保存；美术映射尚未接游戏运行适配。";
            return true;
        }

        private static void ValidateProjectLevelBinding(string levelFile, string sessionPath)
        {
            var comparison = Application.platform == RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.IsNullOrWhiteSpace(sessionPath) || !string.Equals(Path.GetFullPath(levelFile), Path.GetFullPath(sessionPath), comparison))
                throw new EditorOperationRejectedException("工程关卡引用必须指向当前已保存的 LevelData；请先另存关卡到该位置或调整相对路径。");
        }
        private string CaptureProjectRecovery()
        {
            var root = new JObject { ["version"] = 1, ["projectPath"] = projectPath, ["projectHash"] = projectHash,
                ["levelPath"] = projectLevelPath, ["artJson"] = artJson, ["savedLevel"] = savedProjectLevel, ["savedArt"] = savedProjectArt };
            if (finishedLevelOrigin.Length > 0) root["finishedLevelOrigin"] = finishedLevelOrigin;
            return root.ToString();
        }
        private sealed class ProjectRecoveryState
        {
            internal string Path = "", Hash = "", Level = "", Art = "", SavedLevel = "", SavedArt = "", FinishedLevelOrigin = "";
        }
        private static ProjectRecoveryState ReadProjectRecovery(string path)
        {
            var source = DocumentFileStore.Read(path);
            return ParseProjectRecovery(source.Text) ?? throw new InvalidDataException("Empty art recovery file.");
        }
        private static ProjectRecoveryState? ParseProjectRecovery(string json)
        {
            if (json == "") return null;
            using (var text = new StringReader(json))
            using (var reader = new Newtonsoft.Json.JsonTextReader(text) { MaxDepth = 4, DateParseHandling = Newtonsoft.Json.DateParseHandling.None })
            {
                var root = JObject.Load(reader, new Newtonsoft.Json.Linq.JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                var origin = root["finishedLevelOrigin"];
                if (reader.Read() || root.Count != (origin == null ? 7 : 8) ||
                    root["version"]?.Type != JTokenType.Integer || root["version"]!.ToString() != "1")
                    throw new InvalidDataException("Invalid art draft envelope.");
                string Field(string key) => root[key]?.Type == JTokenType.String ? (string)root[key]! : throw new InvalidDataException("Invalid art draft field: " + key);
                var state = new ProjectRecoveryState { Path = Field("projectPath"), Hash = Field("projectHash"), Level = Field("levelPath"), Art = Field("artJson"), SavedLevel = Field("savedLevel"), SavedArt = Field("savedArt") };
                if (origin != null) state.FinishedLevelOrigin = Field("finishedLevelOrigin");
                if ((state.Path.Length == 0) != (state.Hash.Length == 0) || (state.Hash.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(state.Hash, "\\A[0-9a-fA-F]{64}\\z"))) throw new InvalidDataException("Invalid project source identity.");
                // Drafts may intentionally contain incomplete JSON or paths. They are not applied or written here.
                return state;
            }
        }
        private void ApplyProjectRecovery(ProjectRecoveryState state)
        {
            projectPath = state.Path; projectHash = state.Hash; projectLevelPath = state.Level; artJson = state.Art;
            savedProjectLevel = state.SavedLevel; savedProjectArt = state.SavedArt;
            finishedLevelOrigin = state.FinishedLevelOrigin; SyncProjectFields();
        }
        private void RestoreProjectRecovery(string path) => ApplyProjectRecovery(ReadProjectRecovery(path));
        private void BuildProjectPanel(VisualElement parent)
        {
            var fold = new Foldout { text = "编辑工程与美术映射（不混入 LevelData）", value = false };
            var level = new TextField("关卡相对路径") { value = projectLevelPath };
            projectLevelField = level;
            level.RegisterValueChangedCallback(e => { projectLevelPath = e.newValue; hasUnsavedChanges = true; workspaceOwner?.DocumentChanged(this); }); fold.Add(level);
            var art = new TextField("美术映射 JSON") { multiline = true, value = artJson };
            projectArtField = art;
            art.RegisterValueChangedCallback(e => { artJson = e.newValue; hasUnsavedChanges = true; workspaceOwner?.DocumentChanged(this); }); fold.Add(art);
            var asset = new ObjectField("Unity资源引用") { objectType = typeof(UnityEngine.Object), allowSceneObjects = false };
            var key = new TextField("映射 key"); var relative = new TextField("资源相对路径");
            fold.Add(asset); fold.Add(key); fold.Add(relative);
            AddButton(fold, "加入映射", () =>
            {
                var path = asset.value == null ? "" : AssetDatabase.GetAssetPath(asset.value);
                var guid = path.Length == 0 ? null : AssetDatabase.AssetPathToGUID(path);
                var mapping = new ArtMapping(key.value, relative.value, guid);
                var array = ParseArt();
                array.Add(new JObject { ["key"] = mapping.Key, ["resourcePath"] = mapping.ResourcePath, ["unityGuid"] = mapping.UnityGuid });
                art.value = array.ToString();
            });
            AddButton(fold, "保存 .arkmap", () =>
            {
                SaveProjectChanges();
            });
            AddButton(fold, "打开 .arkmap", OpenProjectDocument);
            fold.Add(new HelpBox("资源路径相对于 .arkmap 所在目录；Unity GUID 仅用于编辑器定位，不是游戏路径。当前保存配置，不构建或注入资源。", HelpBoxMessageType.Info));
            parent.Add(fold);
        }
        private void OpenProjectDocument()
        {
                RequireWorkspaceEditable();
                var target = Dialogs.Open("打开编辑工程", "", "arkmap");
                if (target.Length == 0) return;
                var project = ArkMapProjectStore.Read(target, out var hash);
                var levelFile = ArkMapProjectStore.ResolveLevelDataPath(target, project);
                if (FocusOtherWriter(levelFile) || !CanReplace()) return;
                var data = DocumentFileStore.Read(levelFile);
                Core.LevelDocument.Parse(data.Text);
                var array = new JArray(); var missing = new List<string>();
                foreach (var mapping in project.ArtMappings)
                {
                    array.Add(new JObject { ["key"] = mapping.Key, ["resourcePath"] = mapping.ResourcePath, ["unityGuid"] = mapping.UnityGuid });
                    if (!File.Exists(ArkMapProjectStore.ResolveResourcePath(target, mapping))) missing.Add(mapping.Key);
                }
                // All IO and validation finish before replacing the live session.
                BeginRecoveryGeneration(); CloseOwnedPreview();
                session!.Load(data.Text, levelFile, data.Sha256); draft = session.Json;
                ClearInspectorDrafts();
                projectPath = target; projectHash = hash;
                finishedLevelOrigin = "";
                projectLevelPath = savedProjectLevel = project.LevelDataPath;
                artJson = savedProjectArt = array.ToString(); CreateGUI();
                if (missing.Count > 0 && status != null) status.text = "美术资源缺失（保留映射）：" + string.Join(", ", missing);
        }
        private JArray ParseArt() => Core.LevelDocument.Create(1, 1).WithSection("artMappings", artJson).GetSection("artMappings") as JArray
            ?? throw new FormatException("美术映射必须为数组。");
    }
}
