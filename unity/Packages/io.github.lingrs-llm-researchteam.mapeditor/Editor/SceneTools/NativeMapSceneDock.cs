#nullable enable
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Editor
{
    // Unity 2021.3's CreateWindow matches exact types and also searches floating hosts.
    // Keep its internal docking API here so new maps can select the main window explicitly.
    internal static class NativeMapSceneDock
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo? Parent = typeof(EditorWindow).GetField("m_Parent", Instance);
        private static readonly Type? DockArea = typeof(EditorWindow).Assembly.GetType("UnityEditor.DockArea");
        private static readonly PropertyInfo? Container = DockArea?.GetProperty("window", Instance);
        private static readonly PropertyInfo? ShowMode = Container?.PropertyType.GetProperty("showMode", Instance);
        private static readonly MethodInfo? AddTab = DockArea?.GetMethod("AddTab", Instance, null,
            new[] { typeof(EditorWindow), typeof(bool) }, null);

        internal static bool IsInMainWindow(EditorWindow window) => MainDock(window) != null;

        internal static NativeMapSceneView Create()
        {
            if (Parent == null || Container == null || ShowMode == null || AddTab == null)
                throw new NotSupportedException("当前 Unity 版本没有可用的场景停靠接口。");

            var target = Resources.FindObjectsOfTypeAll<EditorWindow>()
                .Where(window => Priority(window) < int.MaxValue && MainDock(window) != null)
                .OrderBy(Priority)
                .ThenByDescending(window => window.position.width * window.position.height)
                .FirstOrDefault();
            if (target == null)
                throw new InvalidOperationException("请先在 Unity 主窗口打开 Game 或 Scene 标签，再点击显示场景。");

            var view = ScriptableObject.CreateInstance<NativeMapSceneView>();
            try
            {
                view.titleContent = new GUIContent("地图场景");
                AddTab.Invoke(MainDock(target), new object[] { view, true });
                return view;
            }
            catch
            {
                if (Parent.GetValue(view) != null) view.CloseFromWorkspace();
                else UnityEngine.Object.DestroyImmediate(view);
                throw;
            }
        }

        private static object? MainDock(EditorWindow window)
        {
            var parent = Parent?.GetValue(window);
            if (parent == null || DockArea?.IsInstanceOfType(parent) != true) return null;
            var container = Container?.GetValue(parent);
            return container != null && ShowMode?.GetValue(container)?.ToString() == "MainWindow" ? parent : null;
        }

        private static int Priority(EditorWindow window)
        {
            if (window.GetType().FullName == "UnityEditor.GameView") return 0;
            if (window.GetType() == typeof(SceneView)) return 1;
            return window is NativeMapSceneView ? 2 : int.MaxValue;
        }
    }
}
