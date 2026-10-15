using KebuzPlayer.Infrastructure;
using KebuzPlayer.Models;
using KebuzPlayer.Services;

namespace KebuzPlayer.Core.Tests;

public class LaunchBatcherTests
{
    private static readonly DateTime T0 = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ProcessesStartedTogether_FormOneBatch_EvenOutOfOrder()
    {
        var batcher = new LaunchBatcher();

        Assert.False(batcher.Joins(T0));
        Assert.True(batcher.Joins(T0.AddMilliseconds(40)));
        Assert.True(batcher.Joins(T0.AddMilliseconds(-30)));
        Assert.True(batcher.Joins(T0.AddMilliseconds(170)));
    }

    [Fact]
    public void TracksOpenedOneAfterAnother_StartNewBatches()
    {
        var batcher = new LaunchBatcher();

        Assert.False(batcher.Joins(T0));
        Assert.False(batcher.Joins(T0.AddMilliseconds(400)));
        Assert.False(batcher.Joins(T0.AddMilliseconds(900)));
    }

    [Fact]
    public async Task Enqueue_AppendsToTheEndOfPlayback()
    {
        var audio = new AudioService();
        var failed = new System.Collections.Concurrent.ConcurrentQueue<int>();
        audio.TrackOpenFailed += (_, t) => failed.Enqueue(t.Id);
        string dir = Path.Combine(Path.GetTempPath(), "KebuzTests_" + Guid.NewGuid().ToString("N"));
        Track T(int id) => new() { Id = id, Path = Path.Combine(dir, $"m{id}.mp3"), Title = $"t{id}" };

        audio.SetQueue([T(1)], 0);
        audio.Enqueue(T(2));
        audio.Enqueue(T(3));
        audio.Play(audio.Queue[0]);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (failed.Count < 3 && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        audio.Dispose();

        Assert.Equal([1, 2, 3], failed.ToList());
    }
}
