using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace MystiaModManager.Logic;

public static class DownloadText
{
    public static string Size(long bytes)
    {
        if (bytes < 0) bytes = 0;
        if (bytes < 1024) return bytes + " B";
        var kb = bytes / 1024.0;
        if (kb < 1024) return kb.ToString("0.#") + " KB";
        return (kb / 1024.0).ToString("0.#") + " MB";
    }

    public static string Detail(long received, long? total)
    {
        if (total is > 0) return Size(received) + " / " + Size(total.Value);
        if (received > 0) return Size(received);
        return "正在连接";
    }

    public static double Percent(long received, long? total)
    {
        if (total is not long n || n <= 0) return 0;
        if (received >= n) return 100;
        return received * 100.0 / n;
    }
}

public sealed class DownloadSnapshot
{
    public DownloadSnapshot(string title, long received, long? total)
    {
        Title = title;
        Received = received;
        Total = total;
    }

    public string Title { get; }
    public long Received { get; }
    public long? Total { get; }
    public string Detail => DownloadText.Detail(Received, Total);
    public double Percent => DownloadText.Percent(Received, Total);
    public bool Unknown => !(Total > 0);
}

public static class DownloadHub
{
    static readonly object Gate = new();
    static readonly List<Job> Items = new();

    public static event Action? Changed;

    public static Job Begin(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) title = "下载";
        var job = new Job(title.Trim());
        lock (Gate) Items.Add(job);
        Changed?.Invoke();
        return job;
    }

    public static DownloadSnapshot[] Snapshot()
    {
        lock (Gate)
        {
            var list = new DownloadSnapshot[Items.Count];
            for (var i = 0; i < Items.Count; i++)
                list[i] = new DownloadSnapshot(Items[i].Title, Items[i].Received, Items[i].Total);
            return list;
        }
    }

    public static async Task CopyAsync(Stream input, Stream output, long? total, Action<long, long?>? progress)
    {
        var buffer = new byte[80 * 1024];
        long received = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            if (read == 0) break;
            await output.WriteAsync(buffer, 0, read).ConfigureAwait(false);
            received += read;
            progress?.Invoke(received, total);
        }
    }

    public sealed class Job : IDisposable
    {
        long _received;
        long? _total;
        int _lastUi;
        bool _armed;

        internal Job(string title) => Title = title;

        public string Title { get; }
        public long Received { get { lock (Gate) return _received; } }
        public long? Total { get { lock (Gate) return _total; } }

        public void Report(long received, long? total)
        {
            bool fire;
            lock (Gate)
            {
                _received = received < 0 ? 0 : received;
                if (total is > 0) _total = total;
                var now = Environment.TickCount;
                fire = !_armed || unchecked(now - _lastUi) >= 150;
                if (fire)
                {
                    _armed = true;
                    _lastUi = now;
                }
            }

            if (fire) Changed?.Invoke();
        }

        public void Dispose()
        {
            bool removed;
            lock (Gate) removed = Items.Remove(this);
            if (removed) Changed?.Invoke();
        }
    }
}
