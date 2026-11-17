#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ArkMapEditor.Verification
{
    internal static class BuildOfflineArtPack
    {
        private const int Parallelism = 4;
        private static readonly List<Job> Pending = new();
        private static IEnumerator<string> sourcePaths = null!;
        private static StreamWriter log = null!;
        private static string staging = "";
        private static string output = "";
        private static int total;
        private static int completed;
        private static int failed;
        private static bool hasMore;

        private sealed class Job
        {
            public readonly string Relative;
            public readonly string Target;
            public readonly AssetBundleRecompressOperation Operation;
            public double OutputWaitStarted = -1;

            public Job(string relative, string target, AssetBundleRecompressOperation operation)
            {
                Relative = relative;
                Target = target;
                Operation = operation;
            }
        }

        public static void Run()
        {
            try
            {
                if (!Application.isBatchMode || Path.GetFileName(Path.GetDirectoryName(Application.dataPath)) != "UnityVerification")
                    throw new InvalidOperationException("Run from the isolated UnityVerification batch project.");
                var root = RequiredDirectory("ARK_PACK_WORK");
                var stagingOverride = Environment.GetEnvironmentVariable("ARK_PACK_STAGING");
                staging = string.IsNullOrEmpty(stagingOverride) ? Path.Combine(root, "Staging") : RequiredDirectory("ARK_PACK_STAGING");
                var distribution = RequiredDirectory("ARK_OFFLINE_ART_PACK");
                output = Path.Combine(distribution, "Bundles");
                Directory.CreateDirectory(output);
                var paths = File.ReadAllLines(Path.Combine(root, "paths.txt"));
                total = paths.Length;
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var relative in paths)
                    if (!set.Add(relative) || !File.Exists(Resolve(staging, relative)))
                        throw new InvalidDataException("Missing or repeated staged bundle " + relative);

                if (total == 0)
                    throw new InvalidDataException("No staged bundles.");
                sourcePaths = ((IEnumerable<string>)paths).GetEnumerator();
                hasMore = sourcePaths.MoveNext();
                log = new StreamWriter(new FileStream(Path.Combine(root, "unity-compression.tsv"), FileMode.CreateNew));
                log.AutoFlush = true;
                EditorApplication.update += Update;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Finish(1);
            }
        }

        private static void Update()
        {
            try
            {
                for (var index = Pending.Count - 1; index >= 0; index--)
                {
                    var job = Pending[index];
                    if (!job.Operation.isDone)
                        continue;
                    if (job.Operation.success && !TryPublish(job))
                        continue;
                    Pending.RemoveAt(index);
                    var status = job.Operation.humanReadableResult.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
                    if (!job.Operation.success)
                        failed++;
                    log.WriteLine($"{job.Relative}\t{job.Operation.success}\t{status}");
                    completed++;
                    if (completed % 500 == 0)
                        Debug.Log($"LZ4 bundles {completed}/{total}. Failed {failed}.");
                }
                while (hasMore && Pending.Count < Parallelism)
                {
                    var relative = sourcePaths.Current;
                    var target = Resolve(output, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    if (File.Exists(target) || File.Exists(target + ".partial"))
                        throw new IOException("Bundle output already exists: " + relative);
                    var native = AssetBundle.RecompressAssetBundleAsync(
                        Resolve(staging, relative), target + ".partial", BuildCompression.LZ4Runtime, 0, ThreadPriority.Normal);
                    Pending.Add(new Job(relative, target, native));
                    hasMore = sourcePaths.MoveNext();
                }
                if (!hasMore && Pending.Count == 0)
                    Finish(failed == 0 ? 0 : 1);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Finish(1);
            }
        }

        private static bool TryPublish(Job job)
        {
            var now = EditorApplication.timeSinceStartup;
            if (job.OutputWaitStarted < 0)
                job.OutputWaitStarted = now;
            try
            {
                File.Move(job.Target + ".partial", job.Target);
                return true;
            }
            catch (IOException) when (now - job.OutputWaitStarted < 5)
            {
                // Native completion and visibility of the output path can arrive on different updates.
                return false;
            }
        }

        private static string RequiredDirectory(string key)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrEmpty(value) || !Directory.Exists(value))
                throw new DirectoryNotFoundException(key);
            return Path.GetFullPath(value);
        }

        private static string Resolve(string root, string relative)
        {
            if (Path.IsPathRooted(relative) || relative.Split('/').AnyInvalidSegment())
                throw new InvalidDataException("Invalid bundle path: " + relative);
            return Path.Combine(root, relative);
        }

        private static bool AnyInvalidSegment(this string[] segments)
        {
            foreach (var value in segments)
                if (value.Length == 0 || value == "." || value == ".." || value.Contains("\\") || value.Contains(":"))
                    return true;
            return false;
        }

        private static void Finish(int exitCode)
        {
            EditorApplication.update -= Update;
            log?.Dispose();
            sourcePaths?.Dispose();
            Debug.Log($"LZ4 output finished: completed {completed}/{total}, failures {failed}, exit {exitCode}.");
            EditorApplication.Exit(exitCode);
        }
    }
}
