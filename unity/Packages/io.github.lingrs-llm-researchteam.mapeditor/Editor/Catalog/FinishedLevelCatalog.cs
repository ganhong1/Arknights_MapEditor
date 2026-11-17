#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Arknights.MapEditor.Core;
using Newtonsoft.Json;

namespace Arknights.MapEditor.Catalog
{
    internal sealed class FinishedLevelCatalog
    {
        internal const string ArchiveName = "finished-levels.zip";
        private const string RequestIdsName = "finished-level-request-ids.json";
        internal const string PackageName = "io.github.lingrs-llm-researchteam.mapeditor";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly string path;
        private readonly Index index;
        internal IReadOnlyList<StageGroup> Groups { get; }
        internal IEnumerable<string> BodyIds => index.Bodies.Keys;

        internal sealed class Source
        {
            public string Body { get; set; } = "";
            [JsonProperty("source")] public string Repository { get; set; } = "";
            public string? Revision { get; set; }
            public string Path { get; set; } = "";
            public string Sha256 { get; set; } = "";
            public string[] Stages { get; set; } = Array.Empty<string>();
            public string[] Roles { get; set; } = Array.Empty<string>();
        }

        internal sealed class Stage
        {
            public string Id { get; set; } = "";
            public string Group { get; set; } = "";
            public string?[]? Label { get; set; }
            [JsonProperty("mode")] public string? ModeOverride { get; set; }
            public string[] Bodies { get; set; } = Array.Empty<string>();
            internal string Code => At(0) ?? Id;
            internal string Name => At(1) ?? "";
            internal string Display => Name.Length > 0 ? Code + " · " + Name : Code;
            internal string Mode => (ModeOverride ?? At(2)) is { Length: > 0 } mode ? GameStageLabel.Mode(mode, Code) : Id;
            private string? At(int position) => Label != null && Label.Length > position ? Label[position] : null;
        }

        internal sealed class Variant
        {
            internal Stage? Stage { get; }
            internal Source Source { get; }
            internal string? RequestLevelId { get; }
            internal string LevelId => RequestLevelId ?? (Source.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? Source.Path.Substring(0, Source.Path.Length - ".json".Length) : Source.Path);
            internal string Title => Stage?.Display ?? System.IO.Path.GetFileNameWithoutExtension(Source.Path);
            internal string Label => GameStageLabel.Title(Title, Stage?.Mode);
            internal string NewDocumentTitle => Stage == null || Stage.Mode == Stage.Id
                ? Title : Title + " · " + Stage.Mode;
            internal string Origin => JsonConvert.SerializeObject(new
            {
                stageId = Stage?.Id, code = Stage?.Code, name = Stage?.Name, mode = Stage?.Mode, levelId = LevelId
            });
            internal Variant(Stage? stage, Source source, string? requestLevelId)
            { Stage = stage; Source = source; RequestLevelId = requestLevelId; }
        }

        internal sealed class StageGroup
        {
            internal string Title { get; }
            internal bool IsComponent { get; }
            internal IReadOnlyList<Variant> Variants { get; }
            internal StageGroup(string title, bool isComponent, Variant[] variants)
            {
                Title = title;
                IsComponent = isComponent;
                Variants = variants;
            }

            internal IEnumerable<Variant> GameVariants() => Variants
                .GroupBy(variant => (variant.Stage?.Id, variant.Source.Body))
                .Select(group => group.FirstOrDefault(variant => variant.RequestLevelId != null) ?? group.First());
        }

        private sealed class Index
        {
            public int Version { get; set; }
            public Stage[] Stages { get; set; } = Array.Empty<Stage>();
            public Source[] Sources { get; set; } = Array.Empty<Source>();
            public Dictionary<string, int> Bodies { get; set; } = new Dictionary<string, int>();
        }

        private FinishedLevelCatalog(string path, Index index, Dictionary<string, Dictionary<string, string>> requestIds)
        {
            this.path = path;
            this.index = index;
            foreach (var pair in index.Bodies)
                if (pair.Value < 0 || pair.Value >= index.Sources.Length || !IsDigest(pair.Key) ||
                    index.Sources[pair.Value].Body != pair.Key || !IsDigest(index.Sources[pair.Value].Sha256))
                    throw new InvalidDataException("成品关卡正文索引有误。");

            var byStage = index.Sources.SelectMany(source => source.Stages.Select(stage => (stage, source)))
                .ToLookup(pair => pair.stage, pair => pair.source, StringComparer.Ordinal);
            var stages = index.Stages.GroupBy(stage => stage.Group, StringComparer.Ordinal).Select(group =>
            {
                var ordered = group.OrderBy(stage => stage.Id == group.Key ? 0 : 1).ThenBy(stage => stage.Id, StringComparer.Ordinal).ToArray();
                var variants = ordered.SelectMany(stage =>
                {
                    var sources = byStage[stage.Id].ToArray();
                    if (!new HashSet<string>(stage.Bodies, StringComparer.Ordinal).SetEquals(sources.Select(source => source.Body)))
                        throw new InvalidDataException("成品关卡版本索引有误。");
                    requestIds.TryGetValue(stage.Id, out var stageRequests);
                    return sources.Select(source =>
                    {
                        string? levelId = null;
                        stageRequests?.TryGetValue(source.Repository + "/" + source.Path, out levelId);
                        return new Variant(stage, source, levelId);
                    });
                }).ToArray();
                return new StageGroup(ordered[0].Display, false, variants);
            });
            var components = index.Sources.Where(source => source.Stages.Length == 0)
                .GroupBy(source => (Role: source.Roles.FirstOrDefault() ?? "", source.Path))
                .Select(group => new StageGroup(ComponentName(group.Key.Role) + " · " +
                    System.IO.Path.GetFileNameWithoutExtension(group.Key.Path), true,
                    group.Select(source => new Variant(null, source, null)).ToArray()));
            Groups = stages.Concat(components).ToArray();
        }

        internal static FinishedLevelCatalog Load(string path)
        {
            using var archive = Open(path);
            var entry = archive.GetEntry("library-index.json") ?? throw new InvalidDataException("缺少成品关卡索引。");
            if (entry.Length > LevelDocument.MaximumUtf8Bytes) throw new InvalidDataException("成品关卡索引过大。");
            using var stream = entry.Open();
            using var text = new StreamReader(stream, Utf8);
            using var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None, MaxDepth = 16 };
            var index = JsonSerializer.CreateDefault().Deserialize<Index>(reader);
            if (index?.Version != 1 || index.Bodies.Count == 0 || archive.Entries.Count != index.Bodies.Count + 1)
                throw new InvalidDataException("成品关卡索引格式不匹配。");
            var idsPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path) ?? "", RequestIdsName);
            var requestIds = File.Exists(idsPath)
                ? JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(idsPath, Utf8))
                    ?? throw new InvalidDataException("成品关卡请求 ID 索引有误。")
                : new Dictionary<string, Dictionary<string, string>>();
            return new FinishedLevelCatalog(path, index, requestIds);
        }

        internal static string BundledPath()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(FinishedLevelCatalog).Assembly);
            if (package == null) throw new InvalidOperationException("找不到地图编辑器安装目录。");
            return System.IO.Path.Combine(package.resolvedPath, "Editor", "Catalog", ArchiveName);
        }

        internal ZipArchive OpenBodies() => Open(path);

        internal LevelDocument ReadVariant(Variant variant)
        {
            using var archive = OpenBodies();
            var document = LevelDocument.Parse(ReadBody(archive, variant.Source.Body));
            return document.WithLevelId(variant.LevelId);
        }

        internal string ReadBody(ZipArchive archive, string body)
        {
            if (!index.Bodies.TryGetValue(body, out var number)) throw new InvalidDataException("未收录成品关卡正文。");
            var entry = archive.GetEntry("levels/" + body + ".json") ?? throw new InvalidDataException("缺少成品关卡正文。");
            if (entry.Length < 1 || entry.Length > LevelDocument.MaximumUtf8Bytes) throw new InvalidDataException("成品关卡正文大小超出范围。");
            using var stream = entry.Open();
            var bytes = new byte[(int)entry.Length];
            var position = 0;
            while (position < bytes.Length)
            {
                var count = stream.Read(bytes, position, bytes.Length - position);
                if (count == 0) throw new InvalidDataException("成品关卡正文不完整。");
                position += count;
            }
            using var sha = SHA256.Create();
            var actual = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            if (stream.ReadByte() != -1 || actual != index.Sources[number].Sha256)
                throw new InvalidDataException("成品关卡正文校验失败。");
            return Utf8.GetString(bytes);
        }

        private static ZipArchive Open(string path)
        {
            var stream = File.OpenRead(path);
            try { return new ZipArchive(stream, ZipArchiveMode.Read); }
            catch { stream.Dispose(); throw; }
        }

        private static bool IsDigest(string value) => value != null && value.Length == 64 &&
            value.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');

        private static string ComponentName(string role) => role switch
        {
            "auto chess round" or "historical auto chess round" => "自走棋回合",
            "auto chess exit template" => "自走棋退场模板",
            "auto chess enemy template" => "自走棋敌方模板",
            "historical crisis map template" or "historical crisis activity template" => "危机合约地图模板",
            "sandbox base layout" or "sandbox empty layout" => "生息演算底图",
            _ => "玩法资源"
        };
    }
}
