using System;
using System.IO;
using System.Text;
using KebuzPlayer.Services;

namespace KebuzPlayer.Core.Tests;

public class AudioReaderFactoryTests : IDisposable
{
    private readonly string _dir;

    public AudioReaderFactoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "KebuzTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Classify_OggVorbisHeader_IsOgg()
    {
        var head = new byte[] { 0x4F, 0x67, 0x67, 0x53, 0x00, 0x02, 0, 0, 0, 0, 0, 0 };
        Assert.Equal(AudioContainer.Ogg, AudioReaderFactory.Classify(head));
    }

    [Fact]
    public void Classify_Id3Header_IsMpeg()
    {
        var head = new byte[] { 0x49, 0x44, 0x33, 0x03, 0, 0, 0, 0x21, 0x7E, 0x12, 0x54, 0x50 };
        Assert.Equal(AudioContainer.Mpeg, AudioReaderFactory.Classify(head));
    }

    [Fact]
    public void Classify_BareMpegFrameSync_IsMpeg()
    {
        var head = new byte[] { 0xFF, 0xFB, 0x90, 0x64, 0, 0, 0, 0, 0, 0, 0, 0 };
        Assert.Equal(AudioContainer.Mpeg, AudioReaderFactory.Classify(head));
    }

    [Fact]
    public void Classify_RiffWaveHeader_IsRiff()
    {
        var head = Encoding.ASCII.GetBytes("RIFF$\0\0\0WAVE");
        Assert.Equal(AudioContainer.Riff, AudioReaderFactory.Classify(head));
    }

    [Fact]
    public void Classify_HtmlPage_IsUnknown()
    {
        var head = Encoding.ASCII.GetBytes("\n\n\n\n\n\n<!doctype");
        Assert.Equal(AudioContainer.Unknown, AudioReaderFactory.Classify(head));
    }

    [Fact]
    public void Sniff_OggContentUnderMp3Extension_IsOgg()
    {
        var path = Path.Combine(_dir, "liar.mp3");
        File.WriteAllBytes(path, new byte[] { 0x4F, 0x67, 0x67, 0x53, 0x00, 0x02, 0, 0, 0, 0, 0, 0, 0, 0 });
        Assert.Equal(AudioContainer.Ogg, AudioReaderFactory.Sniff(path));
    }

    [Fact]
    public void Sniff_MissingFile_IsUnknown()
    {
        Assert.Equal(AudioContainer.Unknown, AudioReaderFactory.Sniff(Path.Combine(_dir, "nope.mp3")));
    }

    [Fact]
    public void Open_NonAudioFile_Throws()
    {
        var path = Path.Combine(_dir, "page.mp3");
        File.WriteAllText(path, "<!doctype html><html></html>");
        Assert.ThrowsAny<Exception>(() => AudioReaderFactory.Open(path));
    }
}
