﻿using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Glamourer.Automation;
using Glamourer.Designs;
using Glamourer.Designs.Special;
using Glamourer.Interop;
using Glamourer.Services;
using Glamourer.Unlocks;
using ImGuiNET;
using OtterGui;
using OtterGui.Log;
using OtterGui.Raii;
using OtterGui.Text;
using OtterGui.Widgets;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;
using Action = System.Action;

namespace Glamourer.Gui.Tabs.AutomationTab;

public class SetPanel(
    SetSelector _selector,
    AutoDesignManager _manager,
    JobService _jobs,
    ItemUnlockManager _itemUnlocks,
    SpecialDesignCombo _designCombo,
    CustomizeUnlockManager _customizeUnlocks,
    CustomizeService _customizations,
    IdentifierDrawer _identifierDrawer,
    Configuration _config,
    RandomRestrictionDrawer _randomDrawer)
{
    private readonly JobGroupCombo          _jobGroupCombo = new(_manager, _jobs, Glamourer.Log);
    private readonly HeaderDrawer.Button[] _rightButtons  = [new HeaderDrawer.IncognitoButton(_config.Ephemeral)];
    private          string?                _tempName;
    private          int                    _dragIndex = -1;

    private Action? _endAction;

    private AutoDesignSet Selection
        => _selector.Selection!;

    public void Draw()
    {
        using var group = ImRaii.Group();
        DrawHeader();
        DrawPanel();
    }

    private void DrawHeader()
        => HeaderDrawer.Draw(_selector.SelectionName, 0, ImGui.GetColorU32(ImGuiCol.FrameBg), [], _rightButtons);

    private void DrawPanel()
    {
        using var child = ImRaii.Child("##Panel", -Vector2.One, true);
        if (!child || !_selector.HasSelection)
            return;

        var spacing = ImGui.GetStyle().ItemInnerSpacing with { Y = ImGui.GetStyle().ItemSpacing.Y };

        using (ImUtf8.Group())
        {
            using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, spacing))
            {
                var enabled = Selection.Enabled;
                if (ImGui.Checkbox("##Enabled", ref enabled))
                    _manager.SetState(_selector.SelectionIndex, enabled);
                ImGuiUtil.LabeledHelpMarker("Enabled",
                    "Whether the designs in this set should be applied at all. Only one set can be enabled for a character at the same time.");
            }

            using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, spacing))
            {
                var useGame = _selector.Selection!.BaseState is AutoDesignSet.Base.Game;
                if (ImGui.Checkbox("##gameState", ref useGame))
                    _manager.ChangeBaseState(_selector.SelectionIndex, useGame ? AutoDesignSet.Base.Game : AutoDesignSet.Base.Current);
                ImGuiUtil.LabeledHelpMarker("Use Game State as Base",
                    "When this is enabled, the designs matching conditions will be applied successively on top of what your character is supposed to look like for the game. "
                  + "Otherwise, they will be applied on top of the characters actual current look using Glamourer.");
            }
        }

        ImGui.SameLine();
        using (ImUtf8.Group())
        {
            using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, spacing))
            {
                var editing = _config.ShowAutomationSetEditing;
                if (ImGui.Checkbox("##Show Editing", ref editing))
                {
                    _config.ShowAutomationSetEditing = editing;
                    _config.Save();
                }

                ImGuiUtil.LabeledHelpMarker("Show Editing",
                    "Show options to change the name or the associated character or NPC of this design set.");
            }

            using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, spacing))
            {
                var resetSettings = _selector.Selection!.ResetTemporarySettings;
                if (ImGui.Checkbox("##resetSettings", ref resetSettings))
                    _manager.ChangeResetSettings(_selector.SelectionIndex, resetSettings);

                ImGuiUtil.LabeledHelpMarker("Reset Temporary Settings",
                    "Always reset all temporary settings applied by Glamourer when this automation set is applied, regardless of active designs.");
            }
        }

        if (_config.ShowAutomationSetEditing)
        {
            ImGui.Dummy(Vector2.Zero);
            ImGui.Separator();
            ImGui.Dummy(Vector2.Zero);

            var name  = _tempName ?? Selection.Name;
            var flags = _selector.IncognitoMode ? ImGuiInputTextFlags.ReadOnly | ImGuiInputTextFlags.Password : ImGuiInputTextFlags.None;
            ImGui.SetNextItemWidth(330 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputText("Rename Set##Name", ref name, 128, flags))
                _tempName = name;

            if (ImGui.IsItemDeactivated())
            {
                _manager.Rename(_selector.SelectionIndex, name);
                _tempName = null;
            }

            DrawIdentifierSelection(_selector.SelectionIndex);
        }

        ImGui.Dummy(Vector2.Zero);
        ImGui.Separator();
        ImGui.Dummy(Vector2.Zero);
        DrawDesignTable();
        _randomDrawer.Draw();
    }


    private void DrawDesignTable()
    {
        var (numCheckboxes, numSpacing) = (_config.ShowAllAutomatedApplicationRules, _config.ShowUnlockedItemWarnings) switch
        {
            (true, true)   => (9, 14),
            (true, false)  => (7, 10),
            (false, true)  => (4, 4),
            (false, false) => (2, 0),
        };

        var requiredSizeOneLine = numCheckboxes * ImGui.GetFrameHeight()
          + (30 + 220 + numSpacing) * ImGuiHelpers.GlobalScale
          + 5 * ImGui.GetStyle().CellPadding.X
          + 150 * ImGuiHelpers.GlobalScale;

        var singleRow = ImGui.GetContentRegionAvail().X >= requiredSizeOneLine || numSpacing == 0;
        var numRows = (singleRow, _config.ShowUnlockedItemWarnings) switch
        {
            (true, true)   => 6,
            (true, false)  => 5,
            (false, true)  => 5,
            (false, false) => 4,
        };

        using var table = ImRaii.Table("SetTable", numRows, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY);
        if (!table)
            return;

        ImGui.TableSetupColumn("##del",   ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
        ImGui.TableSetupColumn("##Index", ImGuiTableColumnFlags.WidthFixed, 30 * ImGuiHelpers.GlobalScale);

        if (singleRow)
        {
            ImGui.TableSetupColumn("Design", ImGuiTableColumnFlags.WidthFixed, 220 * ImGuiHelpers.GlobalScale);
            if (_config.ShowAllAutomatedApplicationRules)
                ImGui.TableSetupColumn("Application", ImGuiTableColumnFlags.WidthFixed,
                    6 * ImGui.GetFrameHeight() + 10 * ImGuiHelpers.GlobalScale);
            else
                ImGui.TableSetupColumn("Use", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("Use").X);
        }
        else
        {
            ImGui.TableSetupColumn("Design / Job Restrictions", ImGuiTableColumnFlags.WidthFixed, 250 * ImGuiHelpers.GlobalScale);
            if (_config.ShowAllAutomatedApplicationRules)
                ImGui.TableSetupColumn("Application", ImGuiTableColumnFlags.WidthFixed,
                    3 * ImGui.GetFrameHeight() + 4 * ImGuiHelpers.GlobalScale);
            else
                ImGui.TableSetupColumn("Use", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("Use").X);
        }

        if (singleRow)
            ImGui.TableSetupColumn("Job Restrictions", ImGuiTableColumnFlags.WidthStretch);

        if (_config.ShowUnlockedItemWarnings)
            ImGui.TableSetupColumn(string.Empty, ImGuiTableColumnFlags.WidthFixed, 2 * ImGui.GetFrameHeight() + 4 * ImGuiHelpers.GlobalScale);

        ImGui.TableHeadersRow();
        foreach (var (design, idx) in Selection.Designs.WithIndex())
        {
            using var id = ImRaii.PushId(idx);
            ImGui.TableNextColumn();
            var keyValid = _config.DeleteDesignModifier.IsActive();
            var tt = keyValid
                ? "Remove this design from the set."
                : $"Remove this design from the set.\nHold {_config.DeleteDesignModifier} to remove.";

            if (ImGuiUtil.DrawDisabledButton(FontAwesomeIcon.Trash.ToIconString(), new Vector2(ImGui.GetFrameHeight()), tt, !keyValid, true))
                _endAction = () => _manager.DeleteDesign(Selection, idx);
            ImGui.TableNextColumn();
            ImGui.Selectable($"#{idx + 1:D2}");
            DrawDragDrop(Selection, idx);
            ImGui.TableNextColumn();
            DrawRandomEditing(Selection, design, idx);
            _designCombo.Draw(Selection, design, idx);
            DrawDragDrop(Selection, idx);
            if (singleRow)
            {
                ImGui.TableNextColumn();
                DrawApplicationTypeBoxes(Selection, design, idx, singleRow);
                ImGui.TableNextColumn();
                DrawConditions(design, idx);
            }
            else
            {
                DrawConditions(design, idx);
                ImGui.TableNextColumn();
                DrawApplicationTypeBoxes(Selection, design, idx, singleRow);
            }

            if (_config.ShowUnlockedItemWarnings)
            {
                ImGui.TableNextColumn();
                DrawWarnings(design);
            }
        }

        ImGui.TableNextColumn();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("New");
        ImGui.TableNextColumn();
        _designCombo.Draw(Selection, null, -1);
        ImGui.TableNextRow();

        _endAction?.Invoke();
        _endAction = null;
    }

    private int _tmpGearset = int.MaxValue;
    private int _whichIndex = -1;

    private void DrawConditions(AutoDesign design, int idx)
    {
        var usingGearset = design.GearsetIndex >= 0;
        if (ImGui.Button($"{(usingGearset ? "Gearset:" : "Jobs:")}##usingGearset"))
        {
            usingGearset = !usingGearset;
            _manager.ChangeGearsetCondition(Selection, idx, (short)(usingGearset ? 0 : -1));
        }

        ImGuiUtil.HoverTooltip("Click to switch between Job and Gearset restrictions.");

        ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
        if (usingGearset)
        {
            var set = 1 + (_tmpGearset == int.MaxValue || _whichIndex != idx ? design.GearsetIndex : _tmpGearset);
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.InputInt("##whichGearset", ref set, 0, 0))
            {
                _whichIndex = idx;
                _tmpGearset = Math.Clamp(set, 1, 100);
            }

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                _manager.ChangeGearsetCondition(Selection, idx, (short)(_tmpGearset - 1));
                _tmpGearset = int.MaxValue;
                _whichIndex = -1;
            }
        }
        else
        {
            _jobGroupCombo.Draw(Selection, design, idx);
        }
    }

    private void DrawRandomEditing(AutoDesignSet set, AutoDesign design, int designIdx)
    {
        if (design.Design is not RandomDesign)
            return;

        _randomDrawer.DrawButton(set, designIdx);
        ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
    }

    private void DrawWarnings(AutoDesign design)
    {
        if (design.Design is not DesignBase)
            return;

        var size = new Vector2(ImGui.GetFrameHeight());
        size.X += ImGuiHelpers.GlobalScale;

        var collection = design.ApplyWhat();
        var sb         = new StringBuilder();
        var designData = design.Design.GetDesignData(default);
        foreach (var slot in EquipSlotExtensions.EqdpSlots.Append(EquipSlot.MainHand).Append(EquipSlot.OffHand))
        {
            var flag = slot.ToFlag();
            if (!collection.Equip.HasFlag(flag))
                continue;

            var item = designData.Item(slot);
            if (!_itemUnlocks.IsUnlocked(item.Id, out _))
                sb.AppendLine($"{item.Name} in {slot.ToName()} slot is not unlocked. Consider obtaining it via gameplay means!");
        }

        using var style = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(2 * ImGuiHelpers.GlobalScale, 0));

        var tt = _config.UnlockedItemMode
            ? "\nThese items will be skipped when applied automatically.\n\nTo change this, disable the Obtained Item Mode setting."
            : string.Empty;
        DrawWarning(sb, _config.UnlockedItemMode ? 0xA03030F0 : 0x0, size, tt, "All equipment to be applied is unlocked.");

        sb.Clear();
        var sb2       = new StringBuilder();
        var customize = designData.Customize;
        if (!designData.IsHuman)
            sb.AppendLine("The base model id can not be changed automatically to something non-human.");

        var set = _customizations.Manager.GetSet(customize.Clan, customize.Gender);
        foreach (var type in CustomizationExtensions.All)
        {
            var flag = type.ToFlag();
            if (!collection.Customize.HasFlag(flag))
                continue;

            if (flag.RequiresRedraw())
                sb.AppendLine($"{type.ToDefaultName()} Customization should not be changed automatically.");
            else if (type is CustomizeIndex.Hairstyle or CustomizeIndex.FacePaint
                  && set.DataByValue(type, customize[type], out var data, customize.Face) >= 0
                  && !_customizeUnlocks.IsUnlocked(data!.Value, out _))
                sb2.AppendLine(
                    $"{type.ToDefaultName()} Customization {_customizeUnlocks.Unlockable[data.Value].Name} is not unlocked but should be applied.");
        }

        ImGui.SameLine();
        tt = _config.UnlockedItemMode
            ? "\nThese customizations will be skipped when applied automatically.\n\nTo change this, disable the Obtained Item Mode setting."
            : string.Empty;
        DrawWarning(sb2, _config.UnlockedItemMode ? 0xA03030F0 : 0x0, size, tt, "All customizations to be applied are unlocked.");
        ImGui.SameLine();
        return;

        static void DrawWarning(StringBuilder sb, uint color, Vector2 size, string suffix, string good)
        {
            using var style = ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, ImGuiHelpers.GlobalScale);
            if (sb.Length > 0)
            {
                sb.Append(suffix);
                using (_ = ImRaii.PushFont(UiBuilder.IconFont))
                {
                    ImGuiUtil.DrawTextButton(FontAwesomeIcon.ExclamationCircle.ToIconString(), size, color);
                }

                ImGuiUtil.HoverTooltip(sb.ToString());
            }
            else
            {
                ImGuiUtil.DrawTextButton(string.Empty, size, 0);
                ImGuiUtil.HoverTooltip(good);
            }
        }
    }

    private void DrawDragDrop(AutoDesignSet set, int index)
    {
        const string dragDropLabel = "DesignDragDrop";
        using (var target = ImRaii.DragDropTarget())
        {
            if (target.Success && ImGuiUtil.IsDropping(dragDropLabel))
            {
                if (_dragIndex >= 0)
                {
                    var idx = _dragIndex;
                    _endAction = () => _manager.MoveDesign(set, idx, index);
                }

                _dragIndex = -1;
            }
        }

        using (var source = ImRaii.DragDropSource())
        {
            if (source)
            {
                ImGui.TextUnformatted($"Moving design #{index + 1:D2}...");
                if (ImGui.SetDragDropPayload(dragDropLabel, nint.Zero, 0))
                {
                    _dragIndex                 = index;
                    _selector._dragDesignIndex = index;
                }
            }
        }
    }

    private void DrawApplicationTypeBoxes(AutoDesignSet set, AutoDesign design, int autoDesignIndex, bool singleLine)
    {
        using var style      = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(2 * ImGuiHelpers.GlobalScale));
        var       newType    = design.Type;
        var       newTypeInt = (uint)newType;
        style.Push(ImGuiStyleVar.FrameBorderSize, ImGuiHelpers.GlobalScale);
        using (_ = ImRaii.PushColor(ImGuiCol.Border, ColorId.FolderLine.Value()))
        {
            if (ImGui.CheckboxFlags("##all", ref newTypeInt, (uint)ApplicationType.All))
                newType = (ApplicationType)newTypeInt;
        }

        style.Pop();
        ImGuiUtil.HoverTooltip("Toggle all application modes at once.");
        if (_config.ShowAllAutomatedApplicationRules)
        {
            void Box(int idx)
            {
                var (type, description) = ApplicationTypeExtensions.Types[idx];
                var value = design.Type.HasFlag(type);
                if (ImGui.Checkbox($"##{(byte)type}", ref value))
                    newType = value ? newType | type : newType & ~type;
                ImGuiUtil.HoverTooltip(description);
            }

            ImGui.SameLine();
            Box(0);
            ImGui.SameLine();
            Box(1);
            if (singleLine)
                ImGui.SameLine();

            Box(2);
            ImGui.SameLine();
            Box(3);
            ImGui.SameLine();
            Box(4);
        }

        _manager.ChangeApplicationType(set, autoDesignIndex, newType);
    }

    private void DrawIdentifierSelection(int setIndex)
    {
        using var id = ImUtf8.PushId("Identifiers"u8);
        _identifierDrawer.DrawWorld(130);
        ImGui.SameLine();
        _identifierDrawer.DrawName(200 - ImGui.GetStyle().ItemSpacing.X);
        _identifierDrawer.DrawNpcs(330);
        var buttonWidth = new Vector2(165 * ImGuiHelpers.GlobalScale - ImGui.GetStyle().ItemSpacing.X / 2, 0);
        if (ImUtf8.ButtonEx("Set to Character"u8, string.Empty, buttonWidth, !_identifierDrawer.CanSetPlayer))
            _manager.ChangeIdentifier(setIndex, _identifierDrawer.PlayerIdentifier);
        ImGui.SameLine();
        if (ImUtf8.ButtonEx("Set to NPC"u8, string.Empty, buttonWidth, !_identifierDrawer.CanSetNpc))
            _manager.ChangeIdentifier(setIndex, _identifierDrawer.NpcIdentifier);

        if (ImUtf8.ButtonEx("Set to Retainer"u8, string.Empty, buttonWidth, !_identifierDrawer.CanSetRetainer))
            _manager.ChangeIdentifier(setIndex, _identifierDrawer.RetainerIdentifier);
        ImGui.SameLine();
        if (ImUtf8.ButtonEx("Set to Mannequin"u8, string.Empty, buttonWidth, !_identifierDrawer.CanSetRetainer))
            _manager.ChangeIdentifier(setIndex, _identifierDrawer.MannequinIdentifier);

        if (ImUtf8.ButtonEx("Set to Owned NPC"u8, string.Empty, buttonWidth, !_identifierDrawer.CanSetOwned))
            _manager.ChangeIdentifier(setIndex, _identifierDrawer.OwnedIdentifier);
    }

    private sealed class JobGroupCombo(AutoDesignManager manager, JobService jobs, Logger log)
        : FilterComboCache<JobGroup>(() => jobs.JobGroups.Values.ToList(), MouseWheelType.None, log)
    {
        public void Draw(AutoDesignSet set, AutoDesign design, int autoDesignIndex)
        {
            CurrentSelection    = design.Jobs;
            CurrentSelectionIdx = jobs.JobGroups.Values.IndexOf(j => j.Id == design.Jobs.Id);
            if (Draw("##JobGroups", design.Jobs.Name,
                    "Select for which job groups this design should be applied.\nControl + Right-Click to set to all classes.",
                    ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeightWithSpacing())
             && CurrentSelectionIdx >= 0)
                manager.ChangeJobCondition(set, autoDesignIndex, CurrentSelection);
            else if (ImGui.GetIO().KeyCtrl && ImGui.IsItemClicked(ImGuiMouseButton.Right))
                manager.ChangeJobCondition(set, autoDesignIndex, jobs.JobGroups[1]);
        }

        protected override string ToString(JobGroup obj)
            => obj.Name;
    }
}
