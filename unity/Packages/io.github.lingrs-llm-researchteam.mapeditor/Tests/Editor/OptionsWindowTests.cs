#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Arknights.MapEditor.Core;
using Arknights.MapEditor.Editor;
using Arknights.MapEditor.Storage;
using Arknights.MapEditor.Catalog;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Tests
{
    public sealed class OptionsWindowTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private MapEditorWindow window = null!;
        private sealed class Dialogs : IEditorFileDialogs
        {
            internal bool Allow;
            public string Open(string title, string directory, string extension) => "";
            public string Save(string title, string directory, string name, string extension) => "";
            public bool Discard(string title, string message) => Allow;
        }
        private DocumentSession Session => (DocumentSession)typeof(MapEditorWindow).GetField("session", Private)!.GetValue(window);
        private object? Call(string name, params object[] arguments) => typeof(MapEditorWindow).GetMethod(name, Private)!.Invoke(window, arguments);
        private TextField Raw => window.rootVisualElement.Q<TextField>("global-field-options");
        private JObject Options => (JObject)Session.Document.GetSection("options")!;
        private static void Click(Button button) => typeof(Clickable).GetMethod("Invoke", Private)!.Invoke(button.clickable, new object?[] { null });
        private void Click(string name) => Click(window.rootVisualElement.Q<Button>(name));
        private void SetOptions(string? value)
        {
            var document = LevelDocument.Create(2, 2);
            document = value == null ? document.RemoveSection("options") : document.WithSection("options", value);
            Session.Load(document.ToJson(), "", "");
            typeof(MapEditorWindow).GetField("draft", Private)!.SetValue(window, Session.Json);
            Call("ClearInspectorDrafts"); window.CreateGUI();
        }

        [UnitySetUp] public IEnumerator SetUp()
        {
            window = ScriptableObject.CreateInstance<MapEditorWindow>(); window.Dialogs = new Dialogs();
            window.Show(); yield return null; Session.UseLocalHistory(); SetOptions(null);
        }
        [TearDown] public void TearDown()
        {
            if (window == null) return;
            Call("ClearInspectorDrafts"); window.DiscardChanges(); window.Close();
        }

        [Test] public void FormUsesNativeDeclarationOrderAndOpeningDoesNotCreateOptions()
        {
            Assert.That(Session.Document.GetSection("options"), Is.Null);
            Assert.That(Raw.value, Is.Empty);
            var keys = window.rootVisualElement.Q("options-form").Children().Take(15).Select(item => item.name).ToArray();
            Assert.That(keys, Is.EqualTo(OptionsFieldSchema.Fields.Select(field => "options-field-" + field.Key).ToArray()));
            Call("ApplyOptionsDraft");
            Assert.That(Session.Document.GetSection("options"), Is.Null);
        }

        [Test] public void ScalarEditsPreserveOtherSectionsAndUnknownMembersAndUndoInOneStep()
        {
            SetOptions("{\"maxLifePoint\":15,\"future\":{\"x\":2},\"initialCost\":3}");
            var original = Session.Json; var map = Session.Document.GetSection("mapData")!.ToString();
            window.rootVisualElement.Q<IntegerField>("options-field-maxLifePoint-integer").value = 30;
            Assert.That(Options["maxLifePoint"]!.Value<int>(), Is.EqualTo(15));
            Call("ApplyOptionsDraft");
            Assert.That(Options["maxLifePoint"]!.Value<int>(), Is.EqualTo(30));
            Assert.That(Options["future"]!["x"]!.Value<int>(), Is.EqualTo(2));
            Assert.That(Session.Document.GetSection("mapData")!.ToString(), Is.EqualTo(map));
            Session.StepHistory(false);
            Assert.That(Session.Json, Is.EqualTo(original));
        }

        [TestCase("costIncreaseTime", "0", "自然回费计时器不启用")]
        [TestCase("costIncreaseTime", "-1", "自然回费计时器不启用")]
        [TestCase("costIncreaseTime", "1.0000308066082653e-05", "自然回费计时器不启用")]
        [TestCase("costIncreaseTime", "1.0000308975577354e-05", null)]
        [TestCase("maxPlayTime", "2147483520", null)]
        [TestCase("maxPlayTime", "2147483648", "超出 FP 转换范围")]
        [TestCase("maxPlayTime", "-2147483904", "超出 FP 转换范围")]
        [TestCase("moveMultiplier", "-0.5", "固定方向位移")]
        [TestCase("maxLifePoint", "0", "以非正值初始化生命")]
        [TestCase("maxLifePoint", "300", null)]
        [TestCase("costIncreaseTime", "null", null)]
        [TestCase("costIncreaseTime", "\"0\"", null)]
        public void RuntimeNoteReflectsTheCurrentDraftWithoutChangingIt(string key, string value, string? note)
        {
            SetOptions("{}");
            Raw.value = "{\"" + key + "\":" + value + "}";
            var label = window.rootVisualElement.Q<HelpBox>("options-field-" + key + "-runtime-note");
            Assert.That(label, Is.Not.Null);
            Assert.That(label.style.display.value, Is.EqualTo(note == null ? DisplayStyle.None : DisplayStyle.Flex));
            if (note != null) Assert.That(label.text, Does.Contain(note));
            Assert.That(Options.Count, Is.Zero);
            if (value == "\"0\"") return;
            var expected = OptionsFieldSchema.Parse(Raw.value);
            Call("ApplyOptionsDraft");
            Assert.That(JToken.DeepEquals(Options, expected), Is.True);
        }

        [Test] public void MissingNullAndExplicitFieldCreationRemainDistinct()
        {
            Click("options-create"); Call("ApplyOptionsDraft");
            Assert.That(Options.Count, Is.Zero);
            Click("options-field-maxLifePoint-add");
            Call("ApplyOptionsDraft"); Assert.That(Options["maxLifePoint"]!.Value<int>(), Is.EqualTo(15));
            Click("options-field-maxLifePoint-null"); Call("ApplyOptionsDraft");
            Assert.That(Options["maxLifePoint"]!.Type, Is.EqualTo(JTokenType.Null));
            Click("options-field-maxLifePoint-remove"); Call("ApplyOptionsDraft"); Assert.That(Options.Property("maxLifePoint"), Is.Null);
            Click("options-null"); Call("ApplyOptionsDraft"); Assert.That(Session.Document.GetSection("options")!.Type, Is.EqualTo(JTokenType.Null));
            Click("options-remove"); Call("ApplyOptionsDraft"); Assert.That(Session.Document.GetSection("options"), Is.Null);
        }

        [Test] public void MalformedJsonSurvivesRebuildAndCannotBeAppliedOrHiddenByForm()
        {
            SetOptions("{\"maxLifePoint\":15}"); Raw.value = "{unfinished";
            window.CreateGUI();
            Assert.That(Raw.value, Is.EqualTo("{unfinished"));
            Assert.That(window.rootVisualElement.Q<Button>("options-show-form").enabledSelf, Is.False);
            Assert.Throws<TargetInvocationException>(() => Call("ApplyOptionsDraft"));
            Assert.That(Options["maxLifePoint"]!.Value<int>(), Is.EqualTo(15));
            Click("options-reload"); Assert.That(Raw.value, Is.EqualTo("{unfinished"));
            ((Dialogs)window.Dialogs).Allow = true; Click("options-reload");
            Assert.That(OptionsFieldSchema.Parse(Raw.value)!["maxLifePoint"]!.Value<int>(), Is.EqualTo(15));
        }

        [Test] public void StaleDraftIsPreservedAndCannotOverwriteAnotherRevision()
        {
            SetOptions("{\"initialCost\":3}"); Raw.value = "{\"initialCost\":8}";
            Session.Edit(Session.Document.WithLevelId("changed"), "change document");
            typeof(MapEditorWindow).GetField("draft", Private)!.SetValue(window, Session.Json);
            window.CreateGUI();
            Assert.That(Raw.value, Does.Contain("8"));
            var error = Assert.Throws<TargetInvocationException>(() => Call("ApplyOptionsDraft"));
            Assert.That(error!.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(Options["initialCost"]!.Value<int>(), Is.EqualTo(3));
            Assert.That(Session.Document.LevelId, Is.EqualTo("changed"));
        }

        [Test] public void CleanFormRefreshesToNewRevisionButEditLockRejectsChanges()
        {
            SetOptions("{\"initialCost\":3}");
            Call("CommitEdit", Session.Document.WithSection("options", "{\"initialCost\":9}"), "external", null!);
            Assert.That(window.rootVisualElement.Q<IntegerField>("options-field-initialCost-integer").value, Is.EqualTo(9));
            window.SetWorkspaceEditLock(true);
            window.rootVisualElement.Q<IntegerField>("options-field-initialCost-integer").value = 17;
            Assert.That(OptionsFieldSchema.Parse(Raw.value)!["initialCost"]!.Value<int>(), Is.EqualTo(9));
            Assert.That(Assert.Throws<TargetInvocationException>(() => Call("ApplyOptionsDraft"))!.InnerException, Is.TypeOf<InvalidOperationException>());
        }

        [Test] public void MaskButtonsPreserveUnknownBitsAndOriginalStringUntilExplicitEdit()
        {
            SetOptions("{\"functionDisableMask\":1048577}");
            Click("options-mask-none"); Call("ApplyOptionsDraft");
            Assert.That(Options["functionDisableMask"]!.Value<int>(), Is.EqualTo(1048576));
            Click("options-mask-all"); Call("ApplyOptionsDraft");
            Assert.That(Options["functionDisableMask"]!.Value<int>(), Is.EqualTo(1048576 | OptionsFieldSchema.KnownMask));
            SetOptions("{\"functionDisableMask\":\"CARD_LIST, PAUSE_BUTTON\",\"initialCost\":3}");
            window.rootVisualElement.Q<IntegerField>("options-field-initialCost-integer").value = 5; Call("ApplyOptionsDraft");
            Assert.That(Options["functionDisableMask"]!.Value<string>(), Is.EqualTo("CARD_LIST, PAUSE_BUTTON"));
            window.rootVisualElement.Q<Toggle>("options-mask-bit-1").value = false; Call("ApplyOptionsDraft");
            Assert.That(Options["functionDisableMask"]!.Value<int>(), Is.EqualTo(16));
        }

        [Test] public void MaskSearchUsesFuzzyMatchingWithoutEditingTheValue()
        {
            SetOptions("{\"functionDisableMask\":17}");
            var query = window.rootVisualElement.Q<TextField>("options-mask-search");
            var card = window.rootVisualElement.Q<Toggle>("options-mask-bit-1");
            var pause = window.rootVisualElement.Q<Toggle>("options-mask-bit-16");
            query.value = "PAUSE_BUTON";
            Assert.That(card.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(pause.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(Options["functionDisableMask"]!.Value<int>(), Is.EqualTo(17));
            query.value = "";
            Assert.That(card.style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        [Test] public void BlackboardEditsPreserveDuplicatesEmptyStringsAndExtensionMembers()
        {
            SetOptions("{\"configBlackBoard\":[{\"key\":\"same\",\"value\":1,\"valueStr\":\"\",\"future\":true},{\"key\":\"SAME\",\"value\":2,\"valueStr\":null}]}");
            window.rootVisualElement.Q<FloatField>("options-blackboard-value-float").value = 4;
            Call("ApplyOptionsDraft");
            var entries = (JArray)Options["configBlackBoard"]!;
            Assert.That(entries.Count, Is.EqualTo(2)); Assert.That(entries[0]["value"]!.Value<float>(), Is.EqualTo(4));
            Assert.That(entries[0]["valueStr"]!.Value<string>(), Is.Empty); Assert.That(entries[0]["future"]!.Value<bool>(), Is.True);
            Assert.That(entries[1]["valueStr"]!.Type, Is.EqualTo(JTokenType.Null));
            Click("options-blackboard-copy"); Call("ApplyOptionsDraft");
            entries = (JArray)Options["configBlackBoard"]!; Assert.That(entries.Count, Is.EqualTo(3));
            Assert.That(entries.Select(item => item["key"]!.Value<string>()), Is.EqualTo(new[] { "same", "same", "SAME" }));
            Click("options-blackboard-down"); Call("ApplyOptionsDraft");
            Assert.That(((JArray)Options["configBlackBoard"]!).Select(item => item["key"]!.Value<string>()), Is.EqualTo(new[] { "same", "SAME", "same" }));
            Click("options-blackboard-delete"); Call("ApplyOptionsDraft"); Assert.That(((JArray)Options["configBlackBoard"]!).Count, Is.EqualTo(2));
        }

        [Test] public void InvalidSingleFieldJsonIsRejectedVisiblyWithoutAHiddenDraft()
        {
            SetOptions("{\"functionDisableMask\":1}");
            var field = window.rootVisualElement.Q<TextField>("options-field-functionDisableMask-raw");
            var before = Raw.value;
            field.value = "\"unfinished";
            Assert.That(field.value, Is.EqualTo("1"));
            Assert.That(Raw.value, Is.EqualTo(before));
            Assert.That(window.rootVisualElement.Q("options-field-functionDisableMask").Query<Label>().ToList()
                .Any(label => label.text.StartsWith("输入未应用：", StringComparison.Ordinal)), Is.True);
            Call("ApplyOptionsDraft");
            Assert.That(Options["functionDisableMask"]!.Value<int>(), Is.EqualTo(1));
        }

        [Test] public void NewTypeErrorsAreRejectedWhileUnchangedImportedProblemsArePreserved()
        {
            SetOptions("{\"characterLimit\":\"legacy\",\"initialCost\":3}");
            window.rootVisualElement.Q<IntegerField>("options-field-initialCost-integer").value = 4; Call("ApplyOptionsDraft");
            Assert.That(Options["characterLimit"]!.Value<string>(), Is.EqualTo("legacy"));
            var baseline = Session.Json;
            Raw.value = "{\"characterLimit\":\"legacy\",\"initialCost\":2147483648}";
            Assert.That(Assert.Throws<TargetInvocationException>(() => Call("ApplyOptionsDraft"))!.InnerException, Is.TypeOf<FormatException>());
            Assert.That(Session.Json, Is.EqualTo(baseline));
        }

        [Test] public void MissingOptionsCanBrowseAllKnownValuesWithoutCreatingOptions()
        {
            SetOptions(null);
            var browser = window.rootVisualElement.Q<OptionsSampleBrowser>("options-observed-maxCost");
            Assert.That(browser.enabledInHierarchy, Is.True);
            browser.Q<Foldout>().value = true;
            Assert.That(browser.Q<ListView>("options-observed-maxCost-values").itemsSource.Count, Is.GreaterThan(0));
            Assert.That(browser.Q<Button>("options-observed-maxCost-choose").enabledSelf, Is.False);
            Assert.That(Session.Document.GetSection("options"), Is.Null);
            Assert.That(Raw.value, Is.Empty);
        }

        [Test] public void ObservedChoicePreservesCustomValueUntilSelectedAndChangesOnlyDraft()
        {
            SetOptions("{\"maxCost\":12345,\"future\":true}");
            var document = Session.Json;
            var sample = OptionsValueCatalog.LoadBundled().Field("maxCost")!.Recommended[0];
            var browser = window.rootVisualElement.Q<OptionsSampleBrowser>("options-observed-maxCost");
            browser.Q<Foldout>().value = true;
            Assert.That(Options["maxCost"]!.Value<int>(), Is.EqualTo(12345));
            Click("options-observed-maxCost-recommended-" + sample.Index);
            Assert.That(Session.Json, Is.EqualTo(document));
            Assert.That(JToken.DeepEquals(OptionsFieldSchema.Parse(Raw.value)!["maxCost"], sample.CopyValue()), Is.True);
            Assert.That(OptionsFieldSchema.Parse(Raw.value)!["future"]!.Value<bool>(), Is.True);
            Assert.That(browser.Q<Foldout>().value, Is.True);
            Call("ApplyOptionsDraft"); Session.StepHistory(false);
            Assert.That(Session.Json, Is.EqualTo(document));
        }

        [Test] public void ObservedMaskChoicePreservesUnknownBitsAndLockKeepsBrowserReadable()
        {
            SetOptions("{\"functionDisableMask\":1048577}");
            var sample = OptionsValueCatalog.LoadBundled().Field("functionDisableMask")!.Recommended[0];
            Click("options-observed-functionDisableMask-recommended-" + sample.Index);
            Assert.That(OptionsFieldSchema.Parse(Raw.value)!["functionDisableMask"]!.Value<int>() & 1048576, Is.EqualTo(1048576));
            window.SetWorkspaceEditLock(true); window.CreateGUI();
            var browser = window.rootVisualElement.Q<OptionsSampleBrowser>("options-observed-functionDisableMask");
            Assert.That(browser.enabledInHierarchy, Is.True);
            Assert.That(browser.Q<Button>("options-observed-functionDisableMask-recommended-" + sample.Index).enabledSelf, Is.False);
        }

        [Test] public void RecommendedAndMissingSourceNavigationKeepValueSelectionInSync()
        {
            SetOptions("{\"maxPlayTime\":12345}");
            const string browserName = "options-observed-maxPlayTime";
            var group = OptionsValueCatalog.LoadBundled().Field("maxPlayTime")!;
            var browser = window.rootVisualElement.Q<OptionsSampleBrowser>(browserName);
            browser.Q<Foldout>().value = true;
            var list = browser.Q<ListView>(browserName + "-values");
            var previous = (OptionsValueCatalog.Sample)list.selectedItem;
            var candidate = group.Recommended.First(item => item != previous);
            Click(browserName + "-recommended-" + candidate.Index);
            Assert.That(list.selectedItem, Is.SameAs(candidate));
            list.SetSelection(group.Samples.ToList().IndexOf(previous));
            Click(browserName + "-choose");
            Assert.That(JToken.DeepEquals(OptionsFieldSchema.Parse(Raw.value)!["maxPlayTime"], previous.CopyValue()), Is.True);
            Click(browserName + "-missing");
            Assert.That(list.selectedIndex, Is.EqualTo(-1));
            Assert.That(browser.Q<Button>(browserName + "-choose").enabledSelf, Is.False);
            var all = browser.Q<Foldout>();
            var sources = browser.Q<Foldout>("options-sample-sources");
            Assert.That(sources.value, Is.True);
            all.value = false;
            all.value = true;
            Assert.That(list.selectedIndex, Is.EqualTo(-1));
            Assert.That(sources.text, Does.StartWith("未提供此字段的全部出处"));
            Assert.That(browser.Q("options-selected-sample").style.display.value, Is.EqualTo(DisplayStyle.None));
            list.SetSelection(group.Samples.ToList().IndexOf(previous));
            Assert.That(list.selectedItem, Is.SameAs(previous));
            Assert.That(browser.Q<Button>(browserName + "-choose").enabledSelf, Is.True);
            Assert.That(sources.value, Is.True);
            Assert.That(sources.text, Does.StartWith("所选值的出处"));
            Assert.That(Options["maxPlayTime"]!.Value<int>(), Is.EqualTo(12345));
        }

        [Test] public void BlackboardWholeArrayAndSingleRecordChoicesHaveDistinctSemantics()
        {
            SetOptions("{\"configBlackBoard\":[{\"key\":\"keep-me\",\"value\":3,\"valueStr\":\"\",\"extra\":true}],\"future\":42}");
            var document = Session.Json;
            var catalog = OptionsValueCatalog.LoadBundled();
            var sample = catalog.Field("configBlackBoard")!.Recommended.First(item => item.CopyValue() is JArray);
            Click("options-observed-configBlackBoard-recommended-" + sample.Index);
            var replacement = (JArray)OptionsFieldSchema.Parse(Raw.value)!["configBlackBoard"]!;
            Assert.That(JToken.DeepEquals(replacement, sample.CopyValue()), Is.True);
            var key = catalog.BlackboardKeys.First(item => catalog.Blackboard(item)!.Recommended.Count > 0);
            var picker = window.rootVisualElement.Q<OptionsBlackboardSamples>("options-blackboard-key-samples");
            picker.value = true;
            picker.Q<ListView>("options-blackboard-known-keys").SetSelection(catalog.BlackboardKeys.ToList().IndexOf(key));
            var entry = catalog.Blackboard(key)!.Recommended[0];
            Click(picker.Q<Button>("options-observed-" + key + "-recommended-" + entry.Index));
            var appended = (JArray)OptionsFieldSchema.Parse(Raw.value)!["configBlackBoard"]!;
            Assert.That(appended.Count, Is.EqualTo(replacement.Count + 1));
            Assert.That(JToken.DeepEquals(appended.Last, entry.CopyValue()), Is.True);
            Assert.That(OptionsFieldSchema.Parse(Raw.value)!["future"]!.Value<int>(), Is.EqualTo(42));
            Assert.That(Session.Json, Is.EqualTo(document));
        }
    }
}
