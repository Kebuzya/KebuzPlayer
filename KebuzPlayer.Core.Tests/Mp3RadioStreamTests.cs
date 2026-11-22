using System.Net;
using System.Net.Sockets;
using KebuzPlayer.Models;
using KebuzPlayer.Services;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace KebuzPlayer.Core.Tests;

public class Mp3RadioStreamTests
{
    [Fact]
    public async Task OpenStream_BuffersAudioForVisualizerAndClosesOnStop()
    {
        var mp3 = CreateTestAudio();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var radio = new Mp3RadioStream(AddressOf(listener));
        using var client = await listener.AcceptTcpClientAsync(timeout.Token);
        using var connection = client.GetStream();
        await ReadRequestAsync(connection, timeout.Token);
        await connection.WriteAsync(
            "HTTP/1.1 200 OK\r\nContent-Type: audio/mpeg\r\nicy-name: Test station\r\nConnection: close\r\n\r\n"u8.ToArray(),
            timeout.Token);
        await connection.WriteAsync(mp3.AsMemory(0, 3), timeout.Token);
        await connection.WriteAsync(mp3.AsMemory(3), timeout.Token);

        await radio.Ready.WaitAsync(timeout.Token);
        Assert.Equal("Test station", radio.StationName);
        var fft = new FftSampleProvider(radio.ToSampleProvider());
        var samples = new float[4096];
        Assert.Equal(samples.Length, fft.Read(samples, 0, samples.Length));
        Assert.Contains(samples, sample => Math.Abs(sample) > 0.05f);
        var spectrum = new float[FftSampleProvider.SpectrumSize];
        fft.CopySpectrum(spectrum);
        Assert.Contains(spectrum, bin => bin > 0);

        radio.Dispose();
        Assert.Equal(0, await connection.ReadAsync(new byte[1], timeout.Token));
    }

    [Fact]
    public async Task CaptureFromRadio_ProvidesSeekableAudioForAnalysisAndCancelsOnStop()
    {
        var mp3 = CreateTestAudio(seconds: 3);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var radio = new Mp3RadioStream(AddressOf(listener));
        using var client = await listener.AcceptTcpClientAsync(timeout.Token);
        using var connection = client.GetStream();
        await ReadRequestAsync(connection, timeout.Token);
        await connection.WriteAsync(
            "HTTP/1.1 200 OK\r\nContent-Type: audio/mpeg\r\nConnection: close\r\n\r\n"u8.ToArray(),
            timeout.Token);
        await connection.WriteAsync(mp3, timeout.Token);
        await radio.Ready.WaitAsync(timeout.Token);

        var progress = new CaptureProgress();
        using var cancelCapture = new CancellationTokenSource();
        var canceled = radio.CaptureAsync(2, progress, cancelCapture.Token);
        cancelCapture.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.Equal(0, progress.Seconds);

        var capture = radio.CaptureAsync(2, progress, timeout.Token);
        var block = new byte[radio.WaveFormat.BlockAlign * 1024];
        while (!capture.IsCompleted)
        {
            radio.Read(block, 0, block.Length);
            await Task.Delay(1, timeout.Token);
        }

        using var clip = await capture;
        Assert.Equal(TimeSpan.FromSeconds(2), clip.TotalTime);
        Assert.Equal(2, progress.Seconds);
        var features = TrackAnalyzer.Analyze(clip, timeout.Token);
        Assert.False(features.IsFallback);
        var cover = CoverArtGenerator.Generate(CoverArtStyle.SpectrumArt, CoverArtGenerator.FeatureSeed(features), features);
        Assert.Equal(new byte[] { 0xff, 0xd8 }, cover[..2]);

        var pending = radio.CaptureAsync(30, progress, timeout.Token);
        radio.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.False(timeout.IsCancellationRequested);
    }

    [Fact]
    public async Task StopDuringConnection_CancelsPendingPlayback()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var radio = new Mp3RadioStream(AddressOf(listener));
        using var client = await listener.AcceptTcpClientAsync(timeout.Token);
        using var connection = client.GetStream();
        await ReadRequestAsync(connection, timeout.Token);

        radio.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => radio.Ready.WaitAsync(timeout.Token));
        Assert.True(radio.Ready.IsCanceled);
        Assert.Equal(0, await connection.ReadAsync(new byte[1], timeout.Token));
    }

    [Fact]
    public async Task OpenWebPage_RejectsNonMp3Response()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var radio = new Mp3RadioStream(AddressOf(listener));
        using var client = await listener.AcceptTcpClientAsync(timeout.Token);
        using var connection = client.GetStream();
        await ReadRequestAsync(connection, timeout.Token);
        await connection.WriteAsync(
            "HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: 0\r\n\r\n"u8.ToArray(),
            timeout.Token);

        await Assert.ThrowsAsync<InvalidDataException>(() => radio.Ready.WaitAsync(timeout.Token));
    }

    [Fact]
    public async Task SelectLocalTrackWhileConnecting_ClosesRadioAndKeepsQueue()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        using var audio = new AudioService();
        var track = new Track { Id = 1 };
        audio.SetQueue([track]);
        listener.Start();
        var opening = audio.PlayRadioAsync(AddressOf(listener), "Radio", -6.5f);
        Assert.Equal(-6.5f, audio.CurrentTrack!.UserGainOffset);
        using var client = await listener.AcceptTcpClientAsync(timeout.Token);
        using var connection = client.GetStream();
        await ReadRequestAsync(connection, timeout.Token);

        audio.RestoreTrackState(track);

        await opening.WaitAsync(timeout.Token);
        Assert.Equal(0, await connection.ReadAsync(new byte[1], timeout.Token));
        Assert.False(audio.IsRadio);
        Assert.False(audio.IsRadioActive);
        Assert.Same(track, audio.CurrentTrack);
        Assert.Same(track, Assert.Single(audio.Queue));
    }

    private static Uri AddressOf(TcpListener listener) =>
        new($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/radio");

    private static async Task ReadRequestAsync(NetworkStream connection, CancellationToken cancellation)
    {
        using var reader = new StreamReader(connection, leaveOpen: true);
        while (await reader.ReadLineAsync(cancellation) is { Length: > 0 })
        {
        }
    }

    private sealed class CaptureProgress : IProgress<int>
    {
        public int Seconds { get; private set; }
        public void Report(int value) => Seconds = value;
    }

    private static byte[] CreateTestAudio(int seconds = 1)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"KebuzTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "tone.mp3");
        try
        {
            var tone = new SignalGenerator(44100, 2) { Frequency = 440, Gain = 0.5 };
            MediaFoundationEncoder.EncodeToMp3(tone.Take(TimeSpan.FromSeconds(seconds)).ToWaveProvider16(), path);
            return File.ReadAllBytes(path);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }
}
