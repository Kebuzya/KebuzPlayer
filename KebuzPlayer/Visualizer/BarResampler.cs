using System;

namespace KebuzPlayer.Visualizer;

public static class BarResampler
{
    public static float Level(float[] source, int index, int count)
    {
        int n = source.Length;
        if (count <= 0 || n == 0) return 0f;
        if (count == n) return source[index];

        if (count < n)
        {
            int start = index * n / count;
            int end   = Math.Max(start + 1, (index + 1) * n / count);
            float v = 0f;
            for (int i = start; i < end && i < n; i++)
                if (source[i] > v) v = source[i];
            return v;
        }

        float pos = count == 1 ? 0f : (float)index * (n - 1) / (count - 1);
        int   i0  = (int)pos;
        int   i1  = Math.Min(i0 + 1, n - 1);
        float t   = pos - i0;
        return source[i0] + (source[i1] - source[i0]) * t;
    }
}
