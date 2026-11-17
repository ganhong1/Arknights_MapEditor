#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        internal enum OperatorPlacementChoice { Scene, Card, Both, Cancel }

        [SerializeField] private bool devicePlacementEnabled;
        [SerializeField] private string devicePlacementDirection = "RIGHT";
        private Toggle? devicePlacementToggle;
        private Action? updateDevicePlacementAvailability;
        private Action<IReadOnlyList<Vector2Int>>? placeDeviceCells;
        internal Action<Action<OperatorPlacementChoice>> OperatorPlacementPrompt { get; set; } = ShowOperatorPlacementMenu;

        private static void ShowOperatorPlacementMenu(Action<OperatorPlacementChoice> resolve)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("仅创建场上预设"), false, () => resolve(OperatorPlacementChoice.Scene));
            menu.AddItem(new GUIContent("仅创建关卡卡牌"), false, () => resolve(OperatorPlacementChoice.Card));
            menu.AddItem(new GUIContent("两者都创建"), false, () => resolve(OperatorPlacementChoice.Both));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("取消"), false, () => resolve(OperatorPlacementChoice.Cancel));
            menu.ShowAsContext();
        }

        private void SetDevicePlacement(bool enabled)
        {
            devicePlacementEnabled = enabled;
            if (enabled) terrainPlacementEnabled = false;
            devicePlacementToggle?.SetValueWithoutNotify(enabled);
        }

        private void BuildDevicePlacement(VisualElement parent, TextField json, Func<string> target,
            PopupField<string> section, PopupField<string> role)
        {
            devicePlacementToggle = new Toggle("在地图放置") { name = "device-placement-enabled", value = devicePlacementEnabled };
            devicePlacementToggle.RegisterValueChangedCallback(change =>
            {
                if (change.newValue) SetEraseMode(false);
                SetDevicePlacement(change.newValue);
            });
            parent.Add(devicePlacementToggle);
            var directions = new List<string> { "UP", "RIGHT", "DOWN", "LEFT" };
            var direction = new PopupField<string>("放置朝向", directions, Math.Max(0, directions.IndexOf(devicePlacementDirection)))
                { name = "device-placement-direction" };
            direction.RegisterValueChangedCallback(change => devicePlacementDirection = change.newValue);
            parent.Add(direction);
            var instructions = new Label { name = "preset-placement-instructions" };
            instructions.style.whiteSpace = WhiteSpace.Normal;
            parent.Add(instructions);
            void UpdateAvailability()
            {
                var canPlace = IsInstanceRole(role.value);
                if (!canPlace) SetDevicePlacement(false);
                devicePlacementToggle.SetEnabled(canPlace);
                devicePlacementToggle.style.display = canPlace ? DisplayStyle.Flex : DisplayStyle.None;
                direction.SetEnabled(canPlace);
                direction.style.display = devicePlacementToggle.style.display;
                instructions.style.display = devicePlacementToggle.style.display;
                instructions.text = role.value == "characterInsts"
                    ? libraryOperatorDraft ? "点击地图格，选择创建场上预设、关卡卡牌或两者。" : "开启放置后，点击地图格添加一名干员。"
                    : "开启放置后，Shift + 左键点击或拖动可添加装置。";
            }
            updateDevicePlacementAvailability = UpdateAvailability;
            role.RegisterValueChangedCallback(_ => UpdateAvailability());
            UpdateAvailability();
            placeDeviceCells = cells =>
            {
                RequireWorkspaceEditable();
                if (!IsInstanceRole(role.value)) throw new EditorOperationRejectedException("请选择场上预设。");
                if (role.value == "characterInsts" && cells.Count > 1)
                    throw new EditorOperationRejectedException("请点击单个格子放置干员。");
                RequireLocalDraft("preset", target());
                if (libraryOperatorDraft && role.value == "characterInsts")
                {
                    if (cells.Count == 1) ChoosePlacement(cells[0]);
                    return;
                }
                var next = session!.Document.PlacePredefined(section.value, role.value, json.value, devicePlacementDirection,
                    cells.Select(cell => (cell.y, cell.x)));
                if (ReferenceEquals(next, session.Document)) return;
                CommitEdit(next, role.value == "characterInsts" ? "放置预设干员" : "放置装置", null);
                AppliedLocalDraft("preset");
                if (role.value == "characterInsts")
                {
                    SetDevicePlacement(false);
                    selectLastPlacedOperator?.Invoke("characterInsts");
                    return;
                }
                // Continue placing the same configuration against the newly committed
                // document. Unrelated edits still invalidate this binding normally.
                InspectorStore.Begin("preset", session.Json, target(), json.value);
                PersistInspectorDrafts();
            };

            void ChoosePlacement(Vector2Int cell)
            {
                var document = session!.Document;
                var layer = section.value;
                var pendingTarget = target();
                var raw = json.value;
                var facing = devicePlacementDirection;
                var resolved = false;
                OperatorPlacementPrompt(choice =>
                {
                    if (resolved) return;
                    resolved = true;
                    if (choice == OperatorPlacementChoice.Cancel) return;
                    try
                    {
                        RequireWorkspaceEditable();
                        if (!libraryOperatorDraft || !OperatorPlacementEnabled || session!.Document != document ||
                            (workspaceOwner != null && workspaceOwner.FocusedEditor != this) ||
                            target() != pendingTarget || json.value != raw || devicePlacementDirection != facing)
                            throw new EditorOperationRejectedException("放置配置已变化，请重新点击地图格。");
                        RequireLocalDraft("preset", pendingTarget);
                        var onMap = choice is OperatorPlacementChoice.Scene or OperatorPlacementChoice.Both;
                        var inHand = choice is OperatorPlacementChoice.Card or OperatorPlacementChoice.Both;
                        var next = document.CreateOperatorPresets(layer, raw, facing, (cell.y, cell.x), onMap, inHand);
                        CommitEdit(next, choice == OperatorPlacementChoice.Both ? "创建干员场上预设和卡牌" :
                            onMap ? "放置预设干员" : "添加干员卡牌", null);
                        AppliedLocalDraft("preset");
                        selectLastPlacedOperator?.Invoke(onMap ? "characterInsts" : "characterCards");
                    }
                    catch (Exception error)
                    {
                        if (status != null) status.text = error.Message;
                        if (workspaceOwner != null) workspaceOwner.ReportNativeError("scene.device-placement", error);
                        else ReportWindowFailure("scene.device-placement", error);
                    }
                });
            }
        }
    }
}
