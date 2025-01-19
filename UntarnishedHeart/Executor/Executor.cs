using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using System.Linq;
using System;
using Dalamud.Game.ClientState.Objects.Enums;
using Lumina.Excel.GeneratedSheets;
using UntarnishedHeart.Managers;
using UntarnishedHeart.Utils;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;

namespace UntarnishedHeart.Executor;

public class Executor : IDisposable
{
    public uint            CurrentRound     { get; private set; }
    public int             MaxRound         { get; init; }
    public bool            AutoOpenTreasure { get; init; }
    public uint            LeaveDutyDelay   { get; init; }
    public ExecutorPreset? ExecutorPreset   { get; init; }
    public string          RunningMessage   => TaskHelper?.CurrentTaskName ?? string.Empty;
    public bool            IsDisposed       { get; private set; }

    public List<Vector3> TreasureRecordList = new List<Vector3>(); 
    public List<IGameObject> Treasures = new List<IGameObject>();

    private TaskHelper? TaskHelper;

    public Executor(ExecutorPreset? preset, int maxRound = -1, bool autoOpenTreasure = false, uint leaveDutyDelay = 0)
    {
        if (preset is not { IsValid: true }) return;

        TaskHelper ??= new() { TimeLimitMS = int.MaxValue };

        DService.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, "ContentsFinderConfirm", OnAddonDraw);

        DService.ClientState.TerritoryChanged += OnZoneChanged;

        DService.DutyState.DutyStarted += OnDutyStarted;
        DService.DutyState.DutyRecommenced += OnDutyStarted;
        DService.DutyState.DutyCompleted += OnDutyCompleted;

        MaxRound = maxRound;
        AutoOpenTreasure = autoOpenTreasure;
        LeaveDutyDelay = leaveDutyDelay;
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
        DService.AddonLifecycle.UnregisterListener(OnAddonDraw);

        TaskHelper?.Abort();
        TaskHelper?.Dispose();
        TaskHelper = null;

        IsDisposed = true;
    }

    // 自动确认进入副本
    private static unsafe void OnAddonDraw(AddonEvent type, AddonArgs args)
    {
        if (!Throttler.Throttle("自动确认进入副本节流")) return;

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
        {
            task.Invoke();
            TaskHelper.Enqueue(() =>
            {
            var treasures = DService.ObjectTable
                    .Where(obj => obj.ObjectKind == ObjectKind.Treasure)
                    .ToList();
            if (treasures.Count == 0) return;

                foreach (var obj in treasures)
                {
                    if (!TreasureRecordList.Contains(obj.Position))
                    {
                        Treasures.Add(obj);
                        TreasureRecordList.Add(obj.Position);
                        Chat($"add: obj:{obj}, pos: {obj.Position}");
                    }
                }
            });
        }
    }

    // 副本完成
    private void OnDutyCompleted(object? sender, ushort zone)
    {
        AbortPrevious();
        if (ExecutorPreset == null || zone != ExecutorPreset.Zone) return;

        if (AutoOpenTreasure)
            EnqueueTreasureHunt();

        if (LeaveDutyDelay > 0)
            TaskHelper.DelayNext((int)LeaveDutyDelay);

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
        var currentZoneType = DService.ClientState.TerritoryType;
        var contentFinderConditionSheet = LuminaCache.Get<ContentFinderCondition>();
        var setDelayTime = 50;
        var contentTypeTrial = 4;
        var contentTypeRaid = 5;
        
        if (contentFinderConditionSheet != null)
        {
            var contentFinderEntry = contentFinderConditionSheet.FirstOrDefault(entry => entry.TerritoryType.Row == currentZoneType);

            if (contentFinderEntry != null)
            {
                if (contentFinderEntry.ContentType.Row == contentTypeTrial || contentFinderEntry.ContentType.Row == contentTypeRaid)
                {
                    setDelayTime = 2300;
                }
            }
        }

        TaskHelper.Enqueue(() =>
        {
            //var treasures = DService.ObjectTable
            //                        .Where(obj => obj.ObjectKind == ObjectKind.Treasure)
            //                        .ToList();
            //if (treasures.Count == 0) return false;
            var inCount = 0;
            foreach (var obj in Treasures)
            {
                TaskHelper.Enqueue(() => GameFunctions.Teleport(obj.Position), "传送至宝箱", null, null, 2);
                TaskHelper.DelayNext(setDelayTime, "等待位置确认", false, 2);
                Chat($"count: {inCount}");
                //TaskHelper.Enqueue(() =>
                //{
                //    if (!Throttler.Throttle("交互宝箱节流")) return false;
                //    obj.Interact();
                //    return TargetSystem.Instance()->InteractWithObject(obj.ToStruct(), false) != 0;
                //}, "与宝箱交互", null, null, 2);
                TaskHelper.Enqueue(() => obj.Interact(), "交互宝箱节流", null, null, 2);
                TaskHelper.DelayNext(setDelayTime, "等待位置确认", false, 5000);
                inCount++;
            }

            return true;
        }, "搜寻宝箱中");

        TaskHelper.Enqueue(() => GameFunctions.Teleport(origPosition), "传送回原始位置");
        //TaskHelper.Enqueue(() =>
        //{
        //    Chat($"TreasureRecordListCount: {TreasureRecordList.Count}");
        //    foreach (var obj in Treasures)
        //    {
        //        TaskHelper.Enqueue(() => GameFunctions.Teleport(obj.Position), "传送至宝箱", null, null, 2);
        //        TaskHelper.DelayNext(setDelayTime, "等待位置确认", false, 2);
        //        TaskHelper.Enqueue(() =>
        //        {
        //            if (!Throttler.Throttle("交互宝箱节流")) return false;
        //            return TargetSystem.Instance()->InteractWithObject(obj.ToStruct(), false) != 0;

        //        }, "与宝箱交互", null, null, 2);

        //        TaskHelper.DelayNext(setDelayTime, "等待位置确认", false, 2000);
        //    }

        //    return true;
        //}, "搜寻宝箱中");

        //TaskHelper.Enqueue(() => GameFunctions.Teleport(origPosition), "传送回原始位置");
    }

    // 放弃先前任务
    private void AbortPrevious()
    {
        TaskHelper.Abort();
        GameFunctions.PathFindCancel();
    }
}
