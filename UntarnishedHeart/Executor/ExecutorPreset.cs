using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Utility;
using Dalamud.Interface;
using ImGuiNET;
using System.Collections.Generic;
using System;
using System.Linq;
using UntarnishedHeart.Windows;
using System.Text;
using Newtonsoft.Json;
using System.Windows.Forms;
using UntarnishedHeart.Utils;

namespace UntarnishedHeart.Executor;

public class ExecutorPreset : IEquatable<ExecutorPreset>
{
    public string                   Name  { get; set; } = string.Empty;
    public ushort                   Zone  { get; set; }
    public List<ExecutorPresetStep> Steps { get; set; } = [];

    public bool IsValid => Zone != 0 && Steps.Count > 0 && Main.ZonePlaceNames.ContainsKey(Zone);

    public void Draw()
    {
        var name = Name;
        if (ImGuiOm.CompLabelLeft(
                "名称:", 200f * ImGuiHelpers.GlobalScale,
                () => ImGui.InputText("###PresetNameInput", ref name, 128)))
            Name = name;

        var zone = (int)Zone;
        if (ImGuiOm.CompLabelLeft(
                "区域:", 200f * ImGuiHelpers.GlobalScale,
                () => ImGui.InputInt("###PresetZoneInput", ref zone, 0, 0)))
            Zone = (ushort)Math.Clamp(zone, 0, ushort.MaxValue);

        ImGui.SameLine();
        if (ImGuiOm.ButtonIcon("GetZone", FontAwesomeIcon.MapMarkedAlt, "取当前区域", true))
            Zone = DService.ClientState.TerritoryType;

        using (ImRaii.PushIndent())
        {
            var zoneName = Main.ZonePlaceNames.GetValueOrDefault(Zone, "未知区域");
            ImGui.SameLine();
            ImGui.Text($"({zoneName})");
        }

        ImGui.Dummy(new(8f));

        if (ImGuiOm.ButtonIconWithText(FontAwesomeIcon.Plus, "添加新步骤", true))
            Steps.Add(new());

        
        for (var i = 0; i < Steps.Count; i++)
        {
            var step = Steps[i];
            
            using var node = ImRaii.TreeNode($"第 {i + 1} 步: {step.Note}");
            if (!node) continue;
            
            var ret = step.Draw(i, Steps.Count);
            Action executorOperationAction = ret switch
            {
                ExecutorStepOperationType.DELETE   => () => Steps.RemoveAt(i),
                ExecutorStepOperationType.MOVEDOWN => () => Steps.Swap(i, i + 1),
                ExecutorStepOperationType.MOVEUP   => () => Steps.Swap(i, i - 1),
                ExecutorStepOperationType.COPY     => () => Steps.Insert(i  + 1, step.Copy()),
                ExecutorStepOperationType.PASS     => () => { },
                _                                  => () => { }
            };
            executorOperationAction();
        }
    }

    public List<Action> GetTasks(TaskHelper t, MoveType moveType)
        => Steps.SelectMany(x => x.GetTasks(t, moveType)).ToList();

    public override string ToString() => $"ExecutorPreset_{Name}_{Zone}_{Steps.Count}Steps";

    public bool Equals(ExecutorPreset? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Name == other.Name && Zone == other.Zone && Steps.SequenceEqual(other.Steps);
    }

    public void ExportToClipboard()
    {
        try
        {
            var json = JsonConvert.SerializeObject(this);
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            Clipboard.SetText(base64);
            NotifyHelper.NotificationSuccess("已成功导出预设至剪贴板");
        }
        catch (Exception)
        {
            NotifyHelper.NotificationError("尝试导出预设至剪贴板时发生错误");
        }
    }

    public static ExecutorPreset? ImportFromClipboard()
    {
        try
        {
            var base64 = Clipboard.GetText();
            if (!string.IsNullOrEmpty(base64))
            {
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));

                var config = JsonConvert.DeserializeObject<ExecutorPreset>(json);
                if (config != null)
                    NotifyHelper.NotificationSuccess("已成功从剪贴板导入预设");
                return config;
            }
        }
        catch (Exception)
        {
            NotifyHelper.NotificationError("尝试从剪贴板导入预设时发生错误");
        }
        return null;
    }

    public override bool Equals(object? obj) => Equals(obj as ExecutorPreset);

    public override int GetHashCode() => HashCode.Combine(Name, Zone, Steps);
}

