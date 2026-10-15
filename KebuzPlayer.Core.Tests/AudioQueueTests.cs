using System.Collections.Concurrent;
using KebuzPlayer.Models;
using KebuzPlayer.Services;

namespace KebuzPlayer.Core.Tests;

public class AudioQueueTests
{
    private static List<Track> MissingTracks(int count)
    {
        string dir = Path.Combine(Path.GetTempPath(), "KebuzTests_" + Guid.NewGuid().ToString("N"));
        return Enumerable.Range(0, count)
            .Select(i => new Track { Id = i + 1, Path = Path.Combine(dir, $"missing{i}.mp3"), Title = $"t{i}" })
            .ToList();
    }

    private static async Task<List<int>> RunUntilQuietAsync(AudioService audio, ConcurrentQueue<int> failed, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (failed.Count < expected && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        int seen = failed.Count;
        for (int i = 0; i < 30 && failed.Count == seen; i++)
            await Task.Delay(10);
        audio.Dispose();
        return failed.ToList();
    }

    private static (AudioService Audio, ConcurrentQueue<int> Failed) Watch()
    {
        var audio = new AudioService();
        var failed = new ConcurrentQueue<int>();
        audio.TrackOpenFailed += (_, t) => failed.Enqueue(t.Id);
        return (audio, failed);
    }

    [Fact]
    public async Task UnopenableQueue_TriesEachTrackOnceAndStops()
    {
        var (audio, failed) = Watch();
        var queue = MissingTracks(3);
        audio.SetQueue(queue, 0);
        audio.Play(queue[0]);

        var ids = await RunUntilQuietAsync(audio, failed, 3);

        Assert.Equal([1, 2, 3], ids);
    }

    [Fact]
    public async Task UnopenableQueue_WithRepeatAll_StillStops()
    {
        var (audio, failed) = Watch();
        audio.SetRepeatMode(RepeatMode.RepeatAll);
        var queue = MissingTracks(3);
        audio.SetQueue(queue, 1);
        audio.Play(queue[1]);

        var ids = await RunUntilQuietAsync(audio, failed, 3);

        Assert.Equal([2, 3, 1], ids);
    }

    [Fact]
    public async Task FailureOnLastTrack_WithoutRepeat_DoesNotWrapToStart()
    {
        var (audio, failed) = Watch();
        var queue = MissingTracks(3);
        audio.SetQueue(queue, 2);
        audio.Play(queue[2]);

        var ids = await RunUntilQuietAsync(audio, failed, 1);

        Assert.Equal([3], ids);
    }

    [Fact]
    public async Task Shuffle_StartsFromTheChosenTrackAndCoversTheWholeQueue()
    {
        var (audio, failed) = Watch();
        audio.SetShuffle(true);
        var queue = MissingTracks(20);
        audio.SetQueue(queue, 7);
        audio.Play(queue[7]);

        var ids = await RunUntilQuietAsync(audio, failed, 20);

        Assert.Equal(20, ids.Count);
        Assert.Equal(8, ids[0]);
        Assert.Equal(20, ids.Distinct().Count());
    }
}
