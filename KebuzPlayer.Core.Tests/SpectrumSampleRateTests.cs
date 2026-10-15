using KebuzPlayer.Services;

namespace KebuzPlayer.Core.Tests;

public class SpectrumSampleRateTests
{
    private static int BarOfTone(float hz, int sampleRate)
    {
        float binHz = (float)sampleRate / FftSampleProvider.FftSizeFor(sampleRate);
        var spectrum = new float[FftSampleProvider.SpectrumSize];
        spectrum[(int)Math.Round(hz / binHz)] = 0.08f;
        var processor = new SpectrumProcessor();
        processor.Update(spectrum, 1f / 30f, binHz);
        int best = 0;
        for (int i = 1; i < SpectrumProcessor.BarCount; i++)
            if (processor.Smooth[i] > processor.Smooth[best]) best = i;
        return best;
    }

    [Theory]
    [InlineData(1000f)]
    [InlineData(10000f)]
    public void SameTone_LandsInTheSameBar_AtAnySampleRate(float hz)
    {
        int reference = BarOfTone(hz, 44100);

        Assert.InRange(BarOfTone(hz, 48000), reference - 1, reference + 1);
        Assert.InRange(BarOfTone(hz, 96000), reference - 1, reference + 1);
    }

    [Fact]
    public void LowSampleRate_DoesNotReadPastTheSpectrum()
    {
        var spectrum = new float[FftSampleProvider.SpectrumSize];
        Array.Fill(spectrum, 0.05f);
        var processor = new SpectrumProcessor();

        processor.Update(spectrum, 1f / 30f, 8000f / FftSampleProvider.FftSizeFor(8000));

        Assert.True(processor.HasVisibleContent);
    }
}
