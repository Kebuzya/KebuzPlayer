namespace KebuzPlayer.Infrastructure;

public static class StableHash
{
    private const uint OffsetBasis = 2166136261;
    private const uint Prime = 16777619;

    public static int Fnv1a(string text)
    {
        uint h = OffsetBasis;
        foreach (char c in text)
        {
            h = (h ^ (byte)c) * Prime;
            h = (h ^ (byte)(c >> 8)) * Prime;
        }
        return unchecked((int)h);
    }

    public static int Fnv1a(ReadOnlySpan<byte> data)
    {
        uint h = OffsetBasis;
        foreach (byte b in data)
            h = (h ^ b) * Prime;
        return unchecked((int)h);
    }

    public static int Combine(params int[] hashes)
    {
        uint h = OffsetBasis;
        foreach (int value in hashes)
        {
            h = (h ^ (byte)value) * Prime;
            h = (h ^ (byte)(value >> 8)) * Prime;
            h = (h ^ (byte)(value >> 16)) * Prime;
            h = (h ^ (byte)(value >> 24)) * Prime;
        }
        return unchecked((int)h);
    }
}
