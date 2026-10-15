using System;
using System.IO;
using NAudio.Vorbis;
using NAudio.Wave;

namespace KebuzPlayer.Services;

public enum AudioContainer { Unknown, Ogg, Riff, Mpeg, Flac, Mp4 }

public static class AudioReaderFactory
{
    public static WaveStream Open(string path)
    {
        if (Sniff(path) == AudioContainer.Ogg)
            return new VorbisWaveReader(path);

        try
        {
            return new AudioFileReader(path);
        }
        catch
        {
            if (Sniff(path) == AudioContainer.Unknown) throw;
            return new MediaFoundationReader(path);
        }
    }

    public static AudioContainer Sniff(string path)
    {
        Span<byte> head = stackalloc byte[12];
        int read;
        try
        {
            using var fs = File.OpenRead(path);
            read = fs.Read(head);
        }
        catch (IOException)
        {
            return AudioContainer.Unknown;
        }

        return Classify(head[..read]);
    }

    public static AudioContainer Classify(ReadOnlySpan<byte> head)
    {
        if (Starts(head, "OggS"u8)) return AudioContainer.Ogg;
        if (Starts(head, "RIFF"u8)) return AudioContainer.Riff;
        if (Starts(head, "fLaC"u8)) return AudioContainer.Flac;
        if (Starts(head, "ID3"u8))  return AudioContainer.Mpeg;
        if (head.Length >= 8 && Starts(head[4..], "ftyp"u8)) return AudioContainer.Mp4;
        if (head.Length >= 2 && head[0] == 0xFF && (head[1] & 0xE0) == 0xE0) return AudioContainer.Mpeg;
        return AudioContainer.Unknown;
    }

    private static bool Starts(ReadOnlySpan<byte> head, ReadOnlySpan<byte> magic) =>
        head.Length >= magic.Length && head[..magic.Length].SequenceEqual(magic);
}
