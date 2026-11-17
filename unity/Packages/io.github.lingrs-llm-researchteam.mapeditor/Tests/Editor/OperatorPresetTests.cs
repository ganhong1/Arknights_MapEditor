#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Arknights.MapEditor.Catalog;
using Arknights.MapEditor.Core;
using Arknights.MapEditor.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Tests
{
    public sealed class OperatorPresetTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private const string Character = @"{""charId"":""char_220_grani"",""instId"":10,
            ""level"":80,""evolvePhase"":2,""potentialRank"":4,""favorPoint"":25570,""mainSkillLvl"":7,
            ""defaultSkillIndex"":0,""skin"":""char_220_grani#2"",""currentEquip"":null,
            ""skills"":[{""skillId"":""skcom_def_up[3]"",""specializeLevel"":0,""unlock"":1},
                {""skillId"":""skchr_grani_2"",""specializeLevel"":3,""unlock"":1}],
            ""equip"":{""uniequip_002_grani"":{""locked"":0,""level"":2}}}";
        private const string Slot = @"{""charInstId"":10,""charId"":""char_220_grani"",
            ""skillIndex"":1,""currentEquip"":""uniequip_002_grani""}";

        private static string PlayerSnapshot(string character = Character) =>
            "{\"troop\":{\"chars\":{\"10\":" + character + "},\"squads\":{\"0\":{\"name\":\"队伍\",\"slots\":[" + Slot + ",null]}}}}";

        [Test]
        public void LibraryLoadsPinnedFormsAndOriginalSampleProvenance()
        {
            var catalog = OperatorPresetCatalog.LoadBundled();
            Assert.That(catalog.Entries.Count, Is.EqualTo(461));
            Assert.That(catalog.Entries.Sum(entry => entry.Instances), Is.EqualTo(1765));
            Assert.That(catalog.Entries.Sum(entry => entry.Cards), Is.EqualTo(2938));
            var amiya = catalog.Find("char_1001_amiya2")!;
            Assert.That(amiya.Id, Is.EqualTo("char_002_amiya"));
            Assert.That(amiya.Forms.Count, Is.EqualTo(3));
            var grani = catalog.Find("char_220_grani")!;
            Assert.That(grani.Rarity, Is.EqualTo(5));
            var samples = (JArray)catalog.Details(grani)["characterInsts"]!;
            var origins = samples.SelectMany(sample => (JArray)sample["origins"]!).OfType<JObject>();
            Assert.That(samples.All(sample => sample["record"]!["position"] == null && sample["record"]!["direction"] == null));
            Assert.That(origins.SelectMany(catalog.SourceLabels).Any(label => label.Contains("GT-1")));
        }

        [Test]
        public void OperatorTagsMatchOneFormAndCanRequireAllTraits()
        {
            var catalog = OperatorPresetCatalog.LoadBundled();
            var tags = new OperatorTagFilter();
            var amiya = catalog.Find("char_002_amiya")!;
            tags.Toggle(OperatorTagFilter.Group.Profession, "WARRIOR");
            tags.Toggle(OperatorTagFilter.Group.Position, "RANGED");
            Assert.That(tags.Match(amiya), Is.Null);
            tags.Toggle(OperatorTagFilter.Group.Position, "MELEE");
            tags.Toggle(OperatorTagFilter.Group.Branch, "术战者");
            Assert.That(tags.Match(amiya)?.Profession, Is.EqualTo("WARRIOR"));

            tags.Clear();
            var grani = catalog.Find("char_220_grani")!;
            tags.Toggle(OperatorTagFilter.Group.Rarity, "6");
            Assert.That(tags.Match(grani), Is.Null);
            tags.Toggle(OperatorTagFilter.Group.Rarity, "5");
            tags.Toggle(OperatorTagFilter.Group.Trait, "费用回复");
            tags.Toggle(OperatorTagFilter.Group.Trait, "治疗");
            Assert.That(tags.Match(grani), Is.Not.Null);
            tags.MatchAllTraits = true;
            Assert.That(tags.Match(grani), Is.Null);
            tags.Toggle(OperatorTagFilter.Group.Trait, "治疗");
            Assert.That(tags.Match(grani), Is.Not.Null);
        }

        [Test]
        public void OperatorTagFilterOpensBesideSearchAndRetainsSelectionWhileClosed()
        {
            var window = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                window.CreateGUI();
                var root = window.rootVisualElement;
                var search = root.Q<TextField>("operator-library-search");
                var toggle = root.Q<Button>("operator-tag-filter-toggle");
                var panel = root.Q<VisualElement>("operator-tag-filter-panel");
                var results = root.Q<ListView>("operator-library");
                Assert.That(toggle.parent, Is.SameAs(search.parent));
                Assert.That(toggle.parent.IndexOf(toggle), Is.EqualTo(search.parent.IndexOf(search) + 1));
                Assert.That(panel.style.display.value, Is.EqualTo(DisplayStyle.None));
                var originalCount = results.itemsSource.Count;
                Click(toggle);
                Assert.That(panel.style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Click(panel.Q<Button>("operator-tag-Profession-PIONEER"));
                Assert.That(results.itemsSource.Count, Is.InRange(1, originalCount - 1));
                Assert.That(results.itemsSource.Cast<OperatorPresetCatalog.Entry>()
                    .All(entry => entry.FilterForms.Any(form => form.Profession == "PIONEER")), Is.True);
                Click(toggle);
                Assert.That(toggle.text, Is.EqualTo("标签筛选 · 1"));
                Assert.That(panel.style.display.value, Is.EqualTo(DisplayStyle.None));
                Click(toggle);
                Assert.That(panel.Q<Button>("operator-tag-Profession-PIONEER").ClassListContains("selected-tab"), Is.True);
                Click(panel.Q<Button>("operator-tag-clear"));
                Assert.That(results.itemsSource.Count, Is.EqualTo(originalCount));
            }
            finally
            {
                window.DiscardChanges();
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void CatalogHasAnExactAvatarForEveryOperatorAndDetailsWithoutSamples()
        {
            var catalog = OperatorPresetCatalog.LoadBundled();
            var regions = new System.Collections.Generic.HashSet<(string, Rect)>();
            foreach (var entry in catalog.Entries)
            {
                Assert.That(PrtsObjectView.TryGetOperatorAvatar(entry.Id, out var texture, out var rect), Is.True, entry.Id);
                Assert.That(rect.xMin >= 0 && rect.yMin >= 0 && rect.xMax <= 1 && rect.yMax <= 1, Is.True, entry.Id);
                Assert.That(regions.Add((texture!.name, rect)), Is.True, entry.Id);
            }
            var noSamples = catalog.Entries.First(entry => entry.Instances == 0 && entry.Cards == 0);
            var overlay = new OperatorPresetDetail(catalog, (_, _) => false);
            overlay.Open(noSamples, new Button());
            var profile = overlay.Q<Foldout>("operator-profile");
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.Q<Foldout>("operator-profile-skills"), Is.Not.Null);
            Assert.That(profile.Query<Label>().ToList().Any(label => label.text.Contains("等级上限")), Is.True);
        }

        [UnityTest]
        public IEnumerator OriginalSampleParametersFollowTheSelectedCard()
        {
            var window = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                window.Show();
                yield return null;
                window.CreateGUI();
                var catalog = OperatorPresetCatalog.LoadBundled();
                var grani = catalog.Find("char_220_grani")!;
                var overlay = window.rootVisualElement.Q<OperatorPresetDetail>();
                overlay.Open(grani, new Button());
                Assert.That(overlay.Q("operator-sample-param-inst.level").tooltip, Is.EqualTo("inst.level = 40"));
                Click(overlay.Q<Button>("operator-role-card"));
                var selection = overlay.Q<PopupField<string>>("operator-sample");
                var raw = overlay.Q<TextField>("operator-sample-json");
                var cards = (JArray)catalog.Details(grani)["characterCards"]!;
                Assert.That(overlay.Q("operator-sample-param-inst.level").tooltip, Is.EqualTo("inst.level = 50"));
                Assert.That(overlay.Q("operator-sample-param-uniEquipIds.0.level"), Is.Not.Null);
                Assert.That(overlay.Q("operator-sample-param-skillIndex"), Is.Not.Null);
                Assert.That(overlay.Q("operator-sample-param-overrideSkillBlackboard"), Is.Not.Null);
                selection.index = 1;
                Assert.That(overlay.Q("operator-sample-param-inst.level").tooltip, Is.EqualTo("inst.level = 1"));
                Assert.That(overlay.Q("operator-sample-param-uniEquipIds.0.level"), Is.Null);
                Assert.That(JToken.DeepEquals(JObject.Parse(raw.value), cards[1]!["record"]), Is.True);
            }
            finally
            {
                window.DiscardChanges();
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void ResolvesOwnedSlotAndBattleLabSnapshotToTheSamePreset()
        {
            var catalog = OperatorPresetCatalog.LoadBundled();
            var fromPlayer = SquadPresetImport.Read(PlayerSnapshot()).Single().ToPreset(catalog);
            var fromBattleLab = SquadPresetImport.Read("{\"clientVersion\":\"2.7.81\",\"roster\":[\"char_220_grani\"],\"patches\":[" +
                Character + "],\"squadSelections\":[" + Slot + "]}").Single().ToPreset(catalog);
            Assert.That(JToken.DeepEquals(fromPlayer, fromBattleLab));
            Assert.That((int?)fromPlayer["skillIndex"], Is.EqualTo(1));
            Assert.That((int?)fromPlayer["mainSkillLvl"], Is.EqualTo(10));
            Assert.That((int?)fromPlayer["uniEquipIds"]?[0]?["level"], Is.EqualTo(2));
            Assert.That((int?)fromPlayer["inst"]?["favorPoint"], Is.EqualTo(100));
            Assert.That(fromPlayer["inst"]?["playerInstId"], Is.Null);
            Assert.That(catalog.FavorPhase(15), Is.EqualTo(0));
            Assert.That(catalog.FavorPhase(16), Is.EqualTo(1));
        }

        [Test]
        public void IncompleteModuleSelectionRequiresItsOwnedLevel()
        {
            var character = JObject.Parse(Character);
            character.Remove("equip");
            var selection = SquadPresetImport.Read(PlayerSnapshot(character.ToString())).Single();
            Assert.That(() => selection.ToPreset(OperatorPresetCatalog.LoadBundled()),
                Throws.TypeOf<FormatException>().With.Message.Contains("模组的等级"));
        }

        [Test]
        public void PresetCommandsWriteOnlyTheChosenArrayAndRetainOtherRecords()
        {
            var record = SquadPresetImport.Read(PlayerSnapshot()).Single().ToPreset(OperatorPresetCatalog.LoadBundled()).ToString();
            var original = LevelDocument.Create(3, 2).WithSection("predefines",
                "{\"tokenInsts\":[{\"inst\":{\"characterKey\":\"trap_001_crate\"}}],\"future\":1}");
            var placed = original.PlacePredefined("hardPredefines", "characterInsts", record, "LEFT", new[] { (1, 2) });
            var value = placed.GetSection("hardPredefines")!["characterInsts"]![0]!;
            Assert.That((int?)value["position"]?["row"], Is.EqualTo(1));
            Assert.That((int?)value["position"]?["col"], Is.EqualTo(2));
            Assert.That((string?)value["direction"], Is.EqualTo("LEFT"));
            Assert.That(JToken.DeepEquals(placed.GetSection("predefines"), original.GetSection("predefines")));
            var cards = placed.SavePredefined("hardPredefines", "characterCards", 0, record, false);
            Assert.That(cards.GetSection("hardPredefines")!["characterCards"]![0]!["position"], Is.Null);
            Assert.That(original.GetSection("hardPredefines"), Is.Null);
        }

        [Test]
        public void SquadCopyStagesAndCommitsASelectedCardInHardPresets()
        {
            var window = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                window.CreateGUI();
                var root = window.rootVisualElement;
                Assert.That(root.Q<Button>("workbench-tab-5").text, Is.EqualTo("关卡预设"));
                Assert.That(root.Q<Button>("resource-category-4").text, Is.EqualTo("预设干员"));
                root.Q<PopupField<string>>("device-edit-section").value = "hardPredefines";
                var choose = (Func<string, JObject, bool>)typeof(MapEditorWindow).GetField("prepareOperatorPreset", Private)!.GetValue(window);
                var session = (DocumentSession)typeof(MapEditorWindow).GetField("session", Private)!.GetValue(window);
                var import = new VisualElement();
                MapEditorWindow.BuildSquadImport(import, PlayerSnapshot(), choose);
                var original = session.Json;
                Click(import.Q<Button>("preset-squad-card"));
                Assert.That(session.Json, Is.EqualTo(original));
                Assert.That(root.Q<PopupField<string>>("device-edit-role").value, Is.EqualTo("characterCards"));
                Click(root.Q<Button>("preset-insert"));
                Assert.That((int?)session.Document.GetSection("hardPredefines")!["characterCards"]![0]!["mainSkillLvl"],
                    Is.EqualTo(10));
                Assert.That(session.Document.GetSection("predefines"), Is.Null);
            }
            finally
            {
                typeof(MapEditorWindow).GetMethod("ClearInspectorDrafts", Private)!.Invoke(window, null);
                window.DiscardChanges();
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [UnityTest]
        public IEnumerator PresetEditorDropdownsSaveNativeSkillAndModuleValues()
        {
            var window = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                window.Show();
                yield return null;
                window.CreateGUI();
                var root = window.rootVisualElement;
                var choose = (Func<string, JObject, bool>)typeof(MapEditorWindow).GetField("prepareOperatorPreset", Private)!.GetValue(window);
                var session = (DocumentSession)typeof(MapEditorWindow).GetField("session", Private)!.GetValue(window);
                var sample = JObject.Parse(@"{""inst"":{""characterKey"":""char_2015_dusk"",""phase"":2,""level"":90},
                    ""skillIndex"":1,""mainSkillLvl"":9,""uniEquipIds"":[{""key"":""uniequip_002_dusk"",""level"":2}],
                    ""futurePresetValue"":123}");
                Assert.That(choose("characterCards", sample), Is.True);
                var skill = root.Q<PopupField<string>>("preset-skill-level");
                Assert.That(skill.choices, Is.EqualTo(new[] { "1级", "2级", "3级", "4级", "5级", "6级", "7级", "专一", "专二", "专三" }));
                Assert.That(skill.value, Is.EqualTo("专二"));
                skill.value = "专三";
                var module = root.Q<PopupField<string>>("preset-module");
                Assert.That(module.value, Is.EqualTo("一模 · 无题长卷"));
                Assert.That(module.choices, Does.Contain("二模 · “终夜无寐”"));
                module.value = "集成特限模 · 夕特限证章";
                var level = root.Q<PopupField<string>>("preset-module-level");
                Assert.That(level.choices, Is.EqualTo(new[] { "1级", "2级", "3级" }));
                Assert.That(level.value, Is.EqualTo("1级"));
                level.value = "3级";
                Assert.That(session.Document.GetSection("predefines"), Is.Null);
                Click(root.Q<Button>("preset-insert"));
                var saved = session.Document.GetSection("predefines")!["characterCards"]![0]!;
                Assert.That((int?)saved["mainSkillLvl"], Is.EqualTo(10));
                Assert.That((string?)saved["uniEquipIds"]![0]!["key"], Is.EqualTo("uniequip_004_dusk"));
                Assert.That((int?)saved["uniEquipIds"]![0]!["level"], Is.EqualTo(3));
                Assert.That((int?)saved["futurePresetValue"], Is.EqualTo(123));
            }
            finally
            {
                typeof(MapEditorWindow).GetMethod("ClearInspectorDrafts", Private)!.Invoke(window, null);
                window.DiscardChanges();
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void PresetEditorOnlyOffersLevelsDefinedForTheSelectedSkill()
        {
            var record = JObject.Parse(@"{""inst"":{""characterKey"":""char_123_fang"",""phase"":1,""level"":55},
                ""skillIndex"":0,""mainSkillLvl"":8}");
            var form = new PresetRecordForm(_ => Assert.Fail("Loading a form must not rewrite a stored value."));
            form.Bind(record, "characterCards");
            var skill = form.Q<PopupField<string>>("preset-skill-level");
            Assert.That(skill.value, Is.EqualTo("当前值 · 8"));
            Assert.That(skill.choices.Skip(1), Is.EqualTo(Enumerable.Range(1, 7).Select(level => $"{level}级")));
            Assert.That((int?)record["mainSkillLvl"], Is.EqualTo(8));
        }

        [UnityTest]
        public IEnumerator SquadCopyPlacesOneOperatorAndSelectsTheNewMapRecord()
        {
            var window = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                window.Show();
                yield return null;
                window.CreateGUI();
                var root = window.rootVisualElement;
                var choose = (Func<string, JObject, bool>)typeof(MapEditorWindow).GetField("prepareOperatorPreset", Private)!.GetValue(window);
                var session = (DocumentSession)typeof(MapEditorWindow).GetField("session", Private)!.GetValue(window);
                var import = new VisualElement();
                MapEditorWindow.BuildSquadImport(import, PlayerSnapshot(), choose);
                Click(import.Q<Button>("preset-squad-instance"));
                Assert.That(window.OperatorPlacementEnabled, Is.False);
                var terrain = session.Document.GetSection("mapData")!.ToString();
                root.Q<PopupField<string>>("device-placement-direction").value = "LEFT";
                root.Q<Toggle>("device-placement-enabled").value = true;
                Assert.That(window.OperatorPlacementEnabled, Is.True);
                window.PaintWorkspaceCells(new[] { new Vector2Int(3, 2) });
                var record = session.Document.GetSection("predefines")!["characterInsts"]![0]!;
                Assert.That((int?)record["position"]?["row"], Is.EqualTo(2));
                Assert.That((int?)record["position"]?["col"], Is.EqualTo(3));
                Assert.That((string?)record["direction"], Is.EqualTo("LEFT"));
                Assert.That(window.SelectedPresetCell, Is.EqualTo(new Vector2Int(3, 2)));
                Assert.That(window.OperatorPlacementEnabled, Is.False);
                Assert.That(session.Document.GetSection("mapData")!.ToString(), Is.EqualTo(terrain));
            }
            finally
            {
                typeof(MapEditorWindow).GetMethod("ClearInspectorDrafts", Private)!.Invoke(window, null);
                window.DiscardChanges();
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        private static void Click(Button button) =>
            typeof(Clickable).GetMethod("Invoke", Private)!.Invoke(button.clickable, new object?[] { null });

        [UnityTest]
        public IEnumerator LibrarySelectionAndMapMenuCreateBothPresetsInOneUndoStep()
        {
            var window = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                window.Show();
                yield return null;
                window.CreateGUI();
                var root = window.rootVisualElement;
                const string id = "char_285_medic2";
                var list = root.Q<ListView>("operator-library");
                var entry = list.itemsSource.Cast<OperatorPresetCatalog.Entry>().Single(item => item.Id == id);
                var row = list.makeItem();
                var index = list.itemsSource.IndexOf(entry);
                list.bindItem(row, index);
                Click(row.Q<Button>("operator-select-" + id));
                list.bindItem(row, index);
                Assert.That(row.Q<Button>("operator-select-" + id).ClassListContains("selected-tab"), Is.True);
                Assert.That(root.Q<Label>("preset-selected-operator").text, Does.Contain("Lancet-2"));
                var overlay = root.Q<OperatorPresetDetail>();
                Assert.That(overlay.style.display.value, Is.EqualTo(DisplayStyle.None));
                Click(row.Q<Button>("operator-info-" + id));
                Assert.That(overlay.style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Click(overlay.Query<Button>().ToList().First(button => button.text == "关闭"));

                var session = (DocumentSession)typeof(MapEditorWindow).GetField("session", Private)!.GetValue(window);
                session.UseLocalHistory();
                var before = session.Json;
                var raw = root.Q<TextField>("device-edit-json");
                var edited = JObject.Parse(raw.value);
                edited["inst"]!["level"] = 20;
                edited["alias"] = "scene-medic";
                raw.value = edited.ToString();
                root.Q<PopupField<string>>("device-edit-section").value = "hardPredefines";
                root.Q<PopupField<string>>("device-placement-direction").value = "LEFT";
                Action<MapEditorWindow.OperatorPlacementChoice>? resolve = null;
                window.OperatorPlacementPrompt = choice => resolve = choice;
                var position = new[] { new Vector2Int(2, 1) };
                window.PaintWorkspaceCells(position);
                Assert.That(session.Json, Is.EqualTo(before));
                Assert.That(resolve, Is.Not.Null);
                resolve!(MapEditorWindow.OperatorPlacementChoice.Cancel);
                Assert.That(session.Json, Is.EqualTo(before));
                Assert.That(window.OperatorPlacementEnabled, Is.True);
                window.PaintWorkspaceCells(position);
                resolve!(MapEditorWindow.OperatorPlacementChoice.Both);
                var layer = session.Document.GetSection("hardPredefines")!;
                Assert.That((int?)layer["characterInsts"]![0]!["inst"]!["level"], Is.EqualTo(20));
                Assert.That((string?)layer["characterInsts"]![0]!["direction"], Is.EqualTo("LEFT"));
                Assert.That((int?)layer["characterInsts"]![0]!["position"]!["row"], Is.EqualTo(1));
                Assert.That((string?)layer["characterInsts"]![0]!["alias"], Is.EqualTo("scene-medic"));
                Assert.That((int?)layer["characterCards"]![0]!["inst"]!["level"], Is.EqualTo(20));
                Assert.That(layer["characterCards"]![0]!["position"], Is.Null);
                Assert.That(layer["characterCards"]![0]!["alias"]?.Type, Is.EqualTo(JTokenType.Null));
                Assert.That(window.SelectedPresetCell, Is.EqualTo(new Vector2Int(2, 1)));
                Assert.That(root.Q<Button>("preset-save").enabledSelf, Is.True);
                session.StepHistory(false);
                Assert.That(session.Json, Is.EqualTo(before));
            }
            finally
            {
                typeof(MapEditorWindow).GetMethod("ClearInspectorDrafts", Private)!.Invoke(window, null);
                window.DiscardChanges();
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [TestCase("char_263_skadi", 7, "uniequip_002_skadi", "技能 7", "一模 2级")]
        [TestCase("char_263_skadi", 8, "uniequip_003_skadi", "技能专一", "二模 2级")]
        [TestCase("char_4046_ebnhlz", 9, "uniequip_004_ebnhlz", "技能专二", "三模 2级")]
        [TestCase("char_2015_dusk", 10, "uniequip_004_dusk", "技能专三", "集成特限模 2级")]
        [TestCase("char_416_zumama", 10, "uniequip_004_zumama", "技能专三", "生息特限模 2级")]
        public void PresetCardSummaryUsesMasteryAndModuleNames(string character, int skillLevel, string id,
            string skill, string module)
        {
            var record = new JObject
            {
                ["inst"] = new JObject { ["characterKey"] = character, ["phase"] = 2, ["level"] = 90 },
                ["skillIndex"] = 0,
                ["mainSkillLvl"] = skillLevel,
                ["uniEquipIds"] = new JArray(new JObject { ["key"] = id, ["level"] = 2 })
            };
            var preview = new PresetPreviewContent(record, true, false);
            var card = new Button();
            preview.AddTo(card, preview.Name);
            Assert.That(card.Q<Label>("preset-preview-detail").text, Does.EndWith($" · {skill} · {module}"));
        }

        [Test]
        public void HandPreviewShowsCardsFromTheSelectedLayerAndUsesOriginalArt()
        {
            var document = LevelDocument.Create(3, 2)
                .WithSection("predefines", "{\"tokenCards\":[{\"inst\":{\"characterKey\":\"trap_001_crate\"},\"initialCnt\":3}]}")
                .WithSection("hardPredefines", "{\"characterCards\":[{\"inst\":{\"characterKey\":\"char_017_huang\",\"phase\":2,\"level\":45,\"potentialRank\":1},\"skinId\":\"char_017_huang@as#1\"}]}");
            (string Layer, string Role, int Index)? picked = null;
            var view = new PresetHandPreview((layer, role, index) => { picked = (layer, role, index); return true; });
            view.Update(document, "predefines", true);
            Assert.That(view.Q<Image>("deployment-card-portrait").image.name, Is.EqualTo("deployment-portraits"));
            Assert.That(view.Q<Image>("deployment-card-profession").image.name, Is.EqualTo("deployment-style"));
            Assert.That(view.Q<Label>("deployment-card-quantity").text, Is.EqualTo("×3"));
            view.Update(document, "hardPredefines", true);
            var card = view.Q<Button>("map-hand-card-0");
            Assert.That(card.Q<Image>("deployment-card-portrait").image.name, Is.EqualTo("deployment-portraits"));
            Assert.That(card.Q<Image>("deployment-card-elite").image.name, Is.EqualTo("deployment-style"));
            Assert.That(card.tooltip, Does.Contain("精英 2 · Lv.45 · 潜 2"));
            Click(card);
            Assert.That(picked, Is.EqualTo(("hardPredefines", "characterCards", 0)));
            Assert.That(card.Q<Image>("deployment-card-selected").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            view.Update(document, "hardPredefines", false);
            Assert.That(view.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void HandPreviewShrinksBeforeScrollingAndCollapsesOnEmpty()
        {
            var one = PresetHandPreview.Measure(1050, 540, 1);
            var normal = PresetHandPreview.Measure(1050, 540, 12);
            var narrow = PresetHandPreview.Measure(430, 540, 12);
            Assert.That(one.StripWidth, Is.EqualTo(89));
            Assert.That(normal.CardWidth, Is.LessThan(one.CardWidth));
            Assert.That(normal.Scroll, Is.False);
            Assert.That(narrow.CardWidth, Is.EqualTo(54.5f));
            Assert.That(narrow.Scroll, Is.True);
            Assert.That(narrow.StripWidth, Is.EqualTo(414));
            var view = new PresetHandPreview();
            view.Update(LevelDocument.Create(2, 2), "predefines", true);
            Assert.That(view.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator RaisedHandCardKeepsTheFrameAndHitAreaTogether()
        {
            var window = ScriptableObject.CreateInstance<UnityEditor.EditorWindow>();
            try
            {
                window.ShowUtility();
                window.position = new Rect(100, 100, 360, 360);
                var host = new VisualElement();
                host.style.width = 310;
                host.style.height = 300;
                host.style.flexShrink = 0;
                window.rootVisualElement.Add(host);
                var view = new PresetHandPreview((_, _, _) => true);
                host.Add(view);
                var document = LevelDocument.Create(2, 2).WithSection("predefines",
                    "{\"tokenCards\":[{\"inst\":{\"characterKey\":\"trap_001_crate\"}},{\"inst\":{\"characterKey\":\"trap_001_crate\"}}]}");
                view.Update(document, "predefines", true);
                yield return null;
                var card = view.Q<Button>("map-hand-card-0");
                var neighbor = view.Q<Button>("map-hand-card-1");
                var originalTop = card.worldBound.yMin;
                var neighborTop = neighbor.worldBound.yMin;
                Assert.That(card.worldBound.height, Is.GreaterThan(10));
                Click(card);
                yield return null;
                window.Repaint();
                yield return null;
                var frame = card.Q<Image>("deployment-card-selected").worldBound;
                Assert.That(frame.yMin, Is.EqualTo(card.worldBound.yMin).Within(0.1f));
                Assert.That(frame.yMin + 2, Is.LessThan(originalTop));
                Assert.That(neighbor.worldBound.yMin, Is.EqualTo(neighborTop).Within(0.1f));
                var raisedPoint = new Vector2(frame.center.x, frame.yMin + 2);
                Assert.That(window.rootVisualElement.worldBound.Contains(raisedPoint), Is.True,
                    "The raised card must lie inside its test window before checking picking.");
                Assert.That(view.panel.Pick(raisedPoint), Is.SameAs(card));
            }
            finally { window.Close(); }
        }
    }
}
