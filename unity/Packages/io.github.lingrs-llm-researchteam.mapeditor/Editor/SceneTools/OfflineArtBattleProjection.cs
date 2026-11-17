#nullable enable
using UnityEngine;

namespace Arknights.MapEditor.Editor
{
    internal static class OfflineArtBattleProjection
    {
        // The 2.7.81 level_main_05-10.unity battle Camera, under an identity ancestor chain.
        internal const float VerticalFieldOfView = 40f;
        internal const float NearPlane = 0.3f;
        private static readonly Quaternion GameToPreview = Quaternion.AngleAxis(90f, Vector3.right);
        private static readonly Vector3 SourcePosition = new Vector3(0f, -5.6f, -8.92f);

        internal static Quaternion Rotation => GameToPreview * Quaternion.Euler(-30f, 0f, 0f);
        internal static Vector3 ToPreviewOffset(Vector3 source) => GameToPreview * source;

        internal static void Apply(Camera camera, int width, int height, float aspect, Vector3 sourceOffset, bool compensate)
        {
            var rotation = Rotation;
            var focus = new Vector3(0f, 0.18f, 0f);
            var forward = rotation * Vector3.forward;
            var distance = FitDistance(width, height, aspect, focus, rotation);
            var offset = ToPreviewOffset(sourceOffset);
            var position = focus - forward * distance + offset;
            camera.orthographic = false;
            camera.fieldOfView = VerticalFieldOfView;
            camera.nearClipPlane = NearPlane;
            camera.aspect = aspect;
            camera.transform.SetPositionAndRotation(position, rotation);

            var projection = Matrix4x4.Perspective(VerticalFieldOfView, aspect, NearPlane, camera.farClipPlane);
            if (compensate)
            {
                var depth = Mathf.Max(NearPlane, Vector3.Dot(focus - position, forward));
                projection.m02 = -projection.m00 * Vector3.Dot(offset, rotation * Vector3.right) / depth;
                projection.m12 = -projection.m11 * Vector3.Dot(offset, rotation * Vector3.up) / depth;
            }
            camera.projectionMatrix = projection;
        }

        private static float FitDistance(int width, int height, float aspect, Vector3 focus, Quaternion rotation)
        {
            var distance = SourcePosition.magnitude;
            var halfWidth = (width + .6f) * .5f;
            var halfHeight = (height + .6f) * .5f;
            var toCameraAxes = Quaternion.Inverse(rotation);
            var vertical = Mathf.Tan(VerticalFieldOfView * .5f * Mathf.Deg2Rad);
            for (var ix = -1; ix <= 1; ix += 2)
            for (var iz = -1; iz <= 1; iz += 2)
            for (var side = 0; side <= 1; side++)
            {
                var corner = toCameraAxes * (new Vector3(ix * halfWidth, side == 0 ? -.6f : .95f, iz * halfHeight) - focus);
                var depth = Mathf.Max(Mathf.Abs(corner.x) / (vertical * aspect), Mathf.Abs(corner.y) / vertical);
                distance = Mathf.Max(distance, depth * 1.10f - corner.z);
            }
            return distance;
        }
    }
}
