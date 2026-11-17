#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Arknights.MapEditor.Catalog;
using Arknights.MapEditor.Core;
using Arknights.MapEditor.Editor;
using Arknights.MapEditor.Storage;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Tests
{
    public sealed class EnemyLibraryWindowTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private MapEditorWindow window = null!;
        private sealed class Dialogs : IEditorFileDialogs
        {
            public bool Allow { get; set; }
            public int Confirmations { get; private set; }
            public string Open(string title, string directory, string extension) => "";
            public string Save(string title, string directory, string name, string extension) => "";
            public bool Discard(string title, string message) { Confirmations++; return Allow; }
        }
        private DocumentSession Session => (DocumentSession)typeof(MapEditorWindow).GetField("session", Private)!.GetValue(window);
        private object? Call(string name, params object[] args) => typeof(MapEditorWindow).GetMethod(name, Private)!.Invoke(window, args);
        private TextField Raw => window.rootVisualElement.Q<TextField>("enemy-reference-json");
        private void Click(string name) => typeof(Clickable).GetMethod("Invoke", Private)!.Invoke(window.rootVisualElement.Q<Button>(name).clickable, new object?[] { null });
        private bool Select(JObject record) => ((Func<JObject, bool>)typeof(MapEditorWindow).GetField("prepareEnemyReference", Private)!.GetValue(window))(record);

        [Test]
        public void RemovedIdenticalDefinitionDoesNotTransferDraftOntoSurvivor()
        {
            var record = Independent("same");
            var original = Session.Document.WithSection("enemyDbRefs", new JArray(record, record.DeepClone()).ToString());
            Session.Load(original.ToJson(), "", "");
            typeof(MapEditorWindow).GetField("draft", Private)!.SetValue(window, Session.Json);
            window.CreateGUI();
            Click("enemy-reference-0");
            var draftRecord = (JObject)record.DeepClone();
            draftRecord["localExtension"] = "belongs-to-first";
            Raw.value = draftRecord.ToString();
            Call("CommitEdit", original.WithSection("enemyDbRefs", new JArray(record.DeepClone()).ToString()), "remove original", null);
            Click("enemy-apply");
            Assert.That(Session.Document.GetEnemyReferences()[0]!["localExtension"], Is.Null);
            Assert.That(Raw.value, Does.Contain("belongs-to-first"));
        }
        private static JObject Independent(string id) => JObject.Parse("{\"useDb\":false,\"id\":\"" + id + "\",\"level\":0,\"overwrittenData\":{\"prefabKey\":{\"m_defined\":true,\"m_value\":\"enemy_1001_slime\"},\"attributes\":{\"maxHp\":{\"m_defined\":true,\"m_value\":100}}}}");
        [UnitySetUp] public IEnumerator SetUp()
        {
            window = ScriptableObject.CreateInstance<MapEditorWindow>(); window.Dialogs = new Dialogs(); window.Show();
            yield return null;
            Session.UseLocalHistory(); Session.Load(LevelDocument.Create(2, 2).ToJson(), "", "");
            typeof(MapEditorWindow).GetField("draft", Private)!.SetValue(window, Session.Json);
            Call("ClearInspectorDrafts"); window.CreateGUI();
        }
        [TearDown] public void TearDown()
        {
            if (window == null) return;
            Call("ClearInspectorDrafts"); window.DiscardChanges(); window.Close();
        }
        [Test] public void SelectionCreatesOnlyLocalDraftAndApplyPreservesMapWithUndo()
        {
            var original = Session.Json;
            Assert.That(Select(Independent("test_enemy")), Is.True);
            Assert.That(Session.Json, Is.EqualTo(original));
            Assert.That(Raw.value, Does.Contain("test_enemy"));
            Click("enemy-apply");
            Assert.That(Session.Document.GetEnemyReferences().Count, Is.EqualTo(1));
            Assert.That((string?)Session.Document.GetEnemyReferences()[0]["id"], Is.EqualTo("test_enemy"));
            Session.StepHistory(false); Assert.That(Session.Json, Is.EqualTo(original));
        }
        [Test] public void DirtyDraftSurvivesRebuildAndRefusesUnconfirmedReplacement()
        {
            Select(Independent("first")); window.CreateGUI();
            Assert.That(Raw.value, Does.Contain("first"));
            Assert.That(Select(Independent("second")), Is.False);
            Assert.That(Raw.value, Does.Contain("first"));
            Assert.That(Session.Document.GetEnemyReferences().Count, Is.Zero);
        }
        [Test] public void MalformedAndStaleDraftsCannotModifyDocument()
        {
            Raw.value = "{unfinished"; window.CreateGUI(); var original = Session.Json;
            Click("enemy-apply"); Assert.That(Session.Json, Is.EqualTo(original)); Assert.That(Raw.value, Is.EqualTo("{unfinished"));
            Call("ClearInspectorDrafts"); window.CreateGUI(); Select(Independent("first"));
            Session.Edit(Session.Document.WithLevelId("another"), "new revision");
            typeof(MapEditorWindow).GetField("draft", Private)!.SetValue(window, Session.Json);
            Click("enemy-apply"); Assert.That(Session.Document.GetEnemyReferences().Count, Is.Zero);
        }
        [Test] public void EditLockPreventsApply()
        {
            Select(Independent("locked")); window.SetWorkspaceEditLock(true); Click("enemy-apply");
            Assert.That(Session.Document.GetEnemyReferences().Count, Is.Zero);
        }

        [Test]
        public void BothDefinitionColumnsKeepTheirOwnDraftsAndCommitToTheCorrectArray()
        {
            var root = window.rootVisualElement;
            const string inline = "{\"key\":\"enemy_1299_ymkilr\",\"alias\":\"inline_a\",\"name\":\"示例\",\"skills\":[{\"prefabKey\":\"InvisibleCombat\",\"priority\":4,\"cooldown\":10,\"initCooldown\":3,\"spCost\":2,\"blackboard\":[{\"key\":\"atk_scale\",\"value\":2.2500,\"valueStr\":null}]}],\"spData\":null,\"extension\":42}";
            var original = Session.Document.PutEnemyReference(-1, Independent("existing").ToString()).PutInlineEnemy(-1, inline);
            Session.Load(original.ToJson(), "", "");
            typeof(MapEditorWindow).GetField("draft", Private)!.SetValue(window, Session.Json);
            Call("ClearInspectorDrafts");
            window.CreateGUI();
            var overview = root.Q("enemy-defined-records");
            Assert.That(overview.Q("enemy-reference-cards").Q<Button>("enemy-reference-0"), Is.Not.Null);
            Assert.That(overview.Q("enemy-inline-cards").Q<Button>("enemy-inline-0"), Is.Not.Null);
            Select(Independent("new_ref"));
            Click("enemy-reference-0");
            var updated = JObject.Parse(Raw.value); updated["localExtension"] = true; Raw.value = updated.ToString();
            Click("enemy-inline-0");
            var skills = root.Q<Foldout>("enemy-skills");
            skills.value = true;
            skills.Q<FloatField>("enemy-field-cooldown").value = 5f;
            Click("enemy-reference-0");
            Assert.That(Raw.value, Does.Contain("localExtension"));
            Click("enemy-apply");
            Click("enemy-inline-0");
            Assert.That(JObject.Parse(Raw.value)["skills"]![0]!["cooldown"]!.Value<float>(), Is.EqualTo(5f));
            Click("enemy-apply");
            var edited = Session.Document.GetInlineEnemies()[0]!;
            Assert.That(edited["skills"]![0]!["cooldown"]!.Value<float>(), Is.EqualTo(5f));
            Assert.That(edited["skills"]![0]!["priority"]!.Value<int>(), Is.EqualTo(4));
            Assert.That(edited["skills"]![0]!["blackboard"]![0]!["valueStr"]!.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(Session.Document.ToJson(false), Does.Contain("\"value\":2.2500"));
            Assert.That(edited["spData"]!.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(edited["extension"]!.Value<int>(), Is.EqualTo(42));
            Assert.That(Session.Document.GetEnemyReferences()[0]!["localExtension"]!.Value<bool>(), Is.True);
            Click("enemy-new-reference");
            Assert.That(Raw.value, Does.Contain("new_ref"));
            Assert.That(((Dialogs)window.Dialogs).Confirmations, Is.Zero);
            Session.StepHistory(false);
            Assert.That(Session.Document.GetInlineEnemies()[0]!["skills"]![0]!["cooldown"]!.Value<int>(), Is.EqualTo(10));
        }

        [Test]
        public void InlineRenameRequiresConfirmationAndUpdatesBothActionTrees()
        {
            var doc = Session.Document.PutInlineEnemy(-1, "{\"key\":\"prefab\",\"alias\":\"first\"}")
                .WithSection("waves", "[{\"fragments\":[{\"actions\":[{\"actionType\":0,\"key\":\"first\"}]}]}]")
                .WithSection("branches", "{\"b\":{\"phases\":[{\"actions\":[{\"actionType\":5,\"key\":\"first\"}]}]}}");
            Session.Load(doc.ToJson(), "", "");
            typeof(MapEditorWindow).GetField("draft", Private)!.SetValue(window, Session.Json);
            Call("ClearInspectorDrafts"); window.CreateGUI();
            Click("enemy-inline-0");
            window.rootVisualElement.Q<TextField>("enemy-field-alias").value = "second";
            Click("enemy-apply");
            Assert.That(Session.Document.GetEnemyUsages("first").Count, Is.EqualTo(2));
            ((Dialogs)window.Dialogs).Allow = true;
            Click("enemy-apply");
            Assert.That(Session.Document.GetEnemyUsages("second").Count, Is.EqualTo(2));
            Assert.That(Session.Document.GetInlineEnemies()[0]!["alias"]!.Value<string>(), Is.EqualTo("second"));
            Session.StepHistory(false);
            Assert.That(Session.Document.GetEnemyUsages("first").Count, Is.EqualTo(2));
        }

        [Test]
        public void SkillAndBlackboardFormsKeepNativeObjectsAndMissingStates()
        {
            var root = window.rootVisualElement;
            Click("enemy-new-inline");
            root.Q<TextField>("enemy-field-key").value = "enemy_1299_ymkilr";
            var skills = root.Q<Foldout>("enemy-skills"); skills.value = true;
            ClickText(skills, "创建技能列表");
            skills = root.Q<Foldout>("enemy-skills");
            Assert.That(skills.value, Is.True);
            ClickText(skills, "新增技能");
            root.Q<TextField>("enemy-field-prefabKey").value = "InvisibleCombat";
            var skill = root.Q<Foldout>("enemy-skill-0");
            skill.Q<FloatField>("enemy-field-cooldown").value = 12f;
            var board = root.Q<Foldout>("enemy-skill-blackboard-0"); board.value = true;
            ClickText(board, "创建黑板列表");
            board = root.Q<Foldout>("enemy-skill-blackboard-0");
            ClickText(board, "新增字符串");
            board = root.Q<Foldout>("enemy-skill-blackboard-0");
            board.Q<TextField>("enemy-field-key").value = "spawn_target";
            board = root.Q<Foldout>("enemy-skill-blackboard-0");
            board.Q<TextField>("enemy-field-valueStr").value = "enemy_custom";
            var sp = root.Q<Foldout>("enemy-sp"); sp.value = true;
            ClickText(sp, "创建技力配置");
            root.Q<IntegerField>("enemy-field-maxSp").value = 13;
            Click("enemy-apply");
            var record = Session.Document.GetInlineEnemies()[0]!;
            Assert.That(record["skills"]![0]!["cooldown"]!.Value<float>(), Is.EqualTo(12f));
            Assert.That(record["skills"]![0]!["blackboard"]![0]!["valueStr"]!.Value<string>(), Is.EqualTo("enemy_custom"));
            Assert.That(record["spData"]!["maxSp"]!.Value<int>(), Is.EqualTo(13));
            Assert.That(record["talentBlackboard"]!.Type, Is.EqualTo(JTokenType.Null));
        }

        [Test]
        public void DatabaseLevelCopiesToInlineAndLegacyDraftSurvives()
        {
            var store = (InspectorDraftStore)typeof(MapEditorWindow).GetProperty("InspectorStore", Private)!.GetValue(window);
            store.SetDraft("enemy", Session.Json, "-1", "{\"id\":\"\"}", Independent("legacy_ref").ToString());
            window.CreateGUI();
            Assert.That(Raw.value, Does.Contain("legacy_ref"));
            var root = window.rootVisualElement;
            root.Q<EnemyDetail>().Open(EnemyCatalog.LoadBundled().Find("enemy_1007_slime")!, new Button());
            Click("enemy-copy-db-inline-0");
            Assert.That(Session.Document.GetInlineEnemies(), Is.Empty);
            Click("enemy-apply");
            var record = Session.Document.GetInlineEnemies()[0]!;
            Assert.That(record["key"]!.Value<string>(), Is.EqualTo("enemy_1007_slime"));
            Assert.That(record["attributes"]!["maxHp"]!.Value<int>(), Is.GreaterThan(0));
            Assert.That(record["useDb"], Is.Null);
            Click("enemy-new-reference");
            Assert.That(Raw.value, Does.Contain("legacy_ref"));
            Assert.That(((Dialogs)window.Dialogs).Confirmations, Is.Zero);
        }

        private static void ClickText(VisualElement parent, string text)
        {
            var button = parent.Query<Button>().ToList().Single(item => item.text == text);
            typeof(Clickable).GetMethod("Invoke", Private)!.Invoke(button.clickable, new object?[] { null });
        }

        [Test]
        public void EveryEnemyBindsAnAtlasPortraitInTheRecycledListRow()
        {
            var list = window.rootVisualElement.Q<ListView>("enemy-library");
            var catalog = EnemyCatalog.LoadBundled();
            var portraits = EnemyPortraitCatalog.LoadBundled();
            Assert.That(catalog.Entries.Count, Is.EqualTo(2445));
            window.rootVisualElement.Q<PopupField<string>>("enemy-page-size").value = "500";
            var seen = new HashSet<string>();
            var next = window.rootVisualElement.Q<Button>("enemy-page-next");
            var row = list.makeItem();
            while (true)
            {
                for (var i = 0; i < list.itemsSource.Count; i++)
                {
                    var id = ((EnemyCatalog.Entry)list.itemsSource[i]!).Id;
                    Assert.That(seen.Add(id), Is.True, id);
                    var expected = portraits.Find(id);
                    Assert.That(expected, Is.Not.Null, id);
                    Assert.That(expected!.SourceLabel, Is.Not.Empty, id);
                    list.bindItem(row, i);
                    var image = row.Q<Image>("enemy-library-avatar");
                    Assert.That(image.image, Is.Not.Null, id);
                    Assert.That(image.image, Is.SameAs(portraits.Texture(expected)), id);
                    Assert.That(image.uv, Is.EqualTo(expected.Uv), id);
                    Assert.That(image.uv.xMin >= 0 && image.uv.yMin >= 0 && image.uv.xMax <= 1 && image.uv.yMax <= 1, Is.True, id);
                }
                if (!next.enabledSelf) break;
                Click(next.name);
            }
            Assert.That(seen, Is.EquivalentTo(catalog.Entries.Select(entry => entry.Id)));
        }

        [Test]
        public void TagPanelFiltersTheFullCatalogWithoutSourceControls()
        {
            var root = window.rootVisualElement;
            Assert.That(root.Q("enemy-library-search").parent.IndexOf(root.Q("enemy-tag-filter-toggle")), Is.EqualTo(1));
            Click("enemy-tag-filter-toggle");
            Assert.That(root.Q("enemy-tag-filter-panel").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(root.Q<Foldout>("enemy-tag-section-basic").value, Is.True);
            Assert.That(root.Q<Foldout>("enemy-tag-section-ratings").value, Is.False);
            Assert.That(root.Q<Foldout>("enemy-tag-section-more").value, Is.False);
            foreach (var group in EnemyTagFilter.Basic.Concat(EnemyTagFilter.Ratings).Concat(EnemyTagFilter.More))
            {
                Assert.That(root.Q<Button>("enemy-tag-all-" + group.Key), Is.Null, group.Key);
                Assert.That(root.Q<Button>("enemy-tag-clear-" + group.Key), Is.Null, group.Key);
                foreach (var option in group.Options)
                    Assert.That(root.Q<Button>($"enemy-tag-{group.Key}-{option}"), Is.Not.Null);
            }
            Assert.That(root.Q("enemy-source-filter"), Is.Null);
            var count = root.Q<Label>("enemy-library-count");
            Assert.That(count.text, Is.EqualTo("2445 项敌人"));
            foreach (var option in EnemyTagFilter.Basic.Single(group => group.Key == "attack").Options)
                Click("enemy-tag-attack-" + option);
            Assert.That(count.text, Is.EqualTo("2375 项敌人"));
            Click("enemy-tag-clear-all");
            Assert.That(count.text, Is.EqualTo("2445 项敌人"));
            Click("enemy-tag-move-E");
            Click("enemy-tag-rank-领袖");
            Assert.That(count.text, Is.EqualTo("25 项敌人"));
            Click("enemy-tag-rank-领袖");
            Assert.That(root.Q<Button>("enemy-tag-rank-领袖").ClassListContains("selected-tab"), Is.False);
            Click("enemy-tag-clear-all");
            Assert.That(count.text, Is.EqualTo("2445 项敌人"));
        }

        [Test]
        public void ListAndAvatarsSharePageAndDefaultOrderWithoutSortingControls()
        {
            var root = window.rootVisualElement;
            var list = root.Q<ListView>("enemy-library");
            var first = (EnemyCatalog.Entry)list.itemsSource[0]!;
            Assert.That(first.Id, Is.EqualTo("enemy_1007_slime"));
            Assert.That(root.Q("enemy-sort-field"), Is.Null);
            Assert.That(root.Q("enemy-sort-direction"), Is.Null);
            Click("enemy-page-next");
            first = (EnemyCatalog.Entry)list.itemsSource[0]!;
            var status = root.Q<Label>("enemy-page-status");
            Assert.That(status.text, Does.StartWith("2 / "));
            Click("enemy-view-avatars");
            var gallery = root.Q<ListView>("enemy-avatar-grid");
            Assert.That(gallery.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(list.style.display.value, Is.EqualTo(DisplayStyle.None));
            var row = gallery.makeItem();
            gallery.bindItem(row, 0);
            var card = row.Q("enemy-avatar-" + first.Id);
            Assert.That(card.Q<Image>("enemy-grid-portrait").image, Is.Not.Null);
            Assert.That(card.Q<Button>("enemy-grid-info"), Is.Not.Null);
            Click("enemy-view-table");
            Assert.That(status.text, Does.StartWith("2 / "));
            Assert.That(list.itemsSource[0], Is.SameAs(first));
        }

        [Test]
        public void BothResultViewsShowEnemyAttributesThroughTheDetailsButton()
        {
            var root = window.rootVisualElement;
            var list = root.Q<ListView>("enemy-library");
            var entry = (EnemyCatalog.Entry)list.itemsSource[0]!;
            Assert.That(entry.Id, Is.EqualTo("enemy_1007_slime"));
            Assert.That(root.Q("enemy-column-attack"), Is.Null);
            Assert.That(root.Q("enemy-column-hp"), Is.Null);

            var row = list.makeItem();
            list.bindItem(row, 0);
            var detail = root.Q<EnemyDetail>();
            Assert.That(detail.style.display.value, Is.EqualTo(DisplayStyle.None));
            var invoke = typeof(Clickable).GetMethod("Invoke", Private)!;
            invoke.Invoke(row.Q<Button>("enemy-info-" + entry.Id).clickable, new object?[] { null });
            Assert.That(detail.Q<Label>("enemy-detail-race").text, Is.EqualTo("感染生物"));
            Assert.That(detail.Q<Label>("enemy-detail-attack").text, Is.EqualTo("近战"));
            Assert.That(detail.Q<Label>("enemy-detail-hp").text, Is.EqualTo("E"));
            Assert.That(detail.Q<Label>("enemy-detail-motion").text, Is.EqualTo("地面"));
            Assert.That(detail.Q<Label>("enemy-detail-element").text, Is.EqualTo("E"));
            Assert.That(detail.Q<Label>("enemy-detail-damageRes").text, Is.EqualTo("E"));
            Click("enemy-detail-close");

            Click("enemy-view-avatars");
            var gallery = root.Q<ListView>("enemy-avatar-grid");
            var card = gallery.makeItem();
            gallery.bindItem(card, 0);
            invoke.Invoke(card.Q<Button>("enemy-grid-info").clickable, new object?[] { null });
            Assert.That(detail.Q<Label>("enemy-detail-race").text, Is.EqualTo("感染生物"));
        }

        [UnityTest]
        public IEnumerator EnemyDetailKeepsAttributeColumnsAlignedAndShowsReadableDescription()
        {
            window.position = new Rect(40, 40, 960, 800);
            var detail = window.rootVisualElement.Q<EnemyDetail>();
            var catalog = EnemyCatalog.LoadBundled();
            var caster = catalog.Find("enemy_1022_dmage_2")!;
            detail.Open(caster, new Button());
            window.Repaint();
            yield return null;
            yield return null;

            Label Value(string key) => detail.Q<Label>("enemy-detail-" + key);
            var rank = Value("rank");
            var race = Value("race");
            Assert.That(race.worldBound.xMin, Is.GreaterThan(rank.worldBound.xMin + 100));
            Assert.That(race.worldBound.yMin, Is.EqualTo(rank.worldBound.yMin).Within(1));
            Assert.That(Value("motion").worldBound.xMin, Is.EqualTo(rank.worldBound.xMin).Within(1));
            Assert.That(Value("damageRes").worldBound.xMin, Is.EqualTo(race.worldBound.xMin).Within(1));
            Assert.That(Value("description").text,
                Is.EqualTo("拥有较高攻击力和法术抗性。会使用远距离法术攻击，且能够使用禁锢我方单位的枷锁。"));
            Assert.That(caster.Description, Does.Contain("<@eb.key>"));

            detail.Open(catalog.Find("enemy_10083_hlbird")!, new Button());
            Assert.That(Value("description").text, Does.EndWith("陷入恐惧"));
            detail.Open(catalog.Find("enemy_10050_cjmeat")!, new Button());
            Assert.That(Value("description").text, Does.Contain("<饮啄>"));
            Assert.That(Value("description").enableRichText, Is.False);
        }

        [Test]
        public void OriginalSearchMatchesHandbookNumberDescriptionAbilityAndTerm()
        {
            var catalog = EnemyCatalog.LoadBundled();
            var search = catalog.CreateSearch();
            foreach (var (query, id) in new[]
            {
                ("B1", "enemy_1007_slime"),
                ("野生的被感染生物", "enemy_1007_slime"),
                ("每次攻击会降低击中目标的防御", "enemy_1004_mslime"),
                ("不阻挡时不成为敌方攻击的目标", "enemy_10031_cnvsld")
            })
            {
                var matches = search.Filter(catalog.Entries, query, out var error).Select(item => item.Id).ToArray();
                Assert.That(error, Is.Null, query);
                Assert.That(matches, Does.Contain(id), query);
                Assert.That(matches.Length, Is.LessThan(catalog.Entries.Count), query);
            }
        }

        [Test]
        public void DetailFollowsHistoricalPrefabAndNamesTheActualImageSource()
        {
            var overlay = window.rootVisualElement.Q<EnemyDetail>();
            var catalog = EnemyCatalog.LoadBundled();
            var entry = catalog.Find("enemy_10045_parrot_3")!;
            overlay.Open(entry, new Button());
            var references = (JArray)catalog.Details(entry)["references"]!;
            var variant = references.Select((sample, index) => (sample, index)).First(pair =>
                (string?)pair.sample["record"]?["overwrittenData"]?["prefabKey"]?["m_value"] == "enemy_10045_parrot_2");
            var oldUv = overlay.Q<Image>("library-detail-portrait").uv;
            Click("enemy-config-" + variant.index);
            Assert.That(overlay.Q<Label>("library-portrait-source").text, Is.EqualTo("关联预制体的原版头像"));
            Assert.That(overlay.Q<Label>("library-portrait-asset").text, Does.Contain("enemy_10045_parrot_2.png"));
            Assert.That(overlay.Q<Image>("library-detail-portrait").uv, Is.Not.EqualTo(oldUv));

            overlay.Open(catalog.Find("enemy_2132_dyfake")!, new Button());
            Assert.That(overlay.Q<Label>("library-portrait-source").text, Is.EqualTo("原版特效代表帧"));
            Assert.That(overlay.Q<Image>("library-detail-portrait").ClassListContains("library-portrait-light"), Is.True);
            overlay.Open(catalog.Find("enemy_1554_lrtsia_2")!, new Button());
            Assert.That(overlay.Q<Label>("library-portrait-source").text, Is.EqualTo("召唤者的原版头像"));
        }

        [Test]
        public void EnemyOverrideEditorsPreserveUnusualNumbersAndDateStrings()
        {
            var host = new VisualElement();
            window.rootVisualElement.Add(host);
            var patch = JObject.Parse("{\"name\":{\"m_defined\":true,\"m_value\":\"before\"},\"future\":{}}");
            var changes = 0;
            EnemyOverrideForm.Build(host, patch, () => changes++);
            const string timestamp = "2025-01-02T03:04:05.123Z";
            const string fragment = "{\"amount\":0.12345678901234567890123456,\"exponent\":1.2300e-4,\"stamp\":\"" + timestamp + "\"}";
            var fields = host.Query<TextField>().ToList();
            var wrapped = fields.Single(field => field.label == "m_value · JSON");
            var raw = fields.Single(field => field.label == "JSON");

            wrapped.value = "\"" + timestamp + "\"";
            raw.value = fragment;
            raw.value = "{\"duplicate\":1,\"duplicate\":2}";

            Assert.That(changes, Is.EqualTo(2));
            Assert.That(patch["name"]!["m_value"]!.Type, Is.EqualTo(JTokenType.String));
            Assert.That(patch["name"]!["m_value"]!.Value<string>(), Is.EqualTo(timestamp));
            Assert.That(patch["future"]!.ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo(fragment));
        }

        [UnityTest]
        public IEnumerator EnemySourceRowsShowDistinctStagesAndModesWithoutRepositoryProvenance()
        {
            var catalog = EnemyCatalog.LoadBundled();
            var detail = window.rootVisualElement.Q<EnemyDetail>();
            detail.Open(catalog.Find("enemy_1007_slime_2")!, new Button());
            Click("enemy-config-23");
            var sources = detail.Query<TerrainSourceBrowser>().ToList();
            Assert.That(sources.Select(source => source.text), Is.EquivalentTo(new[] { "当前配置出处 · 2", "总出处 · 130" }));
            foreach (var source in sources) source.value = true;
            window.Repaint();
            yield return null;
            yield return null;

            foreach (var source in sources)
            {
                var list = source.Q<ListView>("terrain-source-list");
                var first = (Label)list.GetRootElementForIndex(0);
                var labels = list.itemsSource.Cast<string>().ToArray();
                Assert.That(labels.Distinct().Count(), Is.EqualTo(labels.Length));
                Assert.That(labels.All(label => !label.Contains("ArknightsGameData") && !label.Contains("PRTSMap-cache") &&
                    !label.Contains(".json") && !label.Contains("\n")), Is.True);
                Assert.That(first.text, Does.Contain(" · "));
                var measured = first.MeasureTextSize(first.text, first.contentRect.width,
                    VisualElement.MeasureMode.AtMost, 0, VisualElement.MeasureMode.Undefined);
                Assert.That(first.contentRect.height, Is.GreaterThanOrEqualTo(measured.y - 1));
                if (list.itemsSource.Count > 1)
                {
                    var second = list.GetRootElementForIndex(1);
                    Assert.That(second.worldBound.yMin, Is.GreaterThanOrEqualTo(first.worldBound.yMax - 1));
                }
            }
        }
    }
}
