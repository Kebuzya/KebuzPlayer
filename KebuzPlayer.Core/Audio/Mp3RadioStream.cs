using NAudio.Wave;

namespace KebuzPlayer.Services;

internal sealed class Mp3RadioStream : IWaveProvider, IDisposable
{
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Prebuffer = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan HighWaterMark = TimeSpan.FromSeconds(2);
    private static readonly HttpClient Client = new(new SocketsHttpHandler { MaxResponseDrainSize = 0 })
    {
        Timeout = NetworkTimeout,
    };

    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _pump;
    private BufferedWaveProvider? _buffer;
    private RadioCapture? _capture;
    private int _completed;
    private int _disposed;

    public Task Ready => _ready.Task;
    public WaveFormat WaveFormat => _buffer!.WaveFormat;
    public string? StationName { get; private set; }

    public Mp3RadioStream(Uri address)
    {
        _pump = Task.Run(() => PumpAsync(address));
    }

    private async Task PumpAsync(Uri address)
    {
        var cancellation = _stop.Token;
        try
        {
            using var opening = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            opening.CancelAfter(TimeSpan.FromSeconds(15));
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            request.Headers.TryAddWithoutValidation("Icy-MetaData", "0");
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, opening.Token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            if (mediaType is not (null or "audio/mpeg" or "audio/mp3" or "audio/x-mp3" or "audio/x-mpeg"
                                  or "application/octet-stream")
                || response.Headers.Contains("icy-metaint"))
                throw new InvalidDataException();

            if (response.Headers.TryGetValues("icy-name", out var names))
                StationName = names.FirstOrDefault()?.Trim();

            using var body = await response.Content.ReadAsStreamAsync(opening.Token).ConfigureAwait(false);
            using var input = new RadioInput(body, opening.Token);
            var frame = input.ReadFrame();
            var channels = frame.ChannelMode == ChannelMode.Mono ? 1 : 2;
            var format = new Mp3WaveFormat(frame.SampleRate, channels, frame.FrameLength, frame.BitRate);
            using var decoder = new AcmMp3FrameDecompressor(format);
            var buffer = new BufferedWaveProvider(decoder.OutputFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(3),
                ReadFully = false,
            };
            _buffer = buffer;
            var bytes = new byte[16 * 1024];
            var ready = false;
            while (true)
            {
                opening.Token.ThrowIfCancellationRequested();
                if (frame.SampleRate != buffer.WaveFormat.SampleRate
                    || (frame.ChannelMode == ChannelMode.Mono ? 1 : 2) != buffer.WaveFormat.Channels)
                    throw new InvalidDataException();

                var decoded = decoder.DecompressFrame(frame, bytes, 0);
                buffer.AddSamples(bytes, 0, decoded);
                if (!ready && buffer.BufferedDuration >= Prebuffer)
                {
                    opening.CancelAfter(Timeout.InfiniteTimeSpan);
                    ready = true;
                    _ready.TrySetResult();
                }

                while (buffer.BufferedDuration >= HighWaterMark)
                    await Task.Delay(50, cancellation).ConfigureAwait(false);

                frame = input.ReadFrame();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            _ready.TrySetCanceled(cancellation);
        }
        catch (Exception error)
        {
            _ready.TrySetException(error);
        }
        finally
        {
            Volatile.Write(ref _completed, 1);
            Volatile.Read(ref _capture)?.Cancel();
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        var completed = Volatile.Read(ref _completed) != 0;
        var read = _buffer!.Read(buffer, offset, count);
        if (read > 0) Volatile.Read(ref _capture)?.Append(buffer.AsSpan(offset, read));
        if (completed) return read;
        Array.Clear(buffer, offset + read, count - read);
        return count;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Volatile.Read(ref _capture)?.Cancel();
        _stop.Cancel();
        _ = _pump.ContinueWith(_ => _stop.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public async Task<WaveStream> CaptureAsync(int seconds, IProgress<int> progress, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var format = WaveFormat;
        var capture = new RadioCapture(format.AverageBytesPerSecond, seconds, progress);
        if (Interlocked.CompareExchange(ref _capture, capture, null) != null)
            throw new InvalidOperationException();

        try
        {
            if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _completed) != 0)
                capture.Cancel();
            var bytes = await capture.Completed.WaitAsync(cancellation).ConfigureAwait(false);
            return new RawSourceWaveStream(new MemoryStream(bytes, writable: false), format);
        }
        finally
        {
            Interlocked.CompareExchange(ref _capture, null, capture);
        }
    }

    private sealed class RadioCapture(int bytesPerSecond, int seconds, IProgress<int> progress)
    {
        private readonly byte[] _samples = new byte[checked(bytesPerSecond * seconds)];
        private readonly TaskCompletionSource<byte[]> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _written;

        public Task<byte[]> Completed => _result.Task;

        public void Append(ReadOnlySpan<byte> samples)
        {
            var remaining = _samples.Length - _written;
            if (remaining == 0 || _result.Task.IsCompleted) return;

            var count = Math.Min(remaining, samples.Length);
            samples[..count].CopyTo(_samples.AsSpan(_written));
            var oldSecond = _written / bytesPerSecond;
            _written += count;
            var second = _written / bytesPerSecond;
            if (second != oldSecond) progress.Report(second);
            if (_written == _samples.Length) _result.TrySetResult(_samples);
        }

        public void Cancel() => _result.TrySetCanceled();
    }

    private sealed class RadioInput(Stream source, CancellationToken cancellation) : Stream
    {
        private readonly CancellationTokenSource _read = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        private readonly byte[] _chunk = new byte[4096];
        private int _start;
        private int _end;
        private long _position;
        private long _frameLimit;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public Mp3Frame ReadFrame()
        {
            _frameLimit = _position + 64 * 1024;
            var frame = Mp3Frame.LoadFromStream(this) ?? throw new EndOfStreamException();
            if (frame.MpegLayer != MpegLayer.Layer3) throw new InvalidDataException();
            return frame;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position + count > _frameLimit) throw new InvalidDataException();

            var copied = 0;
            while (copied < count)
            {
                if (_start == _end)
                {
                    _read.CancelAfter(NetworkTimeout);
                    _end = source.ReadAsync(_chunk.AsMemory(), _read.Token).AsTask().GetAwaiter().GetResult();
                    _read.CancelAfter(Timeout.InfiniteTimeSpan);
                    _start = 0;
                    if (_end == 0) break;
                }

                var size = Math.Min(count - copied, _end - _start);
                _chunk.AsSpan(_start, size).CopyTo(buffer.AsSpan(offset + copied));
                _start += size;
                copied += size;
            }
            _position += copied;
            return copied;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _read.Dispose();
            base.Dispose(disposing);
        }
    }
}
