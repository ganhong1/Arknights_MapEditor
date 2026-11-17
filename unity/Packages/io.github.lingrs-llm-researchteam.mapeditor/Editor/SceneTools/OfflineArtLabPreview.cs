#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Catalog;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static Arknights.MapEditor.Catalog.OfflineArtRecord;
using Object = UnityEngine.Object;

namespace Arknights.MapEditor.Editor
{
    internal sealed class OfflineArtLabPreview : IDisposable
    {
        private readonly OfflineArtPreviewScene stage;
        private readonly Material surface;
        private readonly Material composite;
        private readonly Material? sky;
        private readonly Color ambient = new Color(0.115f, 0.15f, 0.19f);
        private RenderTexture? output;
        internal OfflineArtCameraDemo? Motion { get; }
        internal OfflineArtReferenceMap Map { get; }
        internal bool HasVisualParameters { get; private set; }

        internal OfflineArtLabPreview(OfflineArtReferenceMap map, OfflineArtRecord? record, OfflineArtCameraDemo? motion, Cubemap? reflection)
        {
            Map = map;
            Motion = motion;
            stage = new OfflineArtPreviewScene();
            try
            {
                surface = stage.NewMaterial("Hidden/Arknights/OfflineArtSurface", Color.white);
                composite = stage.NewMaterial("Hidden/Arknights/OfflineArtComposite", Color.white);
                stage.BuildReferenceMap(map.Level, surface);
                if (reflection != null)
                {
                    sky = stage.NewMaterial("Hidden/Arknights/OfflineArtCube", Color.white);
                    sky.SetTexture("_Cube", reflection);
                    stage.AddReferenceSphere(sky);
                    HasVisualParameters = true;
                }
                if (record != null && motion == null) BindEnvironment(record);
            }
            catch
            {
                stage.Dispose();
                throw;
            }
        }

        private void BindEnvironment(OfflineArtRecord record)
        {
            var fields = record.Fields;
            if (record.Type == "SceneEffectProfile")
            {
                var fog = Enabled(fields["heightFog"]);
                var tint = Enabled(fields["shadowTint"]);
                surface.SetFloat("_FogActive", fog ? 1f : 0f);
                surface.SetColor("_FogColor", ReadColor(fields["fogColor"], Color.white));
                var bottom = Number(fields["fogBaseline"]);
                surface.SetVector("_FogRange", new Vector4(bottom,
                    Mathf.Max(bottom + 0.05f, Number(fields["fogTop"], 2f)), 0f, 0f));
                var value = tint ? ReadColor(fields["shadowTintColor"], Color.white) : Color.white;
                surface.SetColor("_ShadeTint", value);
                HasVisualParameters = fog || (tint && value != Color.white);
                return;
            }
            if (record.Type != "PostProcessProfile" || record.Settings == null) return;

            foreach (var setting in record.Settings.OfType<JObject>())
            {
                if (!(setting["fields"] is JObject config) || !Enabled(config["active"]) ||
                    !Enabled(Parameter(config, "enabled"))) continue;
                switch ((string?)setting["type"])
                {
                    case "Vignette":
                        if (Number(Parameter(config, "mode")) != 0f) break;
                        var intensity = Mathf.Clamp01(Number(Parameter(config, "intensity")));
                        var smoothness = Mathf.Clamp01(Number(Parameter(config, "smoothness"), 0.6f));
                        var center = Parameter(config, "center");
                        composite.SetVector("_Vignette", new Vector4(intensity, smoothness,
                            Number(center?["x"], .5f), Number(center?["y"], .5f)));
                        composite.SetColor("_VignetteColor", ReadColor(Parameter(config, "color"), Color.black));
                        HasVisualParameters |= intensity > 0f;
                        break;
                    case "HGMobileBloom":
                        var strength = Mathf.Clamp(Number(Parameter(config, "intensity")), 0f, 4f);
                        composite.SetVector("_Bloom", new Vector4(strength,
                            Mathf.Max(0f, Number(Parameter(config, "threshold"), .6f)),
                            Mathf.Clamp(Number(Parameter(config, "sepBlurSpread"), 2f), 1f, 12f), 0f));
                        HasVisualParameters |= strength > 0f;
                        break;
                    case "ColorGrading":
                    case "HGColorGrading":
                        var exposure = Number(Parameter(config, "postExposure"));
                        var saturation = Number(Parameter(config, "saturation")) / 100f;
                        var contrast = Number(Parameter(config, "contrast")) / 100f;
                        var filter = ReadColor(Parameter(config, "colorFilter"), Color.white);
                        composite.SetVector("_Grading", new Vector4(Mathf.Pow(2f, Mathf.Clamp(exposure, -5f, 5f)),
                            Mathf.Clamp(1f + saturation, 0f, 2f), Mathf.Clamp(1f + contrast, 0f, 2f), 1f));
                        composite.SetColor("_Filter", filter);
                        HasVisualParameters |= exposure != 0f || saturation != 0f || contrast != 0f || filter != Color.white;
                        break;
                }
            }
        }

        private static JToken? Parameter(JObject fields, string key) =>
            fields[key] is JObject parameter && Enabled(parameter["overrideState"]) ? parameter["value"] : null;

        private static Color ReadColor(JToken? value, Color fallback) => value is JObject rgba ?
            new Color(Number(rgba["r"], fallback.r), Number(rgba["g"], fallback.g),
                Number(rgba["b"], fallback.b), Number(rgba["a"], fallback.a)) : fallback;

        internal Texture Render(Rect rect, float time, bool applied)
        {
            var camera = stage.Camera;
            var offset = applied ? Motion?.OffsetAt(time) ?? Vector3.zero : Vector3.zero;
            OfflineArtBattleProjection.Apply(camera, Map.Level.Width, Map.Level.Height,
                rect.width / rect.height, offset, applied && Motion?.FocusCompensation == true);
            camera.backgroundColor = ambient;
            surface.SetFloat("_Applied", applied ? 1f : 0f);
            if (sky != null) sky.SetFloat("_Applied", applied ? 1f : 0f);
            var baseFrame = stage.RenderConfigured(rect);

            if (!applied || (Motion?.Dither != true && !HasVisualParameters)) return baseFrame;
            EnsureOutput(baseFrame);
            var dither = Motion?.Dither == true ? Mathf.Sin(Mathf.PI * Mathf.Clamp01(time / Motion.Duration)) : 0f;
            composite.SetFloat("_Dither", dither);
            var previous = RenderTexture.active;
            try { Graphics.Blit(baseFrame, output, composite); }
            finally { RenderTexture.active = previous; }
            return output!;
        }

        private void EnsureOutput(Texture source)
        {
            if (output != null && output.width == source.width && output.height == source.height) return;
            ReleaseOutput();
            output = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32)
                { hideFlags = HideFlags.HideAndDontSave, name = "Offline art comparison" };
            output.Create();
        }

        private void ReleaseOutput()
        {
            if (output == null) return;
            output.Release();
            Object.DestroyImmediate(output);
            output = null;
        }

        public void Dispose()
        {
            ReleaseOutput();
            stage.Dispose();
        }
    }
}
