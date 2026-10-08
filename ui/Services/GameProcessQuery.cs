using System;
using System.Diagnostics;
using MystiaModManager.Logic;

namespace MystiaModManager.Services;

internal static class GameProcessQuery
{
    public static bool AnyRunning()
    {
        var list = Process.GetProcessesByName(LaunchState.ProcessName);
        try
        {
            return list.Length > 0;
        }
        finally
        {
            Dispose(list);
        }
    }

    public static int[] Ids()
    {
        var list = Process.GetProcessesByName(LaunchState.ProcessName);
        try
        {
            var ranked = new ProcessStart[list.Length];
            for (var i = 0; i < list.Length; i++)
            {
                DateTime started;
                try { started = list[i].StartTime; }
                catch (Exception) { started = DateTime.MaxValue; }
                ranked[i] = new ProcessStart(list[i].Id, started);
            }
            Array.Sort(ranked, (a, b) => a.Started.CompareTo(b.Started));
            var ids = new int[ranked.Length];
            for (var i = 0; i < ranked.Length; i++)
                ids[i] = ranked[i].Id;
            return ids;
        }
        finally
        {
            Dispose(list);
        }
    }

    private static void Dispose(Process[] list)
    {
        for (var i = 0; i < list.Length; i++)
            list[i].Dispose();
    }

    private readonly struct ProcessStart
    {
        public ProcessStart(int id, DateTime started)
        {
            Id = id;
            Started = started;
        }

        public int Id { get; }
        public DateTime Started { get; }
    }
}
