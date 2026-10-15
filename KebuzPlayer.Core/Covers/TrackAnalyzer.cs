using NAudio.Dsp;
using NAudio.Wave;

namespace KebuzPlayer.Services;

public static class TrackAnalyzer
{
    private const int FftSize = 2048;
    private const int M = 11;
    private const int Hop = 1024;
    private const int UsableBins = FftSize / 2;
    private const int FreqBins = 512;
    private const int SegmentCount = 12;
    private const int FramesPerSegment = 120;
    private const float LoHz = 20f;
    private const float HiHz = 16000f;
    private const float HpssMaxHz = 10000f;

    public static TrackFeatures Analyze(string filePath)
    {
        try { return AnalyzeCore(filePath); }
        catch { return TrackFeatures.Default; }
    }

    private static TrackFeatures AnalyzeCore(string filePath)
    {
        using var reader = new AudioFileReader(filePath);
        int ch = reader.WaveFormat.Channels;
        int sampleRate = reader.WaveFormat.SampleRate;
        int bytesPerFrame = reader.WaveFormat.BitsPerSample / 8 * ch;
        long totalFrames = reader.Length / bytesPerFrame;
        float durSec = (float)reader.TotalTime.TotalSeconds;

        int segLenFrames = Hop * (FramesPerSegment - 1) + FftSize;
        if (totalFrames < FftSize * 4 || durSec < 2f)
            return TrackFeatures.Default;

        float binHz = (float)sampleRate / FftSize;
        var hann = BuildHann();
        var fftBuf = new Complex[FftSize];
        var segments = new List<SegmentFeatures>(SegmentCount);
        var trackSpec = new float[UsableBins];
        long trackSpecFrames = 0;
        var allFlux = new List<float>(SegmentCount * FramesPerSegment);
        var frameRms = new List<float>(SegmentCount * FramesPerSegment);
        int totalOnsets = 0;
        float totalSegSec = 0f;

        for (int s = 0; s < SegmentCount; s++)
        {
            double frac = 0.04 + s * (0.92 / (SegmentCount - 1));
            long start = (long)(totalFrames * frac) - segLenFrames / 2;
            start = Math.Clamp(start, 0, Math.Max(0, totalFrames - segLenFrames));

            int wantFrames = (int)Math.Min(segLenFrames, totalFrames - start);
            var interleaved = new float[wantFrames * ch];
            reader.Position = start * bytesPerFrame;
            int readSamples = ReadFully(reader, interleaved);
            int frames = readSamples / ch;
            int numFft = frames < FftSize ? 0 : (frames - FftSize) / Hop + 1;
            if (numFft > FramesPerSegment) numFft = FramesPerSegment;
            if (numFft < 8) continue;

            var mono = new float[frames];
            double sumSq = 0;
            for (int i = 0; i < frames; i++)
            {
                float v = 0f;
                for (int c = 0; c < ch; c++) v += interleaved[i * ch + c];
                v /= ch;
                mono[i] = v;
                sumSq += v * v;
            }
            float energy = MathF.Sqrt((float)(sumSq / frames));

            var prefix = new double[frames + 1];
            for (int i = 0; i < frames; i++)
                prefix[i + 1] = prefix[i] + (double)mono[i] * mono[i];

            var spec = new float[numFft][];
            for (int t = 0; t < numFft; t++)
            {
                int off = t * Hop;
                for (int i = 0; i < FftSize; i++)
                {
                    fftBuf[i].X = mono[off + i] * hann[i];
                    fftBuf[i].Y = 0f;
                }
                FastFourierTransform.FFT(true, M, fftBuf);
                var row = new float[UsableBins];
                for (int i = 0; i < UsableBins; i++)
                    row[i] = MathF.Sqrt(fftBuf[i].X * fftBuf[i].X + fftBuf[i].Y * fftBuf[i].Y);
                spec[t] = row;

                for (int i = 0; i < UsableBins; i++) trackSpec[i] += row[i];
                frameRms.Add(MathF.Sqrt((float)((prefix[off + FftSize] - prefix[off]) / FftSize)));
            }
            trackSpecFrames += numFft;

            float segDurSec = (float)frames / sampleRate;
            totalSegSec += segDurSec;
            var seg = ComputeSegment(spec, numFft, energy, segDurSec, binHz, allFlux, out int onsets);
            totalOnsets += onsets;
            segments.Add(seg);
        }

        if (segments.Count < 2 || trackSpecFrames == 0)
            return TrackFeatures.Default;

        for (int i = 0; i < UsableBins; i++) trackSpec[i] /= trackSpecFrames;

        var gridSpec = ResampleToGrid(trackSpec, binHz);
        float specPeak = 0f;
        for (int i = 0; i < FreqBins; i++) if (gridSpec[i] > specPeak) specPeak = gridSpec[i];
        if (specPeak > 1e-7f)
            for (int i = 0; i < FreqBins; i++) gridSpec[i] /= specPeak;

        var displaySpec = BuildDisplaySpectrum(gridSpec);

        var energies = segments.Select(x => x.Energy).ToArray();
        float energyMax = Math.Max(energies.Max(), 1e-7f);
        var trajectory = energies.Select(e => e / energyMax).ToArray();

        float energyMean = energies.Average();
        float energyStd = MathF.Sqrt(energies.Sum(e => (e - energyMean) * (e - energyMean)) / energies.Length);
        float energyCv = energyMean > 1e-7f ? Math.Clamp(energyStd / energyMean, 0f, 1f) : 0f;

        var gated = frameRms.Where(r => r > 1e-3f).ToArray();
        if (gated.Length < 16) gated = frameRms.ToArray();
        Array.Sort(gated);
        float p10 = Percentile(gated, 0.10f);
        float p90 = Percentile(gated, 0.90f);
        float dynamicRangeDb = p10 > 1e-7f
            ? Math.Clamp(20f * MathF.Log10(p90 / p10), 0f, 40f)
            : (p90 > 1e-7f ? 40f : 0f);

        double gatedSq = 0;
        foreach (var r in gated) gatedSq += (double)r * r;
        float overallRms = gated.Length > 0 ? (float)Math.Sqrt(gatedSq / gated.Length) : 0f;
        float loudness = overallRms > 1e-7f
            ? Math.Clamp((20f * MathF.Log10(overallRms) + 40f) / 40f, 0f, 1f)
            : 0f;

        float wTotal = Math.Max(energies.Sum(), 1e-7f);
        float Weighted(Func<SegmentFeatures, float> pick) =>
            segments.Sum(x => pick(x) * x.Energy) / wTotal;

        float onsetDensity = Weighted(x => x.OnsetRate);
        float hpRatio = Weighted(x => x.HpRatio);
        float centroid = Weighted(x => x.Centroid);
        float spread = Weighted(x => x.Spread);
        float bass = Weighted(x => x.Bass);
        float mid = Weighted(x => x.Mid);
        float high = Weighted(x => x.High);
        float autocorr = segments.Average(x => x.Autocorrelation);

        var hpRatios = segments.Select(x => x.HpRatio).ToArray();
        float hpMean = hpRatios.Average();
        float hpStd = MathF.Sqrt(hpRatios.Sum(h => (h - hpMean) * (h - hpMean)) / hpRatios.Length);
        float harmonicStability = Math.Clamp(1f - 4f * hpStd, 0f, 1f);

        float fluxMean = allFlux.Count > 0 ? allFlux.Average() : 0f;
        float fluxStd = allFlux.Count > 0
            ? MathF.Sqrt(allFlux.Sum(f => (f - fluxMean) * (f - fluxMean)) / allFlux.Count)
            : 0f;
        float fluxCv = fluxMean > 1e-9f ? fluxStd / fluxMean : 0f;

        float specMean = gridSpec.Average();
        int peaks = 0;
        float pkThreshold = specMean * 3f;
        for (int i = 1; i < FreqBins - 1; i++)
            if (gridSpec[i] > pkThreshold && gridSpec[i] > gridSpec[i - 1] && gridSpec[i] > gridSpec[i + 1])
                peaks++;

        float bandSum = Math.Max(bass + mid + high, 1e-7f);

        return new TrackFeatures
        {
            Spectrum = gridSpec,
            DisplaySpectrum = displaySpec,
            Segments = segments.ToArray(),
            EnergyTrajectory = trajectory,
            DurationSec = durSec,
            DynamicRangeDb = dynamicRangeDb,
            OnsetDensity = onsetDensity,
            EnergyCv = energyCv,
            Bass = bass / bandSum,
            Mid = mid / bandSum,
            High = high / bandSum,
            SpectralFlux = fluxMean,
            FluxStdDev = Math.Clamp(fluxCv, 0f, 3f),
            Autocorrelation = autocorr,
            AttackRate = Math.Clamp(onsetDensity / 5f, 0f, 1f),
            HPRatio = hpRatio,
            HarmonicStability = harmonicStability,
            PercussiveDensity = totalSegSec > 0.5f
                ? Math.Clamp(totalOnsets / totalSegSec / 8f, 0f, 1f)
                : 0f,
            Centroid = Math.Clamp(centroid, 0f, 1f),
            Spread = spread,
            Peaks = Math.Clamp(peaks, 0, 30),
            Rms = loudness,
        };
    }

    private static SegmentFeatures ComputeSegment(
        float[][] spec, int numFft, float energy, float segDurSec, float binHz,
        List<float> allFlux, out int onsetCount)
    {
        var norm = new float[numFft][];
        for (int t = 0; t < numFft; t++)
        {
            float sum = 0f;
            for (int i = 0; i < UsableBins; i++) sum += spec[t][i];
            var row = new float[UsableBins];
            if (sum > 1e-9f)
                for (int i = 0; i < UsableBins; i++) row[i] = spec[t][i] / sum;
            norm[t] = row;
        }

        var flux = new float[numFft - 1];
        for (int t = 1; t < numFft; t++)
        {
            float sum = 0f;
            for (int i = 0; i < UsableBins; i++)
            {
                float d = norm[t][i] - norm[t - 1][i];
                if (d > 0f) sum += d;
            }
            flux[t - 1] = sum;
            allFlux.Add(sum);
        }

        float fMean = flux.Length > 0 ? flux.Average() : 0f;
        float fStd = flux.Length > 0
            ? MathF.Sqrt(flux.Sum(f => (f - fMean) * (f - fMean)) / flux.Length)
            : 0f;
        float threshold = Math.Max(fMean + 2f * fStd, fMean * 1.5f);

        onsetCount = 0;
        for (int t = 0; t < flux.Length; t++)
        {
            bool isPeak = flux[t] > threshold
                && (t == 0 || flux[t] >= flux[t - 1])
                && (t == flux.Length - 1 || flux[t] > flux[t + 1]);
            if (isPeak) onsetCount++;
        }
        float onsetRate = segDurSec > 0.1f ? onsetCount / segDurSec : 0f;

        float corrSum = 0f;
        int corrN = 0;
        for (int t = 1; t < numFft; t++)
        {
            float r = Pearson(spec[t - 1], spec[t], UsableBins);
            if (float.IsFinite(r)) { corrSum += r; corrN++; }
        }
        float autocorr = corrN > 0 ? Math.Clamp(corrSum / corrN, 0f, 1f) : 0f;

        int hpssBins = Math.Clamp((int)MathF.Round(HpssMaxHz / binHz), 8, UsableBins);
        var (hpRatio, harmonicEnergy) = ComputeHpss(spec, numFft, hpssBins);

        var meanPower = new float[UsableBins];
        for (int t = 0; t < numFft; t++)
        {
            var row = spec[t];
            for (int i = 0; i < UsableBins; i++) meanPower[i] += row[i] * row[i];
        }
        for (int i = 0; i < UsableBins; i++) meanPower[i] /= numFft;

        int loBin = Math.Max(1, (int)MathF.Ceiling(LoHz / binHz));
        int hiBin = Math.Min(UsableBins - 1, (int)(HiHz / binHz));
        float invLogRange = 1f / MathF.Log2(HiHz / LoHz);

        double pBass = 0, pMid = 0, pHigh = 0, pTotal = 0, xSum = 0;
        for (int i = loBin; i <= hiBin; i++)
        {
            float freq = i * binHz;
            double p = meanPower[i];
            pTotal += p;
            if (freq < 250f) pBass += p;
            else if (freq < 4000f) pMid += p;
            else pHigh += p;
            xSum += p * (MathF.Log2(freq / LoHz) * invLogRange);
        }
        float centroid = pTotal > 1e-12 ? (float)(xSum / pTotal) : 0.5f;

        double vSum = 0;
        for (int i = loBin; i <= hiBin; i++)
        {
            float d = MathF.Log2(i * binHz / LoHz) * invLogRange - centroid;
            vSum += (double)d * d * meanPower[i];
        }
        float spread = pTotal > 1e-12
            ? Math.Clamp(MathF.Sqrt((float)(vSum / pTotal)) / 0.30f, 0f, 1f)
            : 0f;

        double bandTotal = Math.Max(pBass + pMid + pHigh, 1e-12);

        return new SegmentFeatures
        {
            Energy = energy,
            Centroid = Math.Clamp(centroid, 0f, 1f),
            Flux = fMean,
            OnsetRate = onsetRate,
            HpRatio = hpRatio,
            HarmonicEnergy = harmonicEnergy,
            Autocorrelation = autocorr,
            Bass = (float)(pBass / bandTotal),
            Mid = (float)(pMid / bandTotal),
            High = (float)(pHigh / bandTotal),
            Spread = spread,
        };
    }

    private static (float hpRatio, float harmonicEnergy) ComputeHpss(float[][] spec, int numFrames, int hpssBins)
    {
        const int HWin = 7, FWin = 7;
        const float Eps = 1e-12f;

        var tbuf = new float[HWin];
        var fbuf = new float[FWin];
        float hSum = 0f, pSum = 0f;
        int halfT = HWin / 2, halfF = FWin / 2;

        for (int t = 0; t < numFrames; t++)
        {
            for (int f = 0; f < hpssBins; f++)
            {
                for (int k = 0; k < HWin; k++)
                    tbuf[k] = spec[Math.Clamp(t - halfT + k, 0, numFrames - 1)][f];
                float h = Median(tbuf);

                for (int k = 0; k < FWin; k++)
                    fbuf[k] = spec[t][Math.Clamp(f - halfF + k, 0, hpssBins - 1)];
                float p = Median(fbuf);

                float h2 = h * h, p2 = p * p;
                float tot = h2 + p2 + Eps;
                float power = spec[t][f] * spec[t][f];
                hSum += power * h2 / tot;
                pSum += power * p2 / tot;
            }
        }

        float hpRatio = (hSum + pSum) > Eps ? hSum / (hSum + pSum) : 0.5f;
        return (hpRatio, hSum / (numFrames * hpssBins));
    }

    private static float[] ResampleToGrid(float[] avgSpec, float binHz)
    {
        var grid = new float[FreqBins];
        float gridHz = HiHz / FreqBins;
        int last = avgSpec.Length - 1;
        for (int i = 0; i < FreqBins; i++)
        {
            float pos = i * gridHz / binHz;
            int lo = (int)pos;
            if (lo >= last)
            {
                grid[i] = pos <= last ? avgSpec[last] : 0f;
                continue;
            }
            float frac = pos - lo;
            grid[i] = avgSpec[lo] * (1f - frac) + avgSpec[lo + 1] * frac;
        }
        return grid;
    }

    private static float[] BuildDisplaySpectrum(float[] normalizedSpec)
    {
        var displaySpec = (float[])normalizedSpec.Clone();
        const int DispBands = 8;
        for (int b = 0; b < DispBands; b++)
        {
            int lo = Math.Clamp((int)MathF.Pow(FreqBins, (float)b / DispBands), 0, FreqBins - 1);
            int hi = Math.Clamp((int)MathF.Pow(FreqBins, (float)(b + 1) / DispBands), lo, FreqBins - 1);
            if (lo == hi) continue;
            float bPeak = 0f;
            for (int f = lo; f <= hi; f++) if (displaySpec[f] > bPeak) bPeak = displaySpec[f];
            if (bPeak < 1e-8f) continue;
            for (int f = lo; f <= hi; f++) displaySpec[f] /= bPeak;
        }
        return displaySpec;
    }

    private static int ReadFully(AudioFileReader reader, float[] buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = reader.Read(buffer, total, buffer.Length - total);
            if (read <= 0) break;
            total += read;
        }
        return total;
    }

    private static float Percentile(float[] sorted, float p)
    {
        if (sorted.Length == 0) return 0f;
        float pos = p * (sorted.Length - 1);
        int lo = (int)pos;
        int hi = Math.Min(lo + 1, sorted.Length - 1);
        float frac = pos - lo;
        return sorted[lo] * (1f - frac) + sorted[hi] * frac;
    }

    private static float Pearson(float[] a, float[] b, int n)
    {
        float ma = 0f, mb = 0f;
        for (int i = 0; i < n; i++) { ma += a[i]; mb += b[i]; }
        ma /= n; mb /= n;

        float num = 0f, da = 0f, db = 0f;
        for (int i = 0; i < n; i++)
        {
            float xa = a[i] - ma, xb = b[i] - mb;
            num += xa * xb; da += xa * xa; db += xb * xb;
        }
        float denom = MathF.Sqrt(da * db);
        return denom < 1e-9f ? 0f : num / denom;
    }

    private static float Median(float[] buf)
    {
        for (int i = 1; i < buf.Length; i++)
        {
            float key = buf[i];
            int j = i - 1;
            while (j >= 0 && buf[j] > key) { buf[j + 1] = buf[j]; j--; }
            buf[j + 1] = key;
        }
        return buf[buf.Length / 2];
    }

    private static float[] BuildHann()
    {
        var w = new float[FftSize];
        for (int i = 0; i < FftSize; i++)
            w[i] = (float)(0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (FftSize - 1)));
        return w;
    }
}
