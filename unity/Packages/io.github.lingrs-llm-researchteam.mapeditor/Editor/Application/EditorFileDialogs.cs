#nullable enable
using UnityEditor;

namespace Arknights.MapEditor.Editor
{
    public interface IEditorFileDialogs
    {
        string Open(string title, string directory, string extension);
        string Save(string title, string directory, string name, string extension);
        bool Discard(string title, string message);
    }
    internal sealed class EditorFileDialogs : IEditorFileDialogs
    {
        public string Open(string title, string directory, string extension) => EditorUtility.OpenFilePanel(title, directory, extension);
        public string Save(string title, string directory, string name, string extension) => EditorUtility.SaveFilePanel(title, directory, name, extension);
        public bool Discard(string title, string message) => EditorUtility.DisplayDialog(title, message, "确定", "取消");
    }
}
