#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Core
{
    internal enum BranchEdit { Insert, Copy, Move, Delete }

    internal sealed class BranchReference
    {
        internal string Path = "", Kind = "";
        internal bool CanRewrite;
        internal int? ActionIndex;
    }

    internal sealed class BranchAlias
    {
        internal string Alias = "", Path = "", CharacterKey = "", Layer = "";
        internal bool IsCard, Hidden;
    }

    /// <summary>Branch operations preserve the imported JSON and edit one cloned document.</summary>
    internal static class BranchSchedule
    {
        internal static JObject NewBranch() => new() { ["phases"] = new JArray(NewPhase()) };
        internal static JObject NewPhase() => new() { ["preDelay"] = 0f, ["actions"] = new JArray() };
        internal static JObject NewAction(string type = "SPAWN") => ActionDataRules.NewAction(type);

        internal static void RequireNewAction(JObject document, JObject action, IReadOnlyCollection<string> enemyKeys) =>
            ActionDataRules.Require(document, action, null, enemyKeys, true);
        internal static JObject AddBranch(JObject document, string key, string? copyFrom = null)
        {
            RequireKey(key);
            var copy = (JObject)document.DeepClone();
            if (copy["branches"] == null || copy["branches"]!.Type == JTokenType.Null) copy["branches"] = new JObject();
            var branches = RequiredBranches(copy);
            if (branches.Property(key) != null) throw new FormatException("分支键已存在：" + key);
            branches.Add(key, copyFrom == null ? NewBranch() : Branch(copy, copyFrom).DeepClone());
            return copy;
        }

        internal static JObject RenameBranch(JObject document, string oldKey, string newKey)
        {
            RequireKey(newKey);
            var copy = (JObject)document.DeepClone();
            var branches = RequiredBranches(copy);
            var property = branches.Property(oldKey) ?? throw new FormatException("找不到分支：" + oldKey);
            if (oldKey == newKey) return copy;
            if (branches.Property(newKey) != null) throw new FormatException("分支键已存在：" + newKey);
            var references = References(copy, oldKey);
            var uncertain = references.Where(reference => !reference.CanRewrite).ToArray();
            if (uncertain.Length > 0) throw new FormatException("以下调用需先调整分支键：\n" + string.Join("\n", uncertain.Select(item => item.Path)));
            foreach (var token in MatchingStrings(copy, oldKey).ToArray()) token.Value = newKey;
            property.Replace(new JProperty(newKey, property.Value.DeepClone()));
            return copy;
        }

        internal static JObject DeleteBranch(JObject document, string key)
        {
            var references = References(document, key);
            if (references.Count > 0) throw new FormatException("分支仍被引用：\n" + string.Join("\n", references.Select(item => item.Path)));
            var copy = (JObject)document.DeepClone();
            if (!RequiredBranches(copy).Remove(key)) throw new FormatException("找不到分支：" + key);
            return copy;
        }

        internal static JObject ChangeNode(JObject document, string key, int? phase, int index,
            BranchEdit edit, int to = -1, JObject? value = null)
        {
            var copy = (JObject)document.DeepClone();
            var phases = RequiredArray(Branch(copy, key)["phases"], "phases");
            var rows = phases;
            if (phase is int phaseIndex)
            {
                CheckIndex(phaseIndex, phases.Count);
                rows = RequiredArray((phases[phaseIndex] as JObject)?["actions"], "actions");
            }
            if (edit == BranchEdit.Insert)
            {
                if (index < 0 || index > rows.Count) throw new ArgumentOutOfRangeException(nameof(index));
            }
            else CheckIndex(index, rows.Count);
            if (edit == BranchEdit.Move) CheckIndex(to, rows.Count);
            if (edit == BranchEdit.Move && index == to) return copy;
            // A caller's action_index applies to each selected phase, not solely the phase being edited.
            // Rewriting it for one phase would silently change the other phases' selected records.
            if (phase.HasValue)
            {
                var indexed = References(copy, key).Where(reference => reference.ActionIndex.HasValue &&
                    Affected(reference.ActionIndex.Value, index, to, edit)).ToArray();
                if (indexed.Length > 0) throw new FormatException("操作会改变调用者 action_index 的目标：\n" + string.Join("\n", indexed.Select(item => item.Path)));
            }
            switch (edit)
            {
                case BranchEdit.Insert: rows.Insert(index, value?.DeepClone() ?? (phase.HasValue ? NewAction() : NewPhase())); break;
                case BranchEdit.Copy: rows.Insert(index + 1, rows[index].DeepClone()); break;
                case BranchEdit.Delete: rows.RemoveAt(index); break;
                case BranchEdit.Move:
                    var moved = rows[index]; rows.RemoveAt(index); rows.Insert(to, moved); break;
                default: throw new ArgumentOutOfRangeException(nameof(edit));
            }
            return copy;
        }

        private static bool Affected(int target, int from, int to, BranchEdit edit) => target < 0 || (edit switch
        {
            BranchEdit.Insert => target >= from,
            BranchEdit.Copy => target > from,
            BranchEdit.Delete => target >= from,
            BranchEdit.Move => target >= Math.Min(from, to) && target <= Math.Max(from, to),
            _ => true
        });

        internal static JObject ApplyFields(JObject document, string key, int[] address, JObject fields,
            IReadOnlyCollection<string> enemyKeys)
        {
            var copy = (JObject)document.DeepClone();
            var record = Read(copy, key, address);
            var previous = (JObject)record.DeepClone();
            var childField = address.Length == 0 ? "phases" : address.Length == 1 ? "actions" : null;
            if (childField != null && fields.Property(childField) != null)
                throw new FormatException("请在子项列表中编辑 " + childField + "。");
            if (address.Length == 1) WaveSchedule.RequireWaveFields(fields, previous, 2);
            if (address.Length == 2)
            {
                RequireAction(copy, fields, previous, enemyKeys);
                RequireIndexedActionType(copy, key, address[1], fields, previous);
            }
            foreach (var property in record.Properties().Where(prop => prop.Name != childField).ToArray())
            {
                if (fields.TryGetValue(property.Name, out var replacement)) property.Value = replacement.DeepClone();
                else property.Remove();
            }
            foreach (var property in fields.Properties())
                if (record.Property(property.Name) == null) record.Add(property.Name, property.Value.DeepClone());
            return copy;
        }

        private static void RequireIndexedActionType(JObject document, string key, int index, JObject fields, JObject previous)
        {
            var type = WaveSchedule.ActionType(fields["actionType"]);
            if (type == WaveSchedule.ActionType(previous["actionType"]) || type is "SPAWN" or "EMPTY") return;
            var callers = References(document, key).Where(reference => reference.ActionIndex == index).ToArray();
            if (callers.Length > 0)
                throw new FormatException("调用者 action_index 需要 SPAWN / EMPTY 记录：\n" +
                    string.Join("\n", callers.Select(reference => reference.Path)));
        }

        internal static JObject Read(JObject document, string key, int[] address)
        {
            if (address.Length > 2) throw new ArgumentException("分支地址最多包含阶段和动作索引。", nameof(address));
            var current = Branch(document, key);
            for (var depth = 0; depth < address.Length; depth++)
            {
                var array = RequiredArray(current[depth == 0 ? "phases" : "actions"], "分支子项");
                CheckIndex(address[depth], array.Count);
                current = array[address[depth]] as JObject ?? throw new FormatException("分支子项必须是对象。");
            }
            return current;
        }

        internal static IReadOnlyList<BranchReference> References(JObject document, string key)
        {
            var result = new List<BranchReference>();
            foreach (var value in MatchingStrings(document, key))
            {
                var item = value.Parent?.Parent as JObject;
                var blackboardKey = Text(item?["key"]);
                var isBranchKey = blackboardKey != null && blackboardKey.EndsWith("branch_id", StringComparison.Ordinal) &&
                    value.Parent is JProperty field && field.Name == "valueStr";
                var owner = item?.Ancestors().OfType<JObject>().FirstOrDefault(record => record["inst"] is JObject);
                var isArms = blackboardKey == "branch_id" && isBranchKey && Text((owner?["inst"] as JObject)?["characterKey"]) == "trap_076_bgarms" &&
                    item?.Parent is JArray skillBoard && skillBoard.Parent is JProperty boardField &&
                    boardField.Name == "overrideSkillBlackboard" && ReferenceEquals(boardField.Parent, owner);
                var actionIndex = isBranchKey && item?.Parent is JArray board
                    ? board.OfType<JObject>().Where(row => Text(row["key"]) == blackboardKey!.Substring(0, blackboardKey.Length - "branch_id".Length) + "action_index")
                        .Select(row => ReadBlackboardIndex(row["value"]) ?? -1).Cast<int?>().FirstOrDefault() : null;
                result.Add(new BranchReference
                {
                    Path = value.Path, Kind = isArms ? "装置技能读取分支敌人与额外路线" : isBranchKey ? "黑板分支引用，调用者待核实" : "同值字符串，关联待核实",
                    CanRewrite = isArms, ActionIndex = actionIndex
                });
            }
            // PC 2.7.81 SummonBallis → MoveNextLevelBranch reads this key from the enemy skill asset.
            if (key == "faust_ballis" && document["enemyDbRefs"] is JArray enemies && enemies.OfType<JObject>()
                .Any(enemy => Text(enemy["id"]) == "enemy_1508_faust" && enemy["useDb"]?.Type != JTokenType.Null &&
                    (enemy["useDb"] == null || enemy["useDb"]!.Type == JTokenType.Boolean && enemy["useDb"]!.Value<bool>())))
                result.Add(new BranchReference { Path = "enemy_1508_faust / SummonBallis / branch_id", Kind = "外部敌人技能 · 顺序推进阶段" });
            return result;
        }

        internal static IReadOnlyList<BranchAlias> Aliases(JObject document, string actionType)
        {
            var result = new List<BranchAlias>();
            foreach (var layer in new[] { "predefines", "hardPredefines" })
            {
                if (document[layer] is not JObject predefined) continue;
                foreach (var field in new[] { "characterInsts", "tokenInsts", "characterCards", "tokenCards" })
                {
                    var card = field.EndsWith("Cards", StringComparison.Ordinal);
                    if (actionType == "WITHDRAW_PREDEFINED" && card || predefined[field] is not JArray records) continue;
                    foreach (var record in records.OfType<JObject>())
                    {
                        var character = Text((record["inst"] as JObject)?["characterKey"]);
                        var alias = LevelDocument.EffectivePredefinedAlias(record);
                        if (string.IsNullOrEmpty(alias)) continue;
                        result.Add(new BranchAlias { Alias = alias!, CharacterKey = character ?? "", Path = record.Path,
                            IsCard = card, Hidden = record["hidden"]?.Type == JTokenType.Boolean && record["hidden"]!.Value<bool>(), Layer = layer });
                    }
                }
            }
            return result;
        }

        internal static IReadOnlyList<string> Validate(JObject document, IReadOnlyCollection<string> enemyKeys)
        {
            var result = new List<string>();
            var branches = document["branches"] as JObject;
            if (branches == null)
            {
                if (document["branches"] != null && document["branches"]!.Type != JTokenType.Null)
                { result.Add("branches 必须是对象。"); return result; }
                branches = new JObject();
            }
            foreach (var property in branches.Properties())
            {
                if (property.Value is not JObject branch || branch["phases"] is not JArray phases)
                { result.Add(property.Path + ".phases 必须是数组。"); continue; }
                foreach (var phase in phases)
                {
                    if (phase is not JObject obj || obj["actions"] is not JArray actions)
                    { result.Add(phase.Path + ".actions 必须是数组。"); continue; }
                    if (WaveSchedule.Time(obj["preDelay"]) == null) result.Add(phase.Path + ".preDelay 需要有效秒数。 ");
                    foreach (var action in actions)
                    {
                        if (action is not JObject record) { result.Add(action.Path + " 必须是对象。"); continue; }
                        try { RequireAction(document, record, new JObject(), enemyKeys); }
                        catch (FormatException error) { result.Add(action.Path + " · " + error.Message); }
                    }
                }
                foreach (var reference in References(document, property.Name).Where(item => item.ActionIndex.HasValue))
                {
                    var actionIndex = reference.ActionIndex!.Value;
                    if (actionIndex < 0 || phases.Count == 0 || phases.OfType<JObject>().Any(phase =>
                        phase["actions"] is not JArray actions || actionIndex >= actions.Count ||
                        actions[actionIndex] is not JObject action ||
                        WaveSchedule.ActionType(action["actionType"]) is not ("SPAWN" or "EMPTY")))
                        result.Add(reference.Path + " · action_index 未指向各阶段中的 SPAWN / EMPTY 记录。");
                }
            }
            foreach (var boardEntry in document.Descendants().OfType<JObject>().Where(item =>
                Text(item["key"])?.EndsWith("branch_id", StringComparison.Ordinal) == true))
            {
                var branchKey = Text(boardEntry["valueStr"]);
                if (!string.IsNullOrEmpty(branchKey) && branches.Property(branchKey!) == null)
                    result.Add(boardEntry.Path + ".valueStr · 未找到本关分支：" + branchKey);
            }
            return result;
        }

        private static void RequireAction(JObject document, JObject action, JObject previous, IReadOnlyCollection<string> enemyKeys) =>
            ActionDataRules.Require(document, action, previous.Count == 0 ? null : previous, enemyKeys, true);
        private static IEnumerable<JValue> MatchingStrings(JObject document, string key) => document.Descendants()
            .OfType<JValue>().Where(value => value.Type == JTokenType.String && value.Value<string>() == key);
        private static int? ReadBlackboardIndex(JToken? value)
        {
            if (value == null || WaveSchedule.Time(value) is not double index || index > int.MaxValue || Math.Truncate(index) != index)
                return null;
            if (value.Type == JTokenType.Raw && !LevelDocument.IsIntegralNumber(value)) return null;
            return (int)index;
        }
        private static string? Text(JToken? token) => token?.Type == JTokenType.String ? token.Value<string>() : null;
        private static JObject RequiredBranches(JObject document) => document["branches"] as JObject ?? throw new FormatException("branches 必须是对象。");
        private static JObject Branch(JObject document, string key) => RequiredBranches(document)[key] as JObject ?? throw new FormatException("找不到分支：" + key);
        private static JArray RequiredArray(JToken? token, string label) => token as JArray ?? throw new FormatException(label + " 必须是数组。");
        private static void RequireKey(string key) { if (string.IsNullOrWhiteSpace(key)) throw new FormatException("请输入分支键。"); }
        private static void CheckIndex(int index, int count) { if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index)); }
    }
}
