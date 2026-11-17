#nullable enable
using System;
using System.IO;
using System.Text;
using Arknights.MapEditor.Catalog;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Editor
{
    internal sealed partial class OfflineArtPreviewWindow
    {
        private OfflineArtLabPreview? lab;
        private Texture2D? flatTexture;
        private string fieldsJson = "";
        private string inputSummary = "";
        private bool showFields;
        private bool applied = true;
        private bool playing;
        private float demoTime;
        private double playStarted;
        private Vector2 fieldScroll;

        private void OpenRecord(OfflineArtCatalog catalog, OfflineArtEntry entry)
        {
            var metadata = OfflineArtMetadata.Open(catalog);
            if (!metadata.TryGet(entry, out var record))
                throw new InvalidDataException("预览参数索引未收录 " + entry.Key);

            var motion = entry.Category == OfflineArtCategory.Camera ? OfflineArtCameraDemo.From(record) : null;
            lab = new OfflineArtLabPreview(OfflineArtReferenceMap.Read(entry), record, motion, null);
            status = $"{entry.SourceType} · {record.Type}\n{entry.UnityPath}";
            var summary = new StringBuilder();
            summary.Append("地图 ").Append(lab.Map.Title);
            if (motion != null)
            {
                summary.Append(lab.Map.DirectCameraReference ? " · 关卡直接引用此控制器" : " · 镜头示例");
                summary.Append($"\n演示时长 {motion.Duration:g} 秒");
                if (motion.Dither)
                    summary.Append(" · 抖动透明过渡示意");
                else
                {
                    summary.Append(motion.ReferenceOffset ? " · 示例位移 " : " · 源偏移 ")
                        .Append(motion.Offset.ToString("F2"));
                    if (motion.FocusCompensation) summary.Append(" · 补偿看向点");
                }
                summary.Append("\n基准视角取自 5-10 原场景。回放使用预览时间轴。");
            }
            else
            {
                summary.Append(lab.HasVisualParameters
                    ? " · 同一地图，切换查看配置影响"
                    : " · 示例地图与序列化配置");
                showFields = !lab.HasVisualParameters;
            }
            inputSummary = summary.ToString();
            fieldsJson = new Newtonsoft.Json.Linq.JObject
            {
                ["objectId"] = record.ObjectId,
                ["fields"] = record.Fields.DeepClone(),
                ["settings"] = record.Settings?.DeepClone(),
                ["constants"] = record.Constants?.DeepClone()
            }.ToString(Formatting.Indented);
        }

        private void OpenReflection(OfflineArtEntry entry, Cubemap cube)
        {
            lab = new OfflineArtLabPreview(OfflineArtReferenceMap.Read(entry), null, null, cube);
            status = $"反射与天空贴图 · {cube.width}×{cube.height} × 6 面\n{entry.UnityPath}";
            inputSummary = $"地图 {lab.Map.Title} · 同一地图中的反射球对照";
        }

        private void DrawLab()
        {
            if (lab == null) return;
            EditorGUILayout.LabelField(inputSummary, EditorStyles.wordWrappedLabel);
            if (lab.HasVisualParameters || lab.Motion != null)
            {
                var mode = GUILayout.Toolbar(applied ? 1 : 0, new[] { "原始示意", "应用配置" });
                if (applied != (mode == 1))
                {
                    applied = mode == 1;
                    frame = null;
                }
            }

            if (lab.Motion is { } motion)
            {
                using var bar = new EditorGUILayout.HorizontalScope();
                if (GUILayout.Button(playing ? "暂停" : "播放", GUILayout.Width(56)))
                {
                    if (!playing) StartPlayback(motion);
                    else playing = false;
                }
                EditorGUI.BeginChangeCheck();
                var time = EditorGUILayout.Slider(demoTime, 0, motion.Duration);
                if (EditorGUI.EndChangeCheck())
                {
                    playing = false;
                    SetDemoTime(time);
                }
                GUILayout.Label($"{demoTime:0.00} / {motion.Duration:0.00} s", GUILayout.Width(96));
            }

            var area = GUILayoutUtility.GetRect(200, 120, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Repaint && area.width > 2f && area.height > 2f)
            {
                try
                {
                    if (frame == null || frameSize != area.size)
                    {
                        frame = lab.Render(area, demoTime, applied);
                        frameSize = area.size;
                    }
                    GUI.DrawTexture(area, frame, ScaleMode.StretchToFill, false);
                }
                catch (Exception error) when (IsPreviewFailure(error)) { FailDrawing(error); }
            }

            if (fieldsJson.Length == 0) return;
            showFields = EditorGUILayout.Foldout(showFields, "序列化配置", true);
            if (!showFields) return;
            fieldScroll = EditorGUILayout.BeginScrollView(fieldScroll, GUILayout.Height(Mathf.Min(220f, position.height * .32f)));
            try { EditorGUILayout.SelectableLabel(fieldsJson, EditorStyles.textArea, GUILayout.Height(
                EditorStyles.textArea.CalcHeight(new GUIContent(fieldsJson), Mathf.Max(120f, position.width - 42f)))); }
            finally { EditorGUILayout.EndScrollView(); }
        }

        private void DrawFlatTexture()
        {
            if (flatTexture == null) return;
            EditorGUILayout.LabelField("原始贴图 · 完整纹理范围 0–1。模型通过 UV 选取其中的区域。", EditorStyles.wordWrappedLabel);
            var area = GUILayoutUtility.GetRect(220, 120, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(area, new Color(.105f, .12f, .14f));
                GUI.DrawTexture(area, flatTexture, ScaleMode.ScaleToFit, true);
            }
        }

        private void StartPlayback(OfflineArtCameraDemo motion)
        {
            applied = true;
            if (demoTime >= motion.Duration) SetDemoTime(0f);
            playStarted = EditorApplication.timeSinceStartup - demoTime;
            playing = true;
            frame = null;
        }

        private void SetDemoTime(float time)
        {
            demoTime = time;
            frame = null;
            Repaint();
        }

        private void TickDemo()
        {
            if (!playing || lab?.Motion is not { } motion) return;
            var elapsed = (float)(EditorApplication.timeSinceStartup - playStarted);
            var next = Mathf.Min(elapsed, motion.Duration);
            if (next < motion.Duration && next - demoTime < 1f / 24f) return;
            SetDemoTime(next);
            playing = next < motion.Duration;
        }

        private void ReleaseInspection()
        {
            playing = false;
            demoTime = 0f;
            applied = true;
            lab?.Dispose();
            lab = null;
            flatTexture = null;
            inputSummary = fieldsJson = "";
            showFields = false;
            fieldScroll = Vector2.zero;
        }
    }
}
