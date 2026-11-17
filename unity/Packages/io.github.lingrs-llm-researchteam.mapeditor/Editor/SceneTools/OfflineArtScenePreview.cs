#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Arknights.MapEditor.Catalog;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Arknights.MapEditor.Editor
{
    internal sealed class OfflineArtSceneFrame
    {
        internal Texture2D Image { get; }
        internal string Description { get; }

        internal OfflineArtSceneFrame(Texture2D image, string description)
        {
            Image = image;
            Description = description;
        }
    }

    internal static class OfflineArtScenePreview
    {
        private static readonly SemaphoreSlim WorkerGate = new SemaphoreSlim(1, 1);

        [Serializable]
        private sealed class Request
        {
            public string scene = "";
            public string[] bundles = Array.Empty<string>();
        }

        [Serializable]
        private sealed class Result
        {
            public bool success;
            public string message = "";
        }

        internal static async Task<OfflineArtSceneFrame> RenderAsync(OfflineArtCatalog catalog, OfflineArtEntry entry,
            CancellationToken cancellation)
        {
            var project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var home = Path.Combine(project, "Library", "ArknightsMapEditor", "OfflineScenePreview");
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(OfflineArtScenePreview).Assembly);
            if (package == null) throw new InvalidOperationException("找不到场景预览工具的安装目录。");
            var workerSource = File.ReadAllText(Path.Combine(package.resolvedPath, "Tools~", "ScenePreviewWorker.cs.txt"));
            var request = new Request
            {
                scene = entry.UnityPath,
                bundles = catalog.LoadOrder(new[] { entry }).Select(catalog.BundleFile).ToArray()
            };
            var key = CacheKey(catalog.ResourceVersion, request.scene, request.bundles, workerSource);
            var frames = Path.Combine(home, "Frames");
            var cachedFrame = Path.Combine(frames, key + ".png");
            var cachedResult = Path.Combine(frames, key + ".json");

            await WorkerGate.WaitAsync(cancellation);
            try
            {
                cancellation.ThrowIfCancellationRequested();
                if (File.Exists(cachedFrame) && File.Exists(cachedResult))
                {
                    try { return ReadFrame(cachedFrame, ReadResult(cachedResult)); }
                    catch (Exception error) when (error is InvalidDataException || error is ArgumentException || error is UnityException)
                    {
                        File.Delete(cachedFrame);
                        File.Delete(cachedResult);
                    }
                }

                var worker = PrepareWorker(home, workerSource);
                var job = Path.Combine(home, "Jobs", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(job);
                var requestPath = Path.Combine(job, "request.json");
                var log = Path.Combine(job, "unity.log");
                var output = Path.Combine(job, "frame.png");
                var result = Path.Combine(job, "result.json");
                File.WriteAllText(requestPath, JsonUtility.ToJson(request));
                var discardJob = false;
                try
                {
                    await RunWorker(worker, requestPath, log, cancellation);
                    var frame = ReadFrame(output, ReadResult(result));
                    discardJob = true;
                    try
                    {
                        Directory.CreateDirectory(frames);
                        File.Copy(output, cachedFrame, true);
                        File.Copy(result, cachedResult, true);
                    }
                    catch (Exception failure) when (failure is IOException || failure is UnauthorizedAccessException)
                    {
                        PluginLog.Failure("art-pack.scene-cache-write", failure);
                    }
                    return frame;
                }
                catch (OperationCanceledException)
                {
                    discardJob = true;
                    throw;
                }
                catch (Exception failure) when (failure is IOException || failure is InvalidDataException ||
                    failure is UnauthorizedAccessException || failure is ArgumentException || failure is InvalidOperationException)
                {
                    throw new InvalidOperationException(failure.Message + "\n后台日志 " + log, failure);
                }
                finally
                {
                    if (discardJob) CleanJob(job, requestPath, output, result, log);
                }
            }
            finally { WorkerGate.Release(); }
        }

        internal static string CacheKey(string resourceVersion, string scene, string[] paths, string workerSource)
        {
            var identity = new StringBuilder(resourceVersion).AppendLine().AppendLine(scene).AppendLine(workerSource);
            foreach (var path in paths)
            {
                var file = new FileInfo(path);
                if (!file.Exists) throw new FileNotFoundException("美术包文件缺失 " + file.Name, path);
                identity.AppendLine(file.FullName).Append(file.Length).Append(' ').AppendLine(file.LastWriteTimeUtc.Ticks.ToString());
            }
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity.ToString()))).Replace("-", "").ToLowerInvariant();
        }

        private static Result ReadResult(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 64 * 1024)
                throw new InvalidDataException("后台 Unity 未返回场景预览结果。");
            var result = JsonUtility.FromJson<Result>(File.ReadAllText(path));
            return result ?? throw new InvalidDataException("无法读取后台 Unity 预览结果。");
        }

        private static OfflineArtSceneFrame ReadFrame(string path, Result result)
        {
            if (!result.success) throw new InvalidDataException(result.message);
            var file = new FileInfo(path);
            if (!file.Exists || file.Length < 12 || file.Length > 8 * 1024 * 1024)
                throw new InvalidDataException("后台 Unity 未生成有效的预览图。");
            var bytes = File.ReadAllBytes(path);
            var pngSignature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
            var pngEnd = new byte[] { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 };
            if (!bytes.Take(pngSignature.Length).SequenceEqual(pngSignature) ||
                !bytes.Skip(bytes.Length - pngEnd.Length).SequenceEqual(pngEnd))
                throw new InvalidDataException("后台 Unity 预览图不完整。");
            return new OfflineArtSceneFrame(DecodeFrame(bytes), result.message);
        }

        internal static Texture2D DecodeFrame(byte[] bytes)
        {
            const int width = 1280, height = 720;
            if (bytes.Length < 33 || ReadPngInt(bytes, 8) != 13 || ReadPngInt(bytes, 12) != 0x49484452 ||
                ReadPngInt(bytes, 16) != width || ReadPngInt(bytes, 20) != height)
                throw new InvalidDataException("场景预览图尺寸或文件头无效。");

            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                if (!ImageConversion.LoadImage(texture, bytes, true) || texture.width != width || texture.height != height)
                    throw new InvalidDataException("场景预览图无法解码。");
                return texture;
            }
            catch
            {
                Object.DestroyImmediate(texture);
                throw;
            }
        }

        private static int ReadPngInt(byte[] bytes, int offset) =>
            bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3];

        private static void CleanJob(string directory, params string[] files)
        {
            try
            {
                foreach (var file in files) File.Delete(file);
                Directory.Delete(directory);
            }
            catch (Exception failure) when (failure is IOException || failure is UnauthorizedAccessException)
            {
                PluginLog.Failure("art-pack.scene-job-cleanup", failure);
            }
        }

        private static string PrepareWorker(string home, string source)
        {
            var project = Path.Combine(home, "Worker");
            WriteIfChanged(Path.Combine(project, "Assets", "Editor", "OfflineScenePreviewWorker.cs"), source);
            WriteIfChanged(Path.Combine(project, "ProjectSettings", "ProjectVersion.txt"),
                "m_EditorVersion: " + Application.unityVersion + "\n");

            var modules = new[] { "assetbundle", "jsonserialize", "particlesystem", "animation", "imgui",
                "audio", "terrain", "terrainphysics", "physics", "physics2d", "imageconversion", "uielements" };
            var lines = modules.Select(module => "\"com.unity.modules." + module + "\":\"1.0.0\"");
            WriteIfChanged(Path.Combine(project, "Packages", "manifest.json"), "{\"dependencies\":{" + string.Join(",", lines) + "}}");
            return project;
        }

        private static void WriteIfChanged(string file, string body)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            if (!File.Exists(file) || File.ReadAllText(file) != body) File.WriteAllText(file, body);
        }

        private static async Task RunWorker(string worker, string request, string log, CancellationToken cancellation)
        {
            string Quote(string path) => "\"" + path + "\"";
            var launch = new ProcessStartInfo
            {
                FileName = EditorApplication.applicationPath,
                Arguments = "-batchmode -force-d3d11 -projectPath " + Quote(worker) +
                    " -executeMethod OfflineScenePreviewWorker.Start -arkScenePreviewRequest " + Quote(request) +
                    " -logFile " + Quote(log),
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false
            };
            var temporary = Path.Combine(Path.GetDirectoryName(worker)!, "Temp");
            Directory.CreateDirectory(temporary);
            launch.EnvironmentVariables["TEMP"] = temporary;
            launch.EnvironmentVariables["TMP"] = temporary;
            using var process = Process.Start(launch) ?? throw new InvalidOperationException("无法启动后台 Unity。");

            void StopWorker()
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        if (!process.WaitForExit(1200))
                            PluginLog.Failure("art-pack.scene-worker-stop", new TimeoutException("后台 Unity 未及时退出。"));
                    }
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception failure) { PluginLog.Failure("art-pack.scene-worker-stop", failure); }
            }

            AssemblyReloadEvents.beforeAssemblyReload += StopWorker;
            EditorApplication.quitting += StopWorker;
            try
            {
                var clock = Stopwatch.StartNew();
                while (!process.HasExited)
                {
                    if (clock.Elapsed > TimeSpan.FromMinutes(2))
                        throw new InvalidOperationException("场景渲染超时。日志 " + log);
                    await Task.Delay(140, cancellation);
                }
                cancellation.ThrowIfCancellationRequested();
                if (process.ExitCode != 0 && !File.Exists(Path.Combine(Path.GetDirectoryName(request)!, "result.json")))
                    throw new InvalidOperationException("后台 Unity 退出码 " + process.ExitCode + "。日志 " + log);
            }
            finally
            {
                AssemblyReloadEvents.beforeAssemblyReload -= StopWorker;
                EditorApplication.quitting -= StopWorker;
                StopWorker();
            }
        }
    }
}
