#nullable enable
using System;
using System.Collections.Generic;
using Arknights.MapEditor.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Arknights.MapEditor.Editor
{
    [Serializable]
    internal sealed class MapViewportState
    {
        internal enum Projection { FollowView, Orthographic, Perspective }
        [SerializeField] internal Vector2 Offset;
        [SerializeField] internal float VerticalOffset;
        [SerializeField] internal float Yaw;
        [SerializeField] internal float Pitch = 60f;
        [SerializeField] internal float Zoom = 1f;
        [SerializeField] internal bool Flat;
        [SerializeField] internal bool Coordinates;
        [SerializeField] internal Projection ProjectionMode;
        [SerializeField] internal bool Hand;
        [SerializeField] internal bool CameraLocked;
        [SerializeField] internal Vector2Int LockedSize;
        [SerializeField] internal float LockedAspect;
        [SerializeField] internal Vector3 NativePivot;
        [SerializeField] internal Quaternion NativeRotation;
        [SerializeField] internal float NativeSize;
        [SerializeField] internal bool NativeOrtho;
        internal bool Orthographic => ProjectionMode == Projection.FollowView ? Flat : ProjectionMode == Projection.Orthographic;
        internal Quaternion Rotation => Quaternion.Euler(Flat ? 90f : Pitch, Yaw, 0);
    }

    internal sealed class MapWorkspaceViewport : IDisposable
    {
        private readonly Scene scene;
        private readonly Camera camera;
        private readonly ChunkedMapView terrain;
        private readonly PrtsObjectView objects;
        private readonly PrtsLabelView labels;
        private readonly List<Vector3[]> routes = new List<Vector3[]>();
        private RenderTexture? texture;
        private LevelDocument? renderedDocument;
        internal int RenderedChunkCount => terrain.ChunkCount;
        internal int MissingObjectIconCount => objects.MissingIconCount;

        internal MapWorkspaceViewport()
        {
            scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var cameraRoot = new GameObject("LevelData viewport camera") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(cameraRoot, scene);
                camera = cameraRoot.AddComponent<Camera>();
                camera.scene = scene;
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.094f, .094f, .094f);
                camera.fieldOfView = 38f;
                camera.nearClipPlane = .05f;
                terrain = new ChunkedMapView(scene);
                objects = new PrtsObjectView(scene);
                labels = new PrtsLabelView(scene);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal RenderTexture Render(LevelDocument document, MapViewportState state, Vector2 size)
        {
            if (renderedDocument != document)
            {
                terrain.Update(document, null);
                objects.Update(document);
                MapPreviewStage.ReadRouteLines(document, routes);
                renderedDocument = document;
            }
            var scale = EditorGUIUtility.pixelsPerPoint;
            var width = Mathf.Clamp(Mathf.RoundToInt(size.x * scale), 1, 4096);
            var height = Mathf.Clamp(Mathf.RoundToInt(size.y * scale), 1, 4096);
            if (texture == null || texture.width != width || texture.height != height)
            {
                ReleaseTexture();
                texture = new RenderTexture(width, height, 24) { hideFlags = HideFlags.HideAndDontSave };
                camera.targetTexture = texture;
            }
            PositionCamera(document, state, size.x / size.y);
            terrain.SetBackdropVisible(camera.transform.position.y >= -1.1f);
            labels.Update(document, state.Coordinates, state.Orthographic && state.Rotation * Vector3.forward == Vector3.down);
            var active = RenderTexture.active;
            try { camera.Render(); }
            finally { RenderTexture.active = active; }
            return texture;
        }

        private void PositionCamera(LevelDocument document, MapViewportState state, float aspect)
        {
            var size = state.CameraLocked && state.LockedAspect > 0
                ? state.LockedSize : new Vector2Int(document.Width, document.Height);
            var framingAspect = state.CameraLocked && state.LockedAspect > 0 ? state.LockedAspect : aspect;
            var target = new Vector3(size.x * .5f + state.Offset.x, state.VerticalOffset, size.y * .5f + state.Offset.y);
            var rotation = state.Rotation;
            var halfExtent = Mathf.Max(size.x / framingAspect, size.y) * .58f + 1f;
            var extent = halfExtent * state.Zoom;
            var distance = extent / Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
            camera.aspect = aspect;
            camera.orthographic = state.Orthographic;
            camera.orthographicSize = extent;
            camera.farClipPlane = distance + Mathf.Max(document.Width, document.Height) * 3f + 10;
            camera.transform.SetPositionAndRotation(target - rotation * Vector3.forward * distance, rotation);
        }

        internal bool TryPick(LevelDocument document, Vector2 point, Rect rect, out Vector2Int cell)
        {
            cell = default;
            if (renderedDocument != document || !rect.Contains(point)) return false;
            var uv = PointToViewport(point, rect);
            return ChunkedMapView.TryPick(document, camera.ViewportPointToRay(uv), out cell);
        }

        internal Vector3 PanDelta(Vector2 point, Vector2 delta, Rect rect)
        {
            var extent = camera.orthographicSize;
            var distance = extent / Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
            var plane = new Plane(camera.transform.forward, camera.transform.position + camera.transform.forward * distance);
            var current = camera.ViewportPointToRay(PointToViewport(point, rect));
            var previous = camera.ViewportPointToRay(PointToViewport(point - delta, rect));
            if (!plane.Raycast(current, out var currentDistance) || !plane.Raycast(previous, out var previousDistance))
                return Vector3.zero;
            return previous.GetPoint(previousDistance) - current.GetPoint(currentDistance);
        }

        private static Vector3 PointToViewport(Vector2 point, Rect rect) =>
            new Vector3((point.x - rect.x) / rect.width, 1 - (point.y - rect.y) / rect.height, 0);

        internal void DrawRoutes(Rect rect)
        {
            using (new Handles.DrawingScope(new Color(.28f, .78f, .91f)))
            {
                foreach (var route in routes)
                {
                    for (var i = 1; i < route.Length; i++)
                    {
                        var a = camera.WorldToViewportPoint(route[i - 1]);
                        var b = camera.WorldToViewportPoint(route[i]);
                        if (a.z < camera.nearClipPlane || b.z < camera.nearClipPlane) continue;
                        Handles.DrawLine(ToGui(a, rect), ToGui(b, rect));
                    }
                }
            }
            using (new Handles.DrawingScope(Color.yellow))
            {
                foreach (var marker in objects.UnresolvedMarkers)
                {
                    var uv = camera.WorldToViewportPoint(marker);
                    if (uv.z < camera.nearClipPlane) continue;
                    var point = ToGui(uv, rect);
                    Handles.DrawWireDisc(point, Vector3.forward, 3f);
                }
            }
        }

        private static Vector3 ToGui(Vector3 uv, Rect rect) =>
            new Vector3(rect.x + uv.x * rect.width, rect.y + (1 - uv.y) * rect.height, 0);

        private void ReleaseTexture()
        {
            if (camera != null) camera.targetTexture = null;
            if (texture == null) return;
            texture.Release();
            UnityEngine.Object.DestroyImmediate(texture);
            texture = null;
        }

        public void Dispose()
        {
            ReleaseTexture();
            labels?.Dispose();
            objects?.Dispose();
            terrain?.Dispose();
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
