using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Arknights.MapEditor.Core;
using Arknights.MapEditor.Editor;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

namespace Arknights.MapEditor.Tests
{
    public sealed class PerformanceTests
    {
        [Test]
        public void RecordFiveRoundIsolatedSchematicAndCommandMeasurements()
        {
            var measurements = new List<object>();
            foreach (var size in new[] { new Vector2Int(30,30), new Vector2Int(60,60), new Vector2Int(120,120), new Vector2Int(80,35) })
            for (var round = 0; round < 5; round++)
            {
                var scene = EditorSceneManager.NewPreviewScene();
                var session = ScriptableObject.CreateInstance<DocumentSession>();
                var view = new ChunkedMapView(scene);
                try
                {
                    var watch = Stopwatch.StartNew();
                    var document = LevelDocument.Create(size.x, size.y);
                    session.Load(document.ToJson(), "", "");
                    view.Update(document, null);
                    watch.Stop();
                    var coldMs = watch.Elapsed.TotalMilliseconds;
                    var tile = document.GetCell(0, 0);
                    tile["heightType"] = "LOWLAND";
                    tile["tileKey"] = "tile_road";
                    watch.Restart();
                    document = document.WithCell(0, 0, tile);
                    session.Edit(document, "Performance single cell");
                    view.Update(document, new[] { new Vector2Int(0, 0) });
                    watch.Stop();
                    var hotMs = watch.Elapsed.TotalMilliseconds;
                    var edits = Enumerable.Range(0, 100).Select(i => new CellEdit(i / size.x, i % size.x, tile)).ToArray();
                    var dirty = Enumerable.Range(0, 100).Select(i => new Vector2Int(i % size.x, i / size.x)).ToArray();
                    watch.Restart();
                    document = document.WithCells(edits);
                    session.Edit(document, "Performance hundred cells");
                    view.Update(document, dirty);
                    watch.Stop();
                    var batchMs = watch.Elapsed.TotalMilliseconds;
                    var memoryBefore = Profiler.GetTotalAllocatedMemoryLong();
                    var managedBefore = GC.GetTotalMemory(false);
                    long snapshotCharacters = 0;
                    watch.Restart();
                    for (var step = 0; step < 10; step++)
                    {
                        document = document.WithLevelId("measurement_" + step);
                        snapshotCharacters += session.Json.Length;
                        session.Edit(document, "Performance undo snapshot");
                    }
                    watch.Stop();
                    var undoWriteMs = watch.Elapsed.TotalMilliseconds;
                    var memoryAfter = Profiler.GetTotalAllocatedMemoryLong();
                    var managedAfter = GC.GetTotalMemory(false);
                    Undo.PerformUndo();
                    Assert.That(session.Document.LevelId, Is.EqualTo("measurement_8"));
                    Undo.PerformRedo();
                    Assert.That(session.Document.LevelId, Is.EqualTo("measurement_9"));
                    var roots = scene.GetRootGameObjects();
                    Assert.That(view.ChunkCount, Is.EqualTo(((size.x + 15) / 16) * ((size.y + 15) / 16)));
                    measurements.Add(new {
                        width=size.x, height=size.y, round, coldMs, hotOneCellMs=hotMs,
                        hundredCellCommandMs=batchMs, tenUndoWritesMs=undoWriteMs,
                        managedMemoryDelta=managedAfter-managedBefore, totalAllocatedMemoryDelta=memoryAfter-memoryBefore,
                        undoInputSnapshotCharacters=snapshotCharacters,
                        chunkCount=view.ChunkCount, gameObjects=roots.Sum(r=>r.GetComponentsInChildren<Transform>().Length),
                        meshFilters=roots.Sum(r=>r.GetComponentsInChildren<MeshFilter>().Length),
                        renderers=roots.Sum(r=>r.GetComponentsInChildren<MeshRenderer>().Length)
                    });
                }
                finally
                {
                    Undo.ClearUndo(session);
                    Object.DestroyImmediate(session);
                    view.Dispose();
                    Assert.That(scene.rootCount, Is.Zero);
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }
            var report = new {
                schemaVersion=1, unityVersion=Application.unityVersion,
                measurement="Editor synchronous operations, not interactive frame p95; cold means new document/view, not cold process or shader cache",
                memoryCaveat="Process-wide allocation deltas include transient objects and GC; input snapshot character sum is not native Unity Undo memory size",
                cpu=SystemInfo.processorType, gpu=SystemInfo.graphicsDeviceName, memoryMiB=SystemInfo.systemMemorySize,
                roundsPerSize=5, measurements
            };
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/MapEditorPerformance.json"));
            File.WriteAllText(path, JsonConvert.SerializeObject(report, Formatting.Indented));
            TestContext.WriteLine("Performance evidence: " + path);
        }
    }
}
