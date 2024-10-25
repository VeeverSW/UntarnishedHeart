using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Linq;
using System.Windows.Forms;
using System;
using Dalamud.Game.ClientState.Objects.Enums;
using UntarnishedHeart.Managers;
using UntarnishedHeart.Utils;
using System.Security.Policy;

namespace UntarnishedHeart.Executor;

public class Executor : IDisposable
{
    public uint            CurrentRound     { get; private set; }
    public int             MaxRound         { get; init; }
    public bool            AutoOpenTreasure { get; init; }
    public ExecutorPreset? ExecutorPreset   { get; init; }
    public string          RunningMessage   => TaskHelper?.CurrentTaskName ?? string.Empty;
    public bool            IsDisposed       { get; private set; }

    private TaskHelper? TaskHelper;

    public Executor(ExecutorPreset? preset, int maxRound = -1, bool autoOpenTreasure = false)
    {
        if (preset is not { IsValid: true }) return;

        TaskHelper ??= new() { TimeLimitMS = 30_000 };

        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "ContentsFinderConfirm", OnAddonSetup);

        DService.ClientState.TerritoryChanged += OnZoneChanged;

        DService.DutyState.DutyStarted += OnDutyStarted;
        DService.DutyState.DutyRecommenced += OnDutyStarted;
        DService.DutyState.DutyCompleted += OnDutyCompleted;

        MaxRound = maxRound;
        AutoOpenTreasure = autoOpenTreasure;
        ExecutorPreset = preset;
        
        OnDutyStarted(null, DService.ClientState.TerritoryType);
    }

    public void Dispose()
    {
        if (IsDisposed) return;

        DService.ClientState.TerritoryChanged -= OnZoneChanged;
        DService.DutyState.DutyCompleted -= OnDutyCompleted;
        DService.DutyState.DutyStarted -= OnDutyStarted;
        DService.DutyState.DutyRecommenced -= OnDutyStarted;
        DService.AddonLifecycle.UnregisterListener(OnAddonSetup);

        TaskHelper?.Abort();
        TaskHelper?.Dispose();
        TaskHelper = null;

        IsDisposed = true;
    }

    // 自动确认进入副本
    private static unsafe void OnAddonSetup(AddonEvent type, AddonArgs args)
    {
        if (args.Addon == nint.Zero) return;
        Callback(args.Addon.ToAtkUnitBase(), true, 8);
    }

    // 确认进入副本区域
    private void OnZoneChanged(ushort zone)
    {
        if (ExecutorPreset == null || zone != ExecutorPreset.Zone) return;
        AbortPrevious();
    }

    // 副本开始 / 重新挑战
    private void OnDutyStarted(object? sender, ushort zone)
    {
        AbortPrevious();
        if (ExecutorPreset == null || zone != ExecutorPreset.Zone) return;

        EnqueuePreset();
    }

    // 填装预设步骤
    private void EnqueuePreset()
    {
        TaskHelper.Enqueue(() => DService.ClientState.LocalPlayer != null, "等待区域加载结束");

        TaskHelper.Enqueue(() =>
        {
            if (!Throttler.Throttle("等待副本开始节流")) return false;
            return DService.DutyState.IsDutyStarted;
        }, "等待副本开始");

        foreach (var task in ExecutorPreset.GetTasks(TaskHelper, Service.Config.MoveType))
            task.Invoke();
    }

    // 副本完成
    private void OnDutyCompleted(object? sender, ushort zone)
    {
        AbortPrevious();
        if (ExecutorPreset == null || zone != ExecutorPreset.Zone) return;

        if (AutoOpenTreasure)
            EnqueueTreasureHunt();

        TaskHelper.Enqueue(() =>
        {
            GameFunctions.LeaveDuty();
            CurrentRound++;

            if (MaxRound != -1 && CurrentRound >= MaxRound)
            {
                Dispose();
                return;
            }

            if (!Service.Config.LeaderMode) return;
            EnqueueRegDuty();
        });
    }

    // 填装进入副本
    private void EnqueueRegDuty()
    {
        TaskHelper.Enqueue(() =>
        {
            if (!Throttler.Throttle("等待副本结束节流")) return false;
            return !DService.DutyState.IsDutyStarted && DService.ClientState.TerritoryType != ExecutorPreset.Zone;
        }, "等待副本结束");

        TaskHelper.Enqueue(
            () => DService.ClientState.LocalPlayer != null && !DService.Condition[ConditionFlag.BetweenAreas],
            "等待区域加载结束");

        TaskHelper.Enqueue(() =>
        {
            if (!Throttler.Throttle("进入副本节流")) return false;
            GameFunctions.RegisterToEnterDuty();
            return DService.Condition[ConditionFlag.WaitingForDutyFinder] || DService.Condition[ConditionFlag.WaitingForDuty];
        }, "等待进入下一局");
    }

    // 填装搜寻宝箱
    private unsafe void EnqueueTreasureHunt()
    {
        var localPlayer = DService.ClientState.LocalPlayer;
        var origPosition = localPlayer?.Position ?? default;

        TaskHelper.Enqueue(() =>
        {
            var treasures = DService.ObjectTable
                                    .Where(obj => obj.ObjectKind == ObjectKind.Treasure)
                                    .ToList();
            if (treasures.Count == 0) return false;

            foreach (var obj in treasures)
            {
                TaskHelper.Enqueue(() => GameFunctions.Teleport(obj.Position), "传送至宝箱", null, null, 2);
                TaskHelper.DelayNext(50, "等待位置确认", false, 2);
                TaskHelper.Enqueue(() =>
                {
                    if (!Throttler.Throttle("交互宝箱节流")) return false;
                    return TargetSystem.Instance()->InteractWithObject(obj.ToStruct(), false) != 0;
                }, "与宝箱交互", null, null, 2);
            }

            return true;
        }, "搜寻宝箱中");

        TaskHelper.Enqueue(() => GameFunctions.Teleport(origPosition), "传送回原始位置");
    }

    // 放弃先前任务
    private void AbortPrevious()
    {
        TaskHelper.Abort();
        GameFunctions.PathFindCancel();
    }
}