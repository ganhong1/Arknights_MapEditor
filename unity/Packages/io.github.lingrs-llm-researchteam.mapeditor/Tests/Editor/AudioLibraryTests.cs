#nullable enable
using System;
using System.Collections;
using System.Linq;
using Arknights.MapEditor.Catalog;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor.Tests
{
    public sealed partial class MusicWorkspaceTests
    {
        [Test]
        public void OriginalAudioUsesNativeEventKeysAndKeepsSharedClips()
        {
            var catalog = AudioLibraryCatalog.LoadBundled();
            Assert.That(catalog.Music.Count, Is.EqualTo(807));
            Assert.That(catalog.Music.Count(row => row.EventName != row.BankName), Is.EqualTo(230));
            var tracks = catalog.Music.SelectMany(row => new[] { row.Intro, row.Loop })
                .OfType<MusicEventCatalog.Clip>().ToArray();
            Assert.That(tracks.Select(track => track.Asset).Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(941));
            Assert.That(tracks.Where(track => track.File == null).Select(track => track.Asset), Is.Empty);
            Assert.That(catalog.Music.Single(row => !row.HasPreview).EventName, Is.EqualTo("sys.ON_ACTIVITY_LOADED.act17d7"));
            Assert.That(catalog.Effects.Count, Is.EqualTo(227));
            Assert.That(catalog.EffectClips.Select(row => row.ClipAsset).Distinct().Count(), Is.EqualTo(144));
            Assert.That(catalog.Effects.SelectMany(row => row.Usage).Distinct().Count(), Is.EqualTo(20));
            var alias = catalog.Find(AudioLibraryCatalog.TriggerGroup, "frostnovaevolutionhard")!;
            Assert.That(alias.Address.Signal, Is.EqualTo("ON_CUSTOM_TRIGGER"));
            Assert.That(alias.BankName, Is.EqualTo("battle.ON_GAME_READY.frostnovaevolutionhard"));
            var effects = catalog.EventsForClip("audio/sound_beta_2/ambience/a_bat/a_bat_volcano").ToArray();
            Assert.That(effects.SelectMany(row => row.Usage), Has.Some.StartWith("地块音效/"));
            Assert.That(effects.SelectMany(row => row.Usage), Has.Some.StartWith("装置音效/"));
            var floating = catalog.Effects.Single(row => row.EventName.EndsWith(".tile_float", StringComparison.Ordinal));
            Assert.That(floating.Address.SubSignal, Is.EqualTo("env_038_act47side.tile_float"));
            Assert.That(catalog.Music.Single(row => row.EventName == AudioLibraryCatalog.OpeningGroup).Address.SubSignal, Is.Empty);
        }

        [UnityTest]
        public IEnumerator AudioBrowserSwitchesViewsAndConfinesSelectionToTheCurrentField()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            try
            {
                var document = MakeDocument(MusicAction("audio", 2)).WithSection("environmentSe", "\"beach\"");
                workspace.ShowUtility();
                workspace.CreateGUI();
                workspace.AddDocument(0, "音频分类回归", document.ToJson());
                var editor = workspace.FocusedEditor!;
                var inspector = OpenInspector(workspace);
                yield return null;
                var root = inspector.rootVisualElement;
                var music = Module(root, 11, "music-editor");
                Click(Find<Button>(music, "music-open-library"));
                var library = Find<VisualElement>(root, "music-library");
                Click(Find<Button>(library, "audio-library-native"));
                Assert.That(AudioRows(library).All(row => row.Address.Group == AudioLibraryCatalog.OpeningGroup), Is.True);
                SelectAudio(library, row => row.Address.SubSignal == "shatterpoint_02");
                Click(Find<Button>(library, "audio-library-usage"));
                Click(Find<Button>(library, "music-library-assign"));
                Assert.That(Find<TextField>(music, "music-opening-key").value, Is.EqualTo("shatterpoint_02"));
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(document.ToJson()));

                var trigger = SelectMusicAction(root, 0);
                Click(Find<Button>(trigger, "music-open-library"));
                Click(Find<Button>(library, "audio-library-native"));
                Assert.That(AudioRows(library).All(row => row.Address.Group == AudioLibraryCatalog.TriggerGroup), Is.True);
                SelectAudio(library, row => row.Address.SubSignal == "boss_sui");
                Click(Find<Button>(library, "music-library-assign"));
                Assert.That(Find<TextField>(trigger, "music-input-key").value, Is.EqualTo("boss_sui"));
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(document.ToJson()));
                Click(Find<Button>(library, "audio-library-usage"));

                Click(Find<Button>(root, "workbench-tab-12"));
                var environment = Find<ScrollView>(root, "workbench-page-12");
                Click(Find<Button>(environment, "music-open-library"));
                Assert.That(AudioRows(library).Select(row => row.Key), Is.EquivalentTo(new[] { "a_bat_beach", "a_bat_magma" }));
                Click(Find<Button>(library, "audio-library-native"));
                Assert.That(AudioRows(library).Select(row => row.Address.SubSignal), Is.EquivalentTo(new[] { "beach", "magma" }));
                Assert.That(Find<Button>(root, "resource-category-6").ClassListContains("selected-tab"), Is.True);
                SelectAudio(library, row => row.Address.SubSignal == "magma");
                Click(Find<Button>(library, "audio-library-usage"));
                Click(Find<Button>(library, "music-library-assign"));
                Assert.That(Find<TextField>(environment, "global-field-environmentSe").value, Is.EqualTo("\"magma\""));
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(document.ToJson()));
                Click(Find<Button>(root, "resource-category-5"));
                Assert.That(library.Q<Button>("music-library-assign"), Is.Null);
                ClickText(environment, "应用 environmentSe");
                Assert.That(editor.WorkspaceDocument.GetSection("environmentSe")!.ToString(), Is.EqualTo("magma"));
                Assert.That(editor.WorkspaceDocument.GetSection("bgmEvent")!.ToString(), Is.EqualTo("shield"));
                editor.UndoFromShortcut();
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(document.ToJson()));
            }
            finally { UnityEngine.Object.DestroyImmediate(workspace); }
        }

        [UnityTest]
        public IEnumerator AudioBrowserShowsEachClipOnceByUsageAndEachEventInOriginalView()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            try
            {
                workspace.ShowUtility();
                workspace.CreateGUI();
                workspace.AddDocument(0, "音效分类回归", MakeDocument().ToJson());
                var root = OpenInspector(workspace).rootVisualElement;
                yield return null;
                Click(Find<Button>(root, "resource-category-5"));
                var library = Find<VisualElement>(root, "music-library");
                var categoryStrip = Find<ScrollView>(root, "resource-category-strip");
                Assert.That(Find<Button>(categoryStrip, "resource-category-5").text, Is.EqualTo("音乐"));
                var audioTab = Find<Button>(categoryStrip, "resource-category-6");
                Assert.That(audioTab.text, Is.EqualTo("音效"));
                Assert.That(Find<Button>(categoryStrip, "music-event-audio-root").text, Is.EqualTo("试听包"));
                Assert.That(library.Q<Button>("music-event-audio-root"), Is.Null);
                Click(audioTab);
                Assert.That(AudioRows(library).Count(), Is.EqualTo(144));
                var group = Find<DropdownField>(library, "audio-library-group");
                Assert.That(group.choices.Count, Is.EqualTo(7));
                group.value = "地块音效";
                var subcategories = Find<ResourceSubcategoryList>(library, "audio-library-signal");
                Assert.That(subcategories.Query<Button>().ToList().Any(button => button.text == "地表状态变化"), Is.True);
                SelectAudio(library, row => row.Key == "a_bat_volcano");
                Click(Find<Button>(library, "audio-library-native"));
                Assert.That(AudioRows(library).Count(), Is.EqualTo(227));
                Find<DropdownField>(library, "audio-library-group").value = "battle";
                subcategories = Find<ResourceSubcategoryList>(library, "audio-library-signal");
                Click(subcategories.Query<Button>().ToList().Single(button => button.text == "ON_TILE_TRIGGER"));
                Assert.That(AudioRows(library).Count(), Is.EqualTo(4));
                Click(Find<Button>(library, "audio-library-usage"));
                Assert.That(Find<DropdownField>(library, "audio-library-group").value, Is.EqualTo("地块音效"));
                Assert.That(library.Q<Button>("music-library-assign"), Is.Null);
            }
            finally { UnityEngine.Object.DestroyImmediate(workspace); }
        }

        [UnityTest]
        public IEnumerator VerifiedMonoEffectPlaysThroughTheExistingPreviewEngine()
        {
            if (MusicPreview.AudioRoot(true) == null) Assert.Ignore("Requires the verified local effect audition pack.");
            var catalog = AudioLibraryCatalog.LoadBundled();
            var effect = catalog.EffectClips.First(row => row.Key == "a_bat_bdlnctr");
            Assert.That(effect.Intro!.Channels, Is.EqualTo(1));
            var owner = new object();
            var state = "";
            try
            {
                MusicPreview.Start(owner, effect, false, null, value => state = value);
                var deadline = Time.realtimeSinceStartup + 8;
                while (state == "正在读取试听音轨…" && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(state, Is.EqualTo("正在试听"));
                MusicPreview.Stop(owner);
                Assert.That(state, Is.EqualTo("试听已停止"));
            }
            finally { MusicPreview.Stop(owner); }
        }

        [UnityTest]
        public IEnumerator DetailsPlayerPausesSeeksResumesAndStopsOnClose()
        {
            if (MusicPreview.AudioRoot(true) == null) Assert.Ignore("Requires the verified local effect audition pack.");
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            try
            {
                workspace.ShowUtility();
                workspace.CreateGUI();
                workspace.AddDocument(0, "详情试听回归", MakeDocument().ToJson());
                var root = OpenInspector(workspace).rootVisualElement;
                yield return null;
                Click(Find<Button>(root, "resource-category-6"));
                var library = Find<VisualElement>(root, "music-library");
                var list = Find<ListView>(library, "music-library-list");
                var index = Enumerable.Range(0, list.itemsSource.Count)
                    .Single(i => ((MusicEventCatalog.Entry)list.itemsSource[i]!).Key == "a_bat_beach");
                var row = list.makeItem();
                list.bindItem(row, index);
                Click(Find<Button>(row, "music-library-info-a_bat_beach"));
                var detail = Find<VisualElement>(root, "music-library-detail");
                var player = Find<AudioDetailPlayer>(detail, "music-detail-player");
                var technical = Find<Foldout>(detail, "audio-detail-technical");
                Assert.That(technical.value, Is.False);
                technical.value = true;
                var localPath = Find<Button>(technical, "audio-detail-local-path");
                Assert.That(System.IO.File.Exists(localPath.text), Is.True);
                Assert.That(localPath.text, Does.EndWith("a_bat_beach.ogg"));
                Assert.That(localPath.ClassListContains("audio-detail-path"), Is.True);
                Assert.That(detail.Query<Label>().ToList().Any(label => label.text.Contains("2.7.81")), Is.False);
                var play = Find<Button>(player, "music-detail-play");
                Click(play);
                var deadline = Time.realtimeSinceStartup + 8;
                while (MusicPreview.ReadPlayback(detail).Phase == MusicPreview.PlaybackPhase.Loading &&
                    Time.realtimeSinceStartup < deadline)
                    yield return null;
                var playing = MusicPreview.ReadPlayback(detail);
                Assert.That(playing.Phase, Is.EqualTo(MusicPreview.PlaybackPhase.Playing));
                Assert.That(playing.Duration, Is.InRange(16f, 17f));
                var advanceUntil = Time.realtimeSinceStartup + 0.2f;
                while (Time.realtimeSinceStartup < advanceUntil) yield return null;
                Click(play);
                Assert.That(MusicPreview.ReadPlayback(detail).Phase, Is.EqualTo(MusicPreview.PlaybackPhase.Paused));
                yield return null;
                var seek = Find<Slider>(player, "music-detail-seek");
                Assert.That(seek.enabledInHierarchy, Is.True);
                seek.value = 5;
                Assert.That(MusicPreview.ReadPlayback(detail).Seconds, Is.EqualTo(5).Within(0.02));
                Click(play);
                advanceUntil = Time.realtimeSinceStartup + 0.2f;
                while (Time.realtimeSinceStartup < advanceUntil) yield return null;
                Assert.That(MusicPreview.ReadPlayback(detail).Seconds, Is.GreaterThanOrEqualTo(5));
                Click(Find<Button>(root, "music-library-detail-close"));
                Assert.That(MusicPreview.ReadPlayback(detail).Phase, Is.EqualTo(MusicPreview.PlaybackPhase.Idle));
            }
            finally { UnityEngine.Object.DestroyImmediate(workspace); }
        }

        [UnityTest]
        public IEnumerator AudioSearchFindsMusicTrackIdsAndSoundNamesAcrossViews()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            try
            {
                workspace.ShowUtility();
                workspace.CreateGUI();
                workspace.AddDocument(0, "音频搜索回归", MakeDocument().ToJson());
                var root = OpenInspector(workspace).rootVisualElement;
                yield return null;
                Click(Find<Button>(root, "resource-category-5"));
                var library = Find<VisualElement>(root, "music-library");
                var query = Find<UnityEditor.UIElements.ToolbarSearchField>(library, "music-event-search");
                var status = Find<Label>(library, "audio-library-results-status");

                query.value = "m_bat_longmenbatboss_loop";
                var deadline = Time.realtimeSinceStartup + 12;
                while (status.text == "正在搜索…" && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(status.text, Does.EndWith(" · 2.7.81"));
                Assert.That(AudioRows(library).Any(row => row.Key == "longmenbatboss"), Is.True);

                Click(Find<Button>(library, "audio-library-native"));
                Assert.That(query.value, Is.EqualTo("m_bat_longmenbatboss_loop"));
                deadline = Time.realtimeSinceStartup + 12;
                while (status.text == "正在搜索…" && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(status.text, Does.EndWith(" · 2.7.81"));
                Assert.That(AudioRows(library).Any(row => row.EventName == "battle.ON_GAME_READY.longmenbatboss"), Is.True);

                Click(Find<Button>(root, "resource-category-6"));
                Assert.That(AudioRows(library), Is.Empty);
                query.value = "地块浮起";
                deadline = Time.realtimeSinceStartup + 12;
                while (status.text == "正在搜索…" && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(status.text, Does.EndWith(" · 2.7.81"));
                Assert.That(AudioRows(library).Any(row =>
                    row.EventName == "battle.ON_ENVSYS_TRIGGER_TILE.env_038_act47side.tile_float"), Is.True);

                query.value = "";
                Assert.That(AudioRows(library).Count(), Is.EqualTo(227));
            }
            finally { UnityEngine.Object.DestroyImmediate(workspace); }
        }

        private static System.Collections.Generic.IEnumerable<MusicEventCatalog.Entry> AudioRows(VisualElement library) =>
            Find<ListView>(library, "music-library-list").itemsSource.Cast<MusicEventCatalog.Entry>();

        private static void SelectAudio(VisualElement library, Func<MusicEventCatalog.Entry, bool> test)
        {
            var list = Find<ListView>(library, "music-library-list");
            var index = Enumerable.Range(0, list.itemsSource.Count)
                .First(i => test((MusicEventCatalog.Entry)list.itemsSource[i]!));
            var row = list.makeItem();
            list.bindItem(row, index);
            Click(row.Query<Button>().ToList().First());
        }
    }
}
