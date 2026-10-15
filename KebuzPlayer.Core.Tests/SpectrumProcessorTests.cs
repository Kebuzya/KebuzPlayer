using KebuzPlayer.Services;

namespace KebuzPlayer.Core.Tests;

public class SpectrumProcessorTests
{
    private const float Frame30 = 1f / 30f;

    private static float[] Loud()
    {
        var spectrum = new float[1024];
        for (int i = 0; i < spectrum.Length; i++)
            spectrum[i] = 0.5f;
        return spectrum;
    }

    private static SpectrumProcessor Excited()
    {
        var processor = new SpectrumProcessor();
        var loud = Loud();
        for (int i = 0; i < 20; i++)
            processor.Update(loud, Frame30);
        return processor;
    }

    [Fact]
    public void Decay_DependsOnElapsedTimeNotFrameCount()
    {
        var silence = new float[1024];

        var oneLongStep = Excited();
        oneLongStep.Update(silence, Frame30 * 2);

        var twoShortSteps = Excited();
        twoShortSteps.Update(silence, Frame30);
        twoShortSteps.Update(silence, Frame30);

        for (int i = 0; i < SpectrumProcessor.BarCount; i++)
            Assert.Equal(twoShortSteps.Smooth[i], oneLongStep.Smooth[i], 4);
    }

    [Fact]
    public void PeakDecay_DependsOnElapsedTimeNotFrameCount()
    {
        var silence = new float[1024];

        var oneLongStep = Excited();
        oneLongStep.Update(silence, Frame30 * 3);

        var threeShortSteps = Excited();
        for (int i = 0; i < 3; i++)
            threeShortSteps.Update(silence, Frame30);

        for (int i = 0; i < SpectrumProcessor.BarCount; i++)
            Assert.Equal(threeShortSteps.Peaks[i], oneLongStep.Peaks[i], 4);
    }

    [Fact]
    public void Attack_DependsOnElapsedTimeNotFrameCount()
    {
        var loud = Loud();

        var oneLongStep = new SpectrumProcessor();
        oneLongStep.Update(loud, Frame30 * 2);

        var twoShortSteps = new SpectrumProcessor();
        twoShortSteps.Update(loud, Frame30);
        twoShortSteps.Update(loud, Frame30);

        for (int i = 0; i < SpectrumProcessor.BarCount; i++)
            Assert.Equal(twoShortSteps.Smooth[i], oneLongStep.Smooth[i], 4);
    }

    [Fact]
    public void DefaultOverload_MatchesThirtyFramesPerSecond()
    {
        var loud = Loud();

        var implicitStep = new SpectrumProcessor();
        implicitStep.Update(loud);

        var explicitStep = new SpectrumProcessor();
        explicitStep.Update(loud, Frame30);

        for (int i = 0; i < SpectrumProcessor.BarCount; i++)
            Assert.Equal(explicitStep.Smooth[i], implicitStep.Smooth[i], 6);
    }

    [Fact]
    public void LongStall_DoesNotCollapseEverythingAtOnce()
    {
        var silence = new float[1024];
        var processor = Excited();
        float before = processor.Smooth[0];

        processor.Update(silence, 30f);

        Assert.True(processor.Smooth[0] < before);
        Assert.True(processor.Smooth[0] > 0f);
    }
}
