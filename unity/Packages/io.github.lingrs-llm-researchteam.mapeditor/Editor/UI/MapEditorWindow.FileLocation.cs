#nullable enable
using System;
using System.IO;
using UnityEditor;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        internal void OpenMapFileLocation()
        {
            var source = session?.SourcePath;
            if (string.IsNullOrEmpty(source))
            {
                EditorUtility.DisplayDialog("地图尚未保存", "当前地图还没有文件位置。请先使用“保存”选择地图文件路径。", "确定");
                return;
            }
            var fullPath = Path.GetFullPath(source);
            if (File.Exists(fullPath))
            {
                EditorUtility.RevealInFinder(fullPath);
                return;
            }
            var directory = Path.GetDirectoryName(fullPath);
            if (directory != null && Directory.Exists(directory))
            {
                EditorUtility.RevealInFinder(directory);
                EditorUtility.DisplayDialog("地图文件已移动或删除", "已打开原文件夹：\n" + directory + "\n当前编辑内容仍保留，可另存为新文件。", "确定");
                return;
            }
            EditorUtility.DisplayDialog("找不到地图文件位置", fullPath + "\n当前编辑内容仍保留，请检查路径或另存。", "确定");
        }
    }
}
