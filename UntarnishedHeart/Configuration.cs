using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using UntarnishedHeart.Executor;

namespace UntarnishedHeart;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int                  Version          { get; set; } = 0;
    public MoveType             MoveType         { get; set; } = MoveType.传送;
    public bool                 LeaderMode       { get; set; }
    public bool                 AutoOpenTreasure { get; set; }
    public uint                 LeaveDutyDelay   { get; set; }
    public int                  RunTimes         { get; set; } = -1;
    public List<ExecutorPreset> Presets          { get; set; } = [];


    public static readonly ExecutorPreset ExamplePreset0 = new()
    {
        Name = "O5 魔列车", Zone = 748, Steps = [new() { DataID = 8510, Note = "魔列车", Position = new(0, 0, -15) }]
    };

    public static readonly ExecutorPreset ExamplePreset1 = new()
    {
        Name = "假火 (测试用)", Zone = 1045, Steps = [new() { DataID = 207, Note = "伊弗利特", Position = new(11, 0, 0) }]
    };
    
    public static readonly ExecutorPreset ExamplePreset2 = new()
    {
        Name = "极风 (测试用)", Zone = 297, Steps = [new() { DataID = 245, Note = "Note1", Position = new (-0.24348414f, -1.9395045f, -14.213441f), Delay = 8000, StopInCombat = false},
                                                   new() { DataID = 245, Note = "Note2", Position = new(-0.63603175f, -1.8021163f, 0.6449276f), Delay = 5000, StopInCombat = false}]
    };

    public static readonly ExecutorPreset ExamplePreset3 = new()
    {
        Name = "黄金谷四人本 (测试用)", Zone = 172, Steps = [new() { DataID = 1471, Note = "Boss1", Position = new (31.613f, -9.256f, 3.126f), Delay = 5000, StopInCombat = true},
                                                   new() { DataID = 245, Note = "Boss1后魔界花", Position = new(-37.391f, -17.294f, -94.228f), Delay = 5000, StopInCombat = true},
                                                   new() { DataID = 1470, Note = "Boss2", Position = new(-166.161f, -29.802f, -141.621f), Delay = 5000, StopInCombat = true },
                                                   new() { Note = "动画", Position = new(-373.036f, -32.154f, -137.851f), Delay = 5000, StopInCombat = true },
                                                   new() { DataID = 943, Note = "Boss3", Position = new(-410.875f, -33.293f, -126.703f), Delay = 5000, StopInCombat = true }]
    };

    public void Init()
    {
        if (Presets.Count < 3)
        {
            Presets.Clear();
            Presets.Add(ExamplePreset0);
            Presets.Add(ExamplePreset1);
            Presets.Add(ExamplePreset2);
            Presets.Add(ExamplePreset3);
            Save();
        }
    }

    public void Save()
    {
        DService.PI.SavePluginConfig(this);
    }

    public void Uninit()
    {
        Save();
    }
}
