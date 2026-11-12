#nullable enable
using Arknights.MapEditor.Storage;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed class InvalidDocumentWindow : EditorWindow
    {
        [SerializeField] private string original = "", diagnostic = "";
        public string Original => original;
        public static InvalidDocumentWindow Open(string text, string error)
        {
            var window = CreateInstance<InvalidDocumentWindow>(); window.original = text; window.diagnostic = error;
            window.titleContent = new GUIContent("LevelData 导入诊断"); window.Show(); return window;
        }
        public void SaveOriginal(string path) => DocumentFileStore.Save(path, original, null);
        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.Add(new HelpBox(diagnostic, HelpBoxMessageType.Error));
            rootVisualElement.Add(new Button(() =>
            {
                var path = EditorUtility.SaveFilePanel("另存原文（只创建新文件）", "", "invalid-level.json", "json");
                if (path.Length == 0) return;
                try { SaveOriginal(path); }
                catch (System.Exception error) { diagnostic = error.Message; CreateGUI(); }
            }) { text = "另存原始内容" });
            var scroll = new ScrollView(); scroll.style.flexGrow = 1;
            scroll.Add(new TextField { value = original, multiline = true, isReadOnly = true }); rootVisualElement.Add(scroll);
        }
    }
}
