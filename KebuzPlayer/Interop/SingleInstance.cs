using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Pipes;
using System.Threading;

namespace KebuzPlayer.Interop;

public static class SingleInstance
{
    private const string MutexName = "KebuzPlayer.SingleInstance";
    private const string PipeName  = "KebuzPlayer.Activate";

    private static Mutex? _mutex;

    public static bool TryAcquire()
    {
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
        }
        return createdNew;
    }

    private const int ConnectTimeoutMs = 5000;

    public static void SignalExisting(IReadOnlyList<string> files, DateTime startedUtc)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(ConnectTimeoutMs);
            using var writer = new StreamWriter(client);
            writer.WriteLine(startedUtc.Ticks);
            foreach (var file in files)
                writer.WriteLine(file);
        }
        catch { }
    }

    public static void StartServer(Action<IReadOnlyList<string>, DateTime> onActivate)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server);
                    var lines = reader.ReadToEnd().Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    var started = lines.Length > 0 && long.TryParse(lines[0], out long ticks)
                        ? new DateTime(ticks, DateTimeKind.Utc)
                        : DateTime.UtcNow;
                    onActivate(lines.Skip(1).ToArray(), started);
                }
                catch { Thread.Sleep(500); }
            }
        })
        {
            IsBackground = true,
            Name = "KebuzPlayer.SingleInstance",
        };
        thread.Start();
    }

    public static void Release()
    {
        _mutex?.Dispose();
        _mutex = null;
    }
}
