using System;
using System.Collections.Generic;

namespace MystiaModManager.Logic;

public sealed class GameLaunchWatch
{
    private bool _armed;
    private int? _pid;

    public bool Armed => _armed;

    public int? OwnedPid => _pid;

    public void Arm()
    {
        _armed = true;
        _pid = null;
    }

    public bool? Tick(IReadOnlyList<int> runningPids, Func<int, int?> exitCodeOf)
    {
        if (!_armed) return null;

        if (_pid == null)
        {
            if (runningPids.Count == 0) return null;
            _pid = runningPids[0];
            return null;
        }

        for (var i = 0; i < runningPids.Count; i++)
        {
            if (runningPids[i] == _pid.Value)
                return null;
        }

        var code = exitCodeOf(_pid.Value);
        if (code == null) return null;

        _armed = false;
        _pid = null;
        return code.Value != 0;
    }
}
