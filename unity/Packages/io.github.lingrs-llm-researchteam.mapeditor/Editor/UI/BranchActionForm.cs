#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Arknights.MapEditor.Catalog;
using Arknights.MapEditor.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal sealed class BranchEnemyChoice
    {
        internal string Key = "", Label = "", Path = "";
    }

    /// <summary>Edits a detached ActionData value. Consumers own persistence and validation.</summary>
    internal class ActionDataForm : VisualElement
    {
        private JObject fields;
        private readonly JObject document;
        private readonly IReadOnlyList<BranchEnemyChoice> enemies;
        private readonly Action<JObject> changed;
        private readonly Action<string> locatePath;
        private readonly Action<string, int, bool> showRoute;
        private readonly bool fromBranch;
        private readonly string prefix;
        private readonly bool musicOnly;
        private readonly Action? locateMusic;
        private readonly Func<MusicEventCatalog.Entry?>? previousMusic;
        private readonly Action<MusicEventPicker>? openMusicLibrary;
        private bool showAll;
        private readonly Action<bool>? viewChanged;
        private Button? applicableButton, allButton;
        private readonly Action<string>? rawChanged;
        private readonly Label error = new();
        private readonly VisualElement form = new();
        private readonly TextField raw = new() { multiline = true };
        private bool rawPending;
        internal bool HasPendingRaw => rawPending;
        private bool resourceExpanded;
        private readonly JArray? siblingActions;
        private readonly int actionIndex;
        private Foldout? groupView;
        private bool groupExpanded;
        private readonly Dictionary<string, Label> captions = new(StringComparer.Ordinal);
        private static readonly HashSet<string> Booleans = new(StringComparer.Ordinal)
        {
            "managedByScheduler", "blockFragment", "autoPreviewRoute", "autoDisplayEnemyInfo",
            "isUnharmfulAndAlwaysCountAsKilled", "dontBlockWave", "forceBlockWaveInBranch"
        };
        private static readonly HashSet<string> Numbers = new(StringComparer.Ordinal)
        { "count", "preDelay", "interval", "routeIndex", "weight" };
        private static readonly HashSet<string> Texts = new(StringComparer.Ordinal)
        { "key", "hiddenGroup", "randomSpawnGroupKey", "randomSpawnGroupPackKey" };

        internal ActionDataForm(JObject initial, JObject document, IReadOnlyList<BranchEnemyChoice> enemies,
            Action<JObject> changed, Action<string> locatePath, Action<string, int, bool> showRoute, bool fromBranch,
            Action<string>? rawChanged = null, bool showAll = false, Action<bool>? viewChanged = null, string prefix = "action", JArray? siblingActions = null, int actionIndex = -1,
            bool musicOnly = false, Action? locateMusic = null, Func<MusicEventCatalog.Entry?>? previousMusic = null,
            Action<MusicEventPicker>? openMusicLibrary = null)
        {
            fields = (JObject)initial.DeepClone();
            this.siblingActions = siblingActions == null ? null : (JArray)siblingActions.DeepClone(); this.actionIndex = actionIndex;
            this.document = (JObject)document.DeepClone();
            this.enemies = enemies;
            this.changed = changed;
            this.locatePath = locatePath;
            this.showRoute = showRoute;
            this.rawChanged = rawChanged;
            this.fromBranch = fromBranch; this.prefix = prefix; this.showAll = showAll; this.viewChanged = viewChanged;
            this.musicOnly = musicOnly; this.locateMusic = locateMusic; this.previousMusic = previousMusic;
            this.openMusicLibrary = openMusicLibrary;
            name = prefix + "-action-form"; error.name = prefix + "-action-error";
            form.name = prefix + "-action-fields"; raw.name = prefix + "-action-json";
            error.style.whiteSpace = WhiteSpace.Normal;
            Add(error);
            var modes = Row(this);
            applicableButton = Button(modes, "适用字段", () => SetView(false));
            allButton = Button(modes, "全部字段", () => SetView(true));
            modes.style.display = musicOnly ? DisplayStyle.None : DisplayStyle.Flex;
            Add(form);
            var advanced = new Foldout { text = "原始动作 JSON", value = false };
            advanced.AddToClassList("wave-settings");
            raw.SetValueWithoutNotify(fields.ToString(Formatting.Indented));
            raw.RegisterValueChangedCallback(evt => Run(() =>
            {
                rawPending = true; form.SetEnabled(false); rawChanged?.Invoke(evt.newValue);
                error.text = "原始 JSON 已编辑，点击“读取为待应用字段”更新表单。";
            }));
            advanced.Add(raw);
            Button(advanced, "读取为待应用字段", () =>
            {
                var candidate = ParseObject(raw.value);
                changed((JObject)candidate.DeepClone());
                fields = candidate;
                rawPending = false;
                form.SetEnabled(true);
                Render();
            });
            if (!musicOnly) Add(advanced);
            Render();
        }

        private static JObject ParseObject(string text) =>
            LevelDocument.Create(1, 1).WithSection("action", text).GetSection("action") as JObject ??
            throw new FormatException("动作必须是 JSON 对象。");

        private void Render()
        {
            form.Clear(); groupView = null;
            captions.Clear();
            applicableButton?.EnableInClassList("selected-tab", !showAll);
            allButton?.EnableInClassList("selected-tab", showAll);
            var type = WaveSchedule.ActionType(fields["actionType"]);
            VisualElement? section = null;
            var sectionName = "";
            foreach (var key in WaveSchedule.ActionFields.Concat(fields.Properties().Select(prop => prop.Name)
                .Where(key => !WaveSchedule.ActionFields.Contains(key))))
            {
                if (musicOnly ? key != "key" && key != "preDelay" : !showAll && !Applicable(key, type)) continue;
                var heading = SectionName(key);
                if (section == null || sectionName != heading)
                {
                    sectionName = heading;
                    section = new VisualElement(); section.AddToClassList("wave-settings");
                    var title = Note(heading); title.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Normal;
                    section.Add(title); form.Add(section);
                }
                DrawField(section, key, type);
            }
            if (type == "EMPTY") form.Add(Note(fromBranch
                ? "EMPTY 没有动作执行器；分支查询可读取敌人键与额外路线。"
                : "EMPTY 没有动作执行器；主随机组可用空 key 表示不生成候选。"));
            if (type == "PLAY_OPERA") form.Add(Note("operaConfig · " + Value(document["operaConfig"]) +
                " · 路线读取：" + OperaRouteLabel()));
            raw.SetValueWithoutNotify(fields.ToString(Formatting.Indented));
        }

        private void SetView(bool all)
        {
            if (rawPending) throw new InvalidOperationException("请先读取原始 JSON，再切换字段视图。");
            showAll = all; viewChanged?.Invoke(all); Render();
        }

        private bool Applicable(string key, string type)
        {
            if (!WaveSchedule.ActionFields.Contains(key)) return true;
            if (!WaveSchedule.ActionTypes.Contains(type) || type == "E_NUM") return true;
            if (key == "actionType" || key == "preDelay" || key == "hiddenGroup") return true;
            if (key == "blockFragment") return !fromBranch;
            if (key == "key") return type != "PREVIEW_CURSOR" && type != "SHOW_ALL_HIDDEN_CARDS";
            if (key == "routeIndex") return ActionDataRules.RouteStatus(document, fields, fromBranch) != ActionRouteStatus.Unused;
            if (key.StartsWith("random", StringComparison.Ordinal) || key == "weight")
                return fromBranch || type == "SPAWN" || type == "ACTIVATE_PREDEFINED" || type == "EMPTY";
            if (type == "SPAWN") return key != "forceBlockWaveInBranch" || fromBranch;
            return type == "PREVIEW_CURSOR" && (key == "count" || key == "interval");
        }

        private static string SectionName(string key)
        {
            if (key == "actionType" || key == "managedByScheduler") return "动作与调度";
            if (key == "key" || key == "count" || key == "preDelay" || key == "interval" || key == "routeIndex") return "目标与时序";
            if (key == "blockFragment" || key == "autoPreviewRoute" || key == "autoDisplayEnemyInfo" ||
                key == "isUnharmfulAndAlwaysCountAsKilled" || key == "hiddenGroup") return "完成与显示";
            if (key.StartsWith("random", StringComparison.Ordinal) || key == "weight") return "随机组";
            if (key == "dontBlockWave" || key == "forceBlockWaveInBranch") return "波次阻塞";
            return "扩展字段";
        }

        private string OperaRouteLabel() => ActionDataRules.RouteStatus(document, fields, fromBranch) switch
        {
            ActionRouteStatus.Main => "主路线 routes",
            ActionRouteStatus.Extra => "额外路线 extraRoutes",
            ActionRouteStatus.Unused => "当前命令不读取路线",
            _ => "当前命令的路线消费尚未解析"
        };

        private void DrawField(VisualElement parent, string key, string type)
        {
            var card = new VisualElement { name = prefix + "-field-" + key };
            card.AddToClassList("wave-field");
            var caption = new Label(key + " · " + Value(fields[key]));
            caption.style.whiteSpace = WhiteSpace.Normal;
            captions[key] = caption;
            card.Add(caption);
            parent.Add(card);
            Action<JToken?>? setMusicKey = null;
            if (key == "actionType")
            {
                var choices = WaveSchedule.ActionTypes.Where(item => item != "E_NUM").ToList();
                if (!choices.Contains(type)) choices.Insert(0, type);
                var select = new PopupField<string>(choices, Math.Max(0, choices.IndexOf(type))) { name = prefix + "-input-actionType" };
                select.RegisterValueChangedCallback(evt => Run(() => Set(key, new JValue(evt.newValue), true)));
                card.Add(select);
            }
            else if (key == "key" && (type == "SPAWN" || type == "DISPLAY_ENEMY_INFO")) DrawEnemy(card);
            else if (key == "key" && (type == "ACTIVATE_PREDEFINED" || type == "WITHDRAW_PREDEFINED")) DrawAlias(card, type);
            else if (key == "key" && type == "TRIGGER_PREDEFINED") DrawTriggers(card);
            else if (key == "key" && type == "PLAY_BGM")
            {
                MusicEventPicker? picker = null;
                var input = DrawValue(card, key, caption, value =>
                {
                    Set(key, value);
                    picker?.SetKey(Text(value));
                });
                void UpdateMusicKey(JToken? value)
                {
                    Set(key, value);
                    input.SetValueWithoutNotify(Text(value));
                    picker?.SetKey(Text(value));
                }
                setMusicKey = UpdateMusicKey;
                picker = new MusicEventPicker(false, Text(fields[key]),
                    value => Run(() => UpdateMusicKey(new JValue(value))), previousMusic, openMusicLibrary);
                card.Add(picker);
                if (locateMusic != null)
                    Button(card, "在背景音乐中查看", locateMusic).name = prefix + "-action-music";
            }
            else if (key == "key" && ResourceType(type)) DrawResources(card, type, caption);
            else if (key == "routeIndex" && ActionDataRules.RouteLibrary(document, fields, fromBranch) is string library) DrawRoutes(card, library);
            else if (Booleans.Contains(key))
            {
                var choices = new List<string> { "未提供", "null", "false", "true" };
                var value = fields[key] == null ? "未提供" : fields[key]!.ToString(Formatting.None);
                if (!choices.Contains(value)) choices.Insert(0, value);
                var select = new PopupField<string>(choices, choices.IndexOf(value)) { name = prefix + "-input-" + key };
                select.RegisterValueChangedCallback(evt => Run(() =>
                {
                    Set(key, evt.newValue == "未提供" ? null : JToken.Parse(evt.newValue));
                    caption.text = key + " · " + Value(fields[key]);
                }));
                card.Add(select);
            }
            else if (key == "randomType" || key == "refreshType") DrawRefresh(card, key);
            else DrawValue(card, key, caption);
            if (key == "blockFragment") card.Add(Note(fromBranch ? "原生分支阶段不使用主片段的完成计数等待。" : "等待动作完成回调；出怪动作不会等待敌人死亡。"));
            if (key == "forceBlockWaveInBranch") card.Add(Note(fromBranch ? "分支出怪默认不阻挡当前波次；dontBlockWave=true 时仍不阻挡。" : "此字段用于分支出怪。"));
            if (key == "randomType" || key == "refreshType") card.Add(Note("原版枚举；刷新方式由对应玩法消费。"));
            if (key == "weight") card.Add(Note(fromBranch ? "阶段内随机组的权重。" : "当前片段随机组的权重。"));
            if (type == "PREVIEW_CURSOR" && (key == "count" || key == "interval"))
                card.Add(Note("原生路线指示固定执行 2 次，间隔 0.3 秒。"));
            if (key == "randomSpawnGroupKey")
            {
                groupView = new Foldout { text = "同组动作", value = groupExpanded }; card.Add(groupView);
                groupView.RegisterValueChangedCallback(evt => { if (evt.target == groupView) groupExpanded = evt.newValue; }); RefreshGroup();
            }
            var buttons = Row(card);
            void SetPresence(JToken? value)
            {
                if (setMusicKey != null) setMusicKey(value);
                else Set(key, value, true);
            }
            Button(buttons, "设为 null", () => SetPresence(JValue.CreateNull()));
            Button(buttons, "移除字段", () => SetPresence(null));
        }

        private TextField DrawValue(VisualElement card, string key, Label caption, Action<JToken>? assign = null)
        {
            var value = fields[key];
            var input = new TextField { name = prefix + "-input-" + key, isDelayed = true };
            input.SetValueWithoutNotify(Texts.Contains(key) && value?.Type == JTokenType.String
                ? value.Value<string>()! : value?.ToString(Formatting.None) ?? "");
            var localError = Note("");
            input.RegisterValueChangedCallback(evt => Run(() =>
            {
                try
                {
                    JToken parsed;
                    if (Texts.Contains(key)) parsed = new JValue(evt.newValue);
                    else
                    {
                        parsed = JToken.Parse(evt.newValue);
                        if (Numbers.Contains(key) && parsed.Type != JTokenType.Integer && parsed.Type != JTokenType.Float)
                            throw new FormatException(key + " 需要数值。");
                    }
                    if (assign != null) assign(parsed);
                    else Set(key, parsed, key == "key" && ResourceType(WaveSchedule.ActionType(fields["actionType"])));
                    caption.text = key + " · " + Value(fields[key]);
                    localError.text = "";
                }
                catch (Exception issue) when (issue is JsonException || issue is FormatException)
                {
                    if (Numbers.Contains(key))
                    {
                        Set(key, new JValue(evt.newValue));
                        caption.text = key + " · " + Value(fields[key]);
                        localError.text = issue.Message + " 输入已保留为待修正草稿。";
                    }
                    else localError.text = issue.Message + " 输入保留在此，尚未写入草稿。";
                }
            }));
            card.Add(input); card.Add(localError);
            return input;
        }

        private void DrawRefresh(VisualElement card, string key)
        {
            var choices = new List<string> { "ALWAYS", "PER_DAY", "NEVER", "PER_SETTLE_DAY", "PER_SEASON" };
            var ordinal = WaveSchedule.Integer(fields[key]);
            var value = ordinal is >= 0 and < 5 ? choices[ordinal.Value] :
                fields[key]?.Type == JTokenType.String ? fields[key]!.Value<string>()! : Value(fields[key]);
            if (!choices.Contains(value)) choices.Insert(0, value);
            var popup = new PopupField<string>(choices, choices.IndexOf(value)) { name = prefix + "-input-" + key };
            popup.RegisterValueChangedCallback(evt => Run(() => Set(key, new JValue(evt.newValue), true)));
            card.Add(popup);
        }

        private static bool ResourceType(string type) => type == "STORY" || type == "TUTORIAL" || type == "DIALOG" ||
            type == "PLAY_BGM" || type == "PLAY_OPERA" || type == "BATTLE_EVENTS";

        private void DrawResources(VisualElement card, string type, Label caption)
        {
            DrawValue(card, "key", caption);
            var fold = new Foldout { text = "已有资源与原版配置", value = resourceExpanded };
            card.Add(fold);
            var loaded = false;
            void Load()
            {
                if (loaded) return;
                var entries = ActionResourceCatalog.LoadBundled().Find(type, Text(document["operaConfig"]));
                loaded = true;
                var input = new TextField("搜索资源") { name = prefix + "-resource-search" };
                var summary = Note("");
                var results = new VisualElement();
                fold.Add(input); fold.Add(summary); fold.Add(results);
                void Results(IReadOnlyList<ActionResourceCatalog.Entry> rows, string? failure)
                {
                    results.Clear();
                    summary.text = failure ?? rows.Count + " 项";
                    void Page(int start)
                    {
                        foreach (var entry in rows.Skip(start).Take(30))
                        {
                            var row = new VisualElement(); row.AddToClassList("wave-settings");
                            Button(row, entry.Label, () => Set("key", new JValue(entry.Key), true));
                            row.Add(Note(entry.Source + " · " + entry.MatchStatus));
                            results.Add(row);
                        }
                        if (start + 30 < rows.Count)
                        {
                            Button? more = null;
                            more = Button(results, "显示后续 30 项", () => { more!.RemoveFromHierarchy(); Page(start + 30); });
                        }
                    }
                    Page(0);
                    if (rows.Count == 0 && failure == null) results.Add(Note("当前类型和配置没有原版候选，可直接输入 key。"));
                }
                var search = new CatalogSearchInput<ActionResourceCatalog.Entry>(input,
                    () => CatalogSearchRuntime.CreateWorker<ActionResourceCatalog.Entry>(entry => new JObject
                    { ["code"] = entry.Key, ["name"] = entry.Label, ["description"] = entry.Source + " " + entry.MatchStatus }),
                    entries, () => summary.text = "正在搜索…", Results);
                input.userData = search;
                Results(entries, null);
            }
            fold.RegisterValueChangedCallback(evt =>
            {
                if (evt.target != fold) return;
                resourceExpanded = evt.newValue;
                if (evt.newValue) Run(Load);
            });
            if (fold.value) Run(Load);
        }

        private void DrawEnemy(VisualElement card)
        {
            var current = Text(fields["key"]);
            var choices = enemies.Select(item => item.Label).ToList();
            choices.Insert(0, "当前值 · " + Value(fields["key"]));
            var selected = enemies.ToList().FindIndex(item => item.Key == current) + 1;
            var popup = new PopupField<string>("本关敌人", choices, selected) { name = prefix + "-input-key" };
            Button? locate = null;
            void RefreshLocate() => locate?.SetEnabled(enemies.Any(item => item.Key == Text(fields["key"])));
            popup.RegisterValueChangedCallback(_ => Run(() =>
            {
                if (popup.index > 0) Set("key", new JValue(enemies[popup.index - 1].Key));
                RefreshLocate();
            }));
            card.Add(popup);
            locate = Button(card, "定位敌人定义", () =>
            {
                var match = enemies.FirstOrDefault(item => item.Key == Text(fields["key"]));
                if (match != null) locatePath(match.Path);
            });
            RefreshLocate();
        }

        private void DrawAlias(VisualElement card, string type)
        {
            var aliases = BranchSchedule.Aliases(document, type);
            var choices = aliases.Select(AliasLabel).ToList();
            choices.Insert(0, "当前值 · " + Value(fields["key"]));
            var current = Text(fields["key"]);
            var selected = aliases.ToList().FindIndex(item => item.Alias == current) + 1;
            var popup = new PopupField<string>("预设别名", choices, selected) { name = prefix + "-input-key" };
            popup.RegisterValueChangedCallback(_ => Run(() =>
            {
                if (popup.index > 0) Set("key", new JValue(aliases[popup.index - 1].Alias), true);
            }));
            card.Add(popup);
            foreach (var alias in aliases.Where(item => item.Alias == current))
                Button(card, "定位 " + AliasLabel(alias), () => locatePath(Pointer(alias.Path)));
            if (type == "ACTIVATE_PREDEFINED") card.Add(Note("激活会分别尝试隐藏卡牌和场上预设。"));
        }

        private void DrawRoutes(VisualElement card, string library)
        {
            var routes = document[library] as JArray ?? new JArray();
            var indices = Enumerable.Range(0, routes.Count).Where(index => WaveSchedule.HasRoute(routes, index)).ToList();
            var labels = indices.Select(index => library + "[" + index + "] · " + Value(routes[index]["motionMode"]) +
                " · " + Value(routes[index]["startPosition"]) + " → " + Value(routes[index]["endPosition"])).ToList();
            labels.Insert(0, "当前值 · " + Value(fields["routeIndex"]));
            var indexValue = WaveSchedule.Integer(fields["routeIndex"]);
            var popup = new PopupField<string>(library == "extraRoutes" ? "额外路线" : "主路线", labels, indices.IndexOf(indexValue ?? -1) + 1)
                { name = prefix + "-input-routeIndex" };
            Button? locate = null, preview = null;
            int? CurrentIndex() => WaveSchedule.Integer(fields["routeIndex"]) ??
                (fields["routeIndex"] == null && WaveSchedule.ActionType(fields["actionType"]) is "PLAY_OPERA" or "EMPTY" ? 0 : (int?)null);
            void RefreshButtons()
            {
                var available = WaveSchedule.HasRoute(routes, CurrentIndex());
                locate?.SetEnabled(available); preview?.SetEnabled(available);
            }
            popup.RegisterValueChangedCallback(_ => Run(() =>
            {
                if (popup.index > 0) Set("routeIndex", new JValue(indices[popup.index - 1]));
                RefreshButtons();
            }));
            card.Add(popup);
            var tools = Row(card);
            locate = Button(tools, "定位路线", () => locatePath("/" + library + "/" + CurrentIndex()));
            preview = Button(tools, "演示路线", () => showRoute(library, CurrentIndex()!.Value, true));
            RefreshButtons();
        }

        private void DrawTriggers(VisualElement card)
        {
            card.Add(Note("从候选中选择一个已在场别名，调用指定能力；省略能力名时使用 DefaultTrigger。"));
            var aliases = BranchSchedule.Aliases(document, "TRIGGER_PREDEFINED");
            var current = Text(fields["key"]);
            var entries = current.Length == 0 ? new List<string>() : current.Split('|').ToList();
            for (var i = 0; i < entries.Count; i++)
            {
                var index = i;
                var candidate = entries[i];
                var colon = candidate.IndexOf(':');
                var alias = colon < 0 ? candidate : candidate.Substring(0, colon);
                var ability = colon < 0 ? "" : candidate.Substring(colon + 1);
                var row = new VisualElement(); row.AddToClassList("wave-settings"); card.Add(row);
                var text = new TextField("别名") { isDelayed = true, value = alias, name = prefix + "-trigger-alias-" + i };
                var skill = new TextField("能力名") { isDelayed = true, value = ability, name = prefix + "-trigger-ability-" + i };
                void ApplyCandidate()
                {
                    var updated = entries.ToList(); updated[index] = text.value + (skill.value.Length == 0 ? "" : ":" + skill.value); Set("key", new JValue(string.Join("|", updated))); entries = updated;
                }
                text.RegisterValueChangedCallback(_ => Run(ApplyCandidate));
                skill.RegisterValueChangedCallback(_ => Run(ApplyCandidate));
                row.Add(text);
                var labels = aliases.Select(AliasLabel).ToList(); labels.Insert(0, "从预设选择别名");
                var select = new PopupField<string>(labels, 0);
                select.RegisterValueChangedCallback(_ => Run(() =>
                {
                    if (select.index <= 0) return;
                    text.SetValueWithoutNotify(aliases[select.index - 1].Alias);
                    ApplyCandidate();
                }));
                row.Add(select); row.Add(skill);
                Button(row, "删除候选", () =>
                {
                    var updated = entries.ToList(); updated.RemoveAt(index); Set("key", new JValue(string.Join("|", updated)), true);
                });
            }
            Button(card, "新增候选", () =>
            {
                var updated = entries.ToList(); updated.Add(aliases.FirstOrDefault()?.Alias ?? "new_alias"); Set("key", new JValue(string.Join("|", updated)), true);
            });
            card.Add(Note("能力名由实例挂载能力提供。卡牌需先部署，才成为场上目标。"));
        }

        private void Set(string key, JToken? value, bool redraw = false)
        {
            if (rawPending) throw new InvalidOperationException("请先读取原始 JSON。");
            var candidate = (JObject)fields.DeepClone();
            if (value == null) candidate.Remove(key); else candidate[key] = value.DeepClone();
            changed((JObject)candidate.DeepClone());
            fields = candidate;
            if (captions.TryGetValue(key, out var caption)) caption.text = key + " · " + Value(fields[key]);
            raw.SetValueWithoutNotify(fields.ToString(Formatting.Indented));
            if (redraw) Render(); else RefreshGroup();
        }

        private void RefreshGroup()
        {
            if (groupView == null) return;
            groupView.Clear();
            if (siblingActions == null) { groupView.Add(Note("未提供同级动作上下文。")); return; }
            var group = Text(fields["randomSpawnGroupKey"]);
            if (group.Length == 0) { groupView.Add(Note("当前动作未指定随机组。")); return; }
            var rows = (JArray)siblingActions.DeepClone();
            if (actionIndex >= 0 && actionIndex < rows.Count) rows[actionIndex] = fields.DeepClone();
            else if (actionIndex == rows.Count) rows.Add(fields.DeepClone());
            foreach (var row in rows.OfType<JObject>().Where(row => Text(row["randomSpawnGroupKey"]) == group))
                groupView.Add(Note("key · " + Value(row["key"]) + " | group · " + group + " | pack · " +
                    Value(row["randomSpawnGroupPackKey"]) + " | weight · " + Value(row["weight"])));
        }

        private string Pointer(string path)
        {
            var token = document.SelectToken(path);
            if (token == null) return path;
            var parts = new List<string>();
            while (token.Parent != null)
            {
                if (token.Parent is JProperty property)
                { parts.Add(property.Name.Replace("~", "~0").Replace("/", "~1")); token = property.Parent!; }
                else if (token.Parent is JArray array) { parts.Add(array.IndexOf(token).ToString(CultureInfo.InvariantCulture)); token = array; }
                else token = token.Parent;
            }
            parts.Reverse(); return "/" + string.Join("/", parts);
        }

        private static string AliasLabel(BranchAlias alias) => alias.Alias + " · " + alias.CharacterKey +
            " · " + alias.Layer + (alias.IsCard ? " · 卡牌" : " · 场上") + (alias.Hidden ? " · 隐藏" : "");
        private static string Text(JToken? token) => token?.Type == JTokenType.String ? token.Value<string>()! : "";
        private static string Value(JToken? token) => token?.ToString(Formatting.None) ?? "未提供";
        private static Label Note(string text) => new(text) { style = { whiteSpace = WhiteSpace.Normal } };
        private static VisualElement Row(VisualElement parent)
        { var row = new VisualElement(); row.AddToClassList("workbench-tabs"); parent.Add(row); return row; }
        private Button Button(VisualElement parent, string text, Action action)
        { var button = new Button(() => Run(action)) { text = text }; parent.Add(button); return button; }
        private void Run(Action action)
        {
            try { error.text = ""; action(); }
            catch (Exception issue) when (issue is FormatException || issue is JsonException || issue is InvalidOperationException)
            { error.text = issue.Message; }
            catch (Exception issue) { error.text = PluginLog.FailureMessage("document.action-form", issue); }
        }
    }

    // Existing branch integrations keep their control names while sharing the same implementation.
    internal sealed class BranchActionForm : ActionDataForm
    {
        internal BranchActionForm(JObject initial, JObject document, IReadOnlyList<BranchEnemyChoice> enemies,
            Action<JObject> changed, Action<string> locatePath, Action<int, bool> showRoute,
            Action<string>? rawChanged = null)
            : base(initial, document, enemies, changed, locatePath, (library, route, play) => { if (library == "extraRoutes") showRoute(route, play); else locatePath("/" + library + "/" + route); },
                true, rawChanged, true, null, "branch") { }
    }
}
