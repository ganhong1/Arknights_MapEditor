#nullable enable
using System;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        private void BuildScheduleJsonTools(VisualElement parent)
        {
            var fold = new Foldout { text = "旧版波次与分支 JSON 工具", value = false };
            var branch = new TextField("Branch key（空表示主波次）");
            var wave = new IntegerField("Wave 索引"); var part = new IntegerField("Fragment / Phase 索引");
            var action = new IntegerField("Action 索引"); var target = new IntegerField("重排目标索引");
            var json = new TextField("Action JSON") { multiline = true, value = "{\"actionType\":\"SPAWN\",\"key\":\"\",\"count\":1,\"preDelay\":0,\"interval\":1,\"routeIndex\":0}" };
            fold.Add(branch); fold.Add(wave); fold.Add(part); fold.Add(action); fold.Add(target); fold.Add(json);
            Func<ScheduleAddress> address = () => branch.value.Length == 0 ? ScheduleAddress.Main(wave.value,part.value) : ScheduleAddress.Branch(branch.value,part.value);
            Func<JArray> actions = () =>
            {
                var doc = session!.Document;
                var token = branch.value.Length == 0 ? doc.GetSection("waves")?[wave.value]?["fragments"]?[part.value]?["actions"] : doc.GetSection("branches")?[branch.value]?["phases"]?[part.value]?["actions"];
                return token as JArray ?? throw new FormatException("请先建立对应 Wave/Fragment 或 Branch/Phase。");
            };
            AddButton(fold,"新增主波次",() =>
            {
                var waves = session!.Document.GetSection("waves") as JArray ?? new JArray();
                waves.Add(new JObject { ["preDelay"] = 0, ["postDelay"] = 0, ["maxTimeWaitingForNextWave"] = -1, ["fragments"] = new JArray(new JObject { ["preDelay"] = 0, ["actions"] = new JArray() }) });
                CommitEdit(session.Document.WithSection("waves",waves.ToString()),"新增波次",null); wave.value = waves.Count-1; branch.value = ""; part.value=0;
            });
            AddButton(fold,"新增指定分支",() =>
            {
                if (branch.value.Length == 0) throw new FormatException("请输入 Branch key。");
                var branches = session!.Document.GetSection("branches") as JObject ?? new JObject();
                if (branches[branch.value] != null) throw new EditorOperationRejectedException("该分支已存在，拒绝覆盖。");
                branches[branch.value] = new JObject { ["phases"] = new JArray(new JObject { ["preDelay"] = 0, ["actions"] = new JArray() }) };
                CommitEdit(session.Document.WithSection("branches",branches.ToString()),"新增分支",null); part.value=0;
            });
            Func<string> actionTarget = () => new JArray(branch.value, wave.value, part.value, action.value).ToString(Newtonsoft.Json.Formatting.None);
            BindLocalTextField("action", json, actionTarget);
            AddButton(fold,"读取动作",() => ReloadLocalDraft("action", actionTarget(), actions()[action.value].ToString()));
            AddButton(fold,"新建动作草稿",() => ReloadLocalDraft("action", actionTarget(), "{\"actionType\":\"SPAWN\",\"key\":\"\",\"count\":1,\"preDelay\":0,\"interval\":1,\"routeIndex\":0}"));
            AddButton(fold,"插入动作",() => { RequireLocalDraft("action", actionTarget()); CommitEdit(session!.Document.InsertAction(address(),action.value,json.value),"插入动作",null); AppliedLocalDraft("action"); });
            AddButton(fold,"删除动作",() => CommitEdit(session!.Document.DeleteAction(address(),action.value),"删除动作",null));
            AddButton(fold,"移动动作",() => { CommitEdit(session!.Document.MoveAction(address(),action.value,target.value),"重排动作",null);action.value=target.value; });
            var diagnostics = new Label(); diagnostics.style.whiteSpace=WhiteSpace.Normal;
            AddButton(fold,"检查引用与字段",() =>
            {
                RequireAppliedDraft(); var text = new System.Text.StringBuilder();
                foreach (var item in session!.Document.ValidateReferences()) text.AppendLine(item.Severity+" "+item.Path+": "+item.Message);
                diagnostics.text = text.Length==0 ? "已知结构与引用未发现问题；不代表游戏运行验证。" : text.ToString();
            });
            fold.Add(diagnostics); parent.Add(fold);
        }
    }
}
